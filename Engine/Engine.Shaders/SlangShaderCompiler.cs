using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Engine.Core;
using Engine.RHI;

namespace Engine.Shaders;

using Engine.Core;

public sealed record ShaderCompilationOptions
{
    public string Target { get; init; } = "spirv";
    public string Profile { get; init; } = "spirv_1_4";
    public IReadOnlyList<string> Capabilities { get; init; } = [];
    public IReadOnlyList<string> IncludeDirectories { get; init; } = [];
    public bool PreserveEntryPointName { get; init; }
    public bool RowMajorMatrices { get; init; } = true;
    public int OptimizationLevel { get; init; } = 1;
}

public sealed record ShaderCompilationRequest
{
    public required string SourcePath { get; init; }
    public required string EntryPoint { get; init; }
    public required ShaderStage Stage { get; init; }
    public IReadOnlyDictionary<string, string?> Defines { get; init; } = new Dictionary<string, string?>();
    public ShaderCompilationOptions Options { get; init; } = new();
}

public sealed record ShaderCompilationResult
{
    public required ShaderStageCode StageCode { get; init; }
    public required PipelineReflection Reflection { get; init; }
    public required bool FromCache { get; init; }
}

public sealed class ShaderCompilationException : Exception
{
    public string CompilerOutput { get; }

    public ShaderCompilationException(string message, string compilerOutput)
        : base($"{message}{Environment.NewLine}{compilerOutput}")
    {
        CompilerOutput = compilerOutput;
    }
}

public sealed class SlangShaderCompiler
{
    public static readonly string DefaultCacheDirectory = Path.Combine("Cache", "Shaders");

    private readonly ILogger? _log;
    private readonly string _cacheDirectory;
    private readonly string? _slangcPathOverride;
    private string? _resolvedSlangcPath;
    private readonly object _identityLock = new();
    private string? _compilerFileState;
    private string? _compilerIdentity;

    public SlangShaderCompiler(ILogger? log = null, string? cacheDirectory = null, string? slangcPath = null)
    {
        _log = log;
        _cacheDirectory = Path.GetFullPath(cacheDirectory ?? DefaultCacheDirectory);
        _slangcPathOverride = slangcPath;
    }

    private string? ResolveSlangcPath()
    {
        if (_slangcPathOverride is not null)
            return _slangcPathOverride;
        if (_resolvedSlangcPath is not null)
            return _resolvedSlangcPath;
        try
        {
            _resolvedSlangcPath = Path.GetFullPath(LocateSlangCompiler());
        }
        catch (FileNotFoundException)
        {
            _resolvedSlangcPath = null;
        }
        return _resolvedSlangcPath;
    }

    public static string LocateSlangCompiler()
    {
        string? environmentPath = Environment.GetEnvironmentVariable("ENGINE_SLANGC");
        if (!string.IsNullOrEmpty(environmentPath) && File.Exists(environmentPath))
            return Path.GetFullPath(environmentPath);

        string executableName = OperatingSystem.IsWindows() ? "slangc.exe" : "slangc";
        // Prefer the engine's pinned tool over a potentially incompatible SDK installation.
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? directory = new(start);
            for (int i = 0; i < 10 && directory is not null; i++, directory = directory.Parent)
            {
                string candidate = Path.Combine(directory.FullName, "Tools", "Slang", executableName);
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        string? sdkRoot = Environment.GetEnvironmentVariable("VULKAN_SDK");
        if (!string.IsNullOrEmpty(sdkRoot))
        {
            string candidate = Path.Combine(sdkRoot, "Bin", executableName);
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }
        if (OperatingSystem.IsWindows() && Directory.Exists("C:\\VulkanSDK"))
        {
            foreach (string version in Directory.GetDirectories("C:\\VulkanSDK").OrderByDescending(static v => v, StringComparer.OrdinalIgnoreCase))
            {
                string candidate = Path.Combine(version, "Bin", executableName);
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            string candidate = Path.Combine(directory.Trim().Trim('"'), executableName);
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }
        throw new FileNotFoundException(
            "slangc not found. Set ENGINE_SLANGC, place the compiler under Tools/Slang/, or add it to PATH.");
    }

    public ShaderCompilationResult Compile(ShaderCompilationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.EntryPoint);
        string sourceFullPath = Path.GetFullPath(request.SourcePath);
        if (!VirtualFileSystem.Exists(sourceFullPath))
            throw new FileNotFoundException($"Shader source '{request.SourcePath}' not found.", sourceFullPath);
        ValidateOptions(request);

        string cachePath = Path.Combine(_cacheDirectory, ComputeCacheKey(request, sourceFullPath) + ".spv");
        using (Stream? cachedStream = VirtualFileSystem.TryOpenRead(cachePath))
        {
            if (cachedStream is not null)
            {
                try
                {
                    using MemoryStream contents = new();
                    cachedStream.CopyTo(contents);
                    return CreateResult(contents.ToArray(), request, fromCache: true);
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
                {
                    _log?.Debug($"Discarding invalid shader cache: {cachePath} ({exception.Message})");
                }
            }
        }

        byte[] spirV = InvokeSlangc(request, sourceFullPath);
        // Validate and reflect before publishing. A failed compilation cannot poison the cache.
        ShaderCompilationResult result = CreateResult(spirV, request, fromCache: false);
        Directory.CreateDirectory(_cacheDirectory);
        string temporaryCache = Path.Combine(_cacheDirectory, $".{Path.GetFileName(cachePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporaryCache, spirV);
            PublishCache(temporaryCache, cachePath);
        }
        finally
        {
            if (File.Exists(temporaryCache))
                File.Delete(temporaryCache);
        }
        return result;
    }

    private static void PublishCache(string temporaryPath, string cachePath)
    {
        // Windows can briefly deny replacing a destination that another publisher just renamed.
        // Every attempt is an atomic rename, never an in-place overwrite of readers' bytes.
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(temporaryPath, cachePath, overwrite: true);
                return;
            }
            catch (Exception exception) when (attempt < 8 && exception is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(10 * (attempt + 1));
            }
        }
    }

    private static ShaderCompilationResult CreateResult(byte[] spirV, ShaderCompilationRequest request, bool fromCache)
    {
        string emittedEntryPoint = SpirVReflectionParser.GetEntryPoint(spirV, request.Stage, request.EntryPoint);
        if (IsRayTracing(request.Stage) && BitConverter.ToUInt32(spirV, 4) < 0x00010400)
            throw new ArgumentException("Ray tracing requires emitted SPIR-V 1.4 or newer; select profile spirv_1_4 or higher.");
        return new ShaderCompilationResult
        {
            StageCode = new ShaderStageCode { Stage = request.Stage, SpirV = spirV, EntryPoint = emittedEntryPoint },
            Reflection = SpirVReflectionParser.Parse(spirV, request.Stage, emittedEntryPoint),
            FromCache = fromCache,
        };
    }

    public ShaderProgramDescription CompileProgram(
        string sourcePath,
        IEnumerable<(ShaderStage Stage, string EntryPoint)> entries,
        IReadOnlyDictionary<string, string?>? defines = null,
        string? programName = null,
        ShaderCompilationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        List<ShaderStageCode> stages = [];
        List<PipelineReflection> reflections = [];
        // Do not key this collection by stage: multiple misses/hit shaders have distinct SBT indices.
        foreach ((ShaderStage stage, string entryPoint) in entries)
        {
            ShaderCompilationResult result = Compile(new ShaderCompilationRequest
            {
                SourcePath = sourcePath,
                EntryPoint = entryPoint,
                Stage = stage,
                Defines = defines ?? new Dictionary<string, string?>(),
                Options = options ?? new(),
            });
            stages.Add(result.StageCode);
            reflections.Add(result.Reflection);
        }
        return new ShaderProgramDescription
        {
            Name = programName ?? Path.GetFileNameWithoutExtension(sourcePath),
            Stages = stages,
            Reflection = MergeReflections(reflections),
        };
    }

    public static PipelineReflection MergeReflections(IReadOnlyList<PipelineReflection> reflections)
    {
        Dictionary<uint, Dictionary<uint, DescriptorBinding>> sets = [];
        List<PushConstantRange> pushRanges = [];
        HashSet<uint> vertexInputs = [];
        foreach (PipelineReflection reflection in reflections)
        {
            vertexInputs.UnionWith(reflection.VertexInputs);
            foreach (DescriptorSetLayout set in reflection.DescriptorSets)
            {
                if (!sets.TryGetValue(set.SetIndex, out Dictionary<uint, DescriptorBinding>? bindings))
                    sets[set.SetIndex] = bindings = [];
                foreach (DescriptorBinding binding in set.Bindings)
                {
                    if (binding.ArrayCount == 0)
                        throw new InvalidOperationException($"Descriptor set {set.SetIndex}, binding {binding.Binding} has unsupported runtime/zero array count.");
                    if (bindings.TryGetValue(binding.Binding, out DescriptorBinding? existing))
                    {
                        if (existing.Type != binding.Type || existing.ArrayCount != binding.ArrayCount)
                            throw new InvalidOperationException(
                                $"Incompatible descriptor set {set.SetIndex}, binding {binding.Binding}: " +
                                $"{existing.Type}[{existing.ArrayCount}] versus {binding.Type}[{binding.ArrayCount}].");
                        bindings[binding.Binding] = existing with { Stages = existing.Stages | binding.Stages };
                    }
                    else
                        bindings[binding.Binding] = binding;
                }
            }
            foreach (PushConstantRange range in reflection.PushConstants)
            {
                int index = pushRanges.FindIndex(r => r.Offset == range.Offset);
                if (index >= 0)
                {
                    PushConstantRange existing = pushRanges[index];
                    pushRanges[index] = existing with { Stages = existing.Stages | range.Stages, Size = Math.Max(existing.Size, range.Size) };
                }
                else
                    pushRanges.Add(range);
            }
        }
        return new PipelineReflection
        {
            PushConstants = pushRanges.OrderBy(r => r.Offset).ToList(),
            DescriptorSets = sets.OrderBy(kvp => kvp.Key).Select(kvp => new DescriptorSetLayout
            {
                SetIndex = kvp.Key,
                Bindings = kvp.Value.OrderBy(b => b.Key).Select(b => b.Value).ToList(),
            }).ToList(),
            VertexInputs = vertexInputs.Order().ToList(),
        };
    }

    private byte[] InvokeSlangc(ShaderCompilationRequest request, string sourceFullPath)
    {
        string slangcPath = ResolveSlangcPath()
            ?? throw new ShaderCompilationException(
                "slangc not found and no cached SPIR-V matched. Set ENGINE_SLANGC or place the compiler under Tools/Slang/.", string.Empty);
        string temporaryOutput = Path.Combine(Path.GetTempPath(), $"engine-shader-{Guid.NewGuid():N}.spv");
        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = slangcPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (string argument in BuildArguments(request, sourceFullPath))
                startInfo.ArgumentList.Add(argument);
            startInfo.ArgumentList.Add("-o");
            startInfo.ArgumentList.Add(temporaryOutput);
            using Process process = Process.Start(startInfo) ?? throw new ShaderCompilationException("Failed to start slangc.", string.Empty);
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorsTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(60000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                throw new ShaderCompilationException("Shader compilation timed out after 60 seconds.",
                    outputTask.GetAwaiter().GetResult() + errorsTask.GetAwaiter().GetResult());
            }
            string output = outputTask.GetAwaiter().GetResult();
            string errors = errorsTask.GetAwaiter().GetResult();
            string diagnostics = output + errors;
            if (process.ExitCode != 0)
                throw new ShaderCompilationException(
                    $"Shader compilation failed: '{Path.GetFileName(sourceFullPath)}' [{request.Stage} {request.EntryPoint}]", diagnostics);
            if (!File.Exists(temporaryOutput))
                throw new ShaderCompilationException(
                    $"Shader compiler produced no output: '{Path.GetFileName(sourceFullPath)}' [{request.Stage} {request.EntryPoint}]", diagnostics);
            _log?.Debug($"Shader compiled: {Path.GetFileName(sourceFullPath)} [{request.Stage} {request.EntryPoint}]");
            return File.ReadAllBytes(temporaryOutput);
        }
        finally
        {
            if (File.Exists(temporaryOutput))
                File.Delete(temporaryOutput);
        }
    }

    private static void ValidateOptions(ShaderCompilationRequest request)
    {
        ShaderCompilationOptions options = request.Options;
        if (!string.Equals(options.Target, "spirv", StringComparison.Ordinal))
            throw new NotSupportedException("The shader compiler/reflection pipeline currently requires the spirv target.");
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Profile);
        if (options.OptimizationLevel is < 0 or > 3)
            throw new ArgumentOutOfRangeException(nameof(request), "Optimization level must be between 0 and 3.");
        _ = StageName(request.Stage);
        if (IsRayTracing(request.Stage) && Regex.Match(options.Profile, @"^(?:glsl_)?spirv_1_(\d+)$") is { Success: true } match
            && int.Parse(match.Groups[1].Value) < 4)
            throw new ArgumentException("Ray tracing requires SPIR-V 1.4 or newer.", nameof(request));
        foreach (string capability in options.Capabilities)
            ArgumentException.ThrowIfNullOrWhiteSpace(capability);
    }

    private static IEnumerable<string> BuildArguments(ShaderCompilationRequest request, string sourceFullPath)
    {
        ShaderCompilationOptions options = request.Options;
        List<string> arguments =
        [
            sourceFullPath, "-target", options.Target, "-profile", options.Profile,
            "-entry", request.EntryPoint, "-stage", StageName(request.Stage),
            options.RowMajorMatrices ? "-matrix-layout-row-major" : "-matrix-layout-column-major",
            $"-O{options.OptimizationLevel}",
        ];
        if (options.PreserveEntryPointName)
            arguments.Add("-fvk-use-entrypoint-name");
        IEnumerable<string> capabilities = options.Capabilities;
        if (IsRayTracing(request.Stage))
            capabilities = capabilities.Append("GL_EXT_ray_tracing");
        foreach (string capability in capabilities.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            arguments.Add("-capability");
            arguments.Add(capability);
        }
        foreach ((string key, string? value) in request.Defines.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
            arguments.Add(value is null ? $"-D{key}" : $"-D{key}={value}");
        foreach (string directory in GetIncludeDirectories(request, sourceFullPath))
        {
            arguments.Add("-I");
            arguments.Add(directory);
        }
        return arguments;
    }

    private string ComputeCacheKey(ShaderCompilationRequest request, string sourceFullPath)
    {
        StringBuilder key = new();
        AppendKey(key, "engine-slang-cache-v4");
        AppendKey(key, GetCompilerIdentity());
        foreach (string argument in BuildArguments(request, Path.GetFileName(sourceFullPath)))
            AppendKey(key, argument);
        HashSet<string> visited = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        AppendShaderSource(key, sourceFullPath, visited, GetIncludeDirectories(request, sourceFullPath));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key.ToString()))).ToLowerInvariant();
    }

    private string GetCompilerIdentity()
    {
        string? slangcPath = ResolveSlangcPath();
        if (slangcPath is null)
        {
            string fingerprint = ReadCompilerFingerprint();
            if (fingerprint.Length > 0)
                return fingerprint;
            return "compiler-unavailable";
        }
        // The driver version alone is not enough: packaged compiler/backend DLLs can change independently.
        string directory = Path.GetDirectoryName(slangcPath)!;
        string[] files = Directory.EnumerateFiles(directory)
            .Where(path => path == slangcPath || Path.GetFileName(path).StartsWith("slang", StringComparison.OrdinalIgnoreCase)
                && (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || path.Contains(".so", StringComparison.Ordinal) || path.EndsWith(".dylib", StringComparison.Ordinal)))
            .Append(slangcPath).Distinct().Order(StringComparer.Ordinal).ToArray();
        string state = string.Join('|', files.Select(path => { FileInfo file = new(path); return $"{path}:{file.Length}:{file.LastWriteTimeUtc.Ticks}"; }));
        lock (_identityLock)
        {
            if (_compilerFileState == state)
                return _compilerIdentity!;
            StringBuilder identity = new();
            foreach (string path in files)
            {
                AppendKey(identity, path);
                using FileStream stream = File.OpenRead(path);
                AppendKey(identity, Convert.ToHexString(SHA256.HashData(stream)));
            }
            _compilerIdentity = identity.ToString();
            _compilerFileState = state;
            return _compilerIdentity;
        }
    }

    private string ReadCompilerFingerprint()
    {
        try
        {
            string path = Path.Combine(_cacheDirectory, ".compiler-id");
            return VirtualFileSystem.TryReadAllText(path)?.Trim() ?? string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    public string? TryGetCompilerIdentity()
    {
        string? slangcPath = ResolveSlangcPath();
        if (slangcPath is null)
            return null;
        return GetCompilerIdentity();
    }

    private static void AppendKey(StringBuilder key, string value) => key.Append(value.Length).Append(':').Append(value);

    private static string[] GetIncludeDirectories(ShaderCompilationRequest request, string sourceFullPath) =>
        new[] { Path.GetDirectoryName(sourceFullPath)! }.Concat(request.Options.IncludeDirectories.Select(Path.GetFullPath)).Distinct().ToArray();

    private static void AppendShaderSource(StringBuilder key, string path, HashSet<string> visited, IReadOnlyList<string> includeDirectories)
    {
        string fullPath = Path.GetFullPath(path);
        if (!visited.Add(fullPath))
            return;
        string source = VirtualFileSystem.ReadAllText(fullPath);
        AppendKey(key, Path.GetFileName(fullPath));
        AppendKey(key, source);
        string directory = Path.GetDirectoryName(fullPath)!;
        IEnumerable<string> dependencies = Regex.Matches(source, @"(?m)^\s*(?:export\s+)?import\s+([A-Za-z_][A-Za-z0-9_\.]*)\s*;")
            .Select(match => match.Groups[1].Value.Replace('.', Path.DirectorySeparatorChar) + ".slang")
            .Concat(Regex.Matches(source, "(?m)^\\s*#\\s*include\\s*[\"<]([^\">]+)[\">]").Select(match => match.Groups[1].Value));
        foreach (string dependency in dependencies)
        {
            string? resolved = new[] { directory }.Concat(includeDirectories)
                .Select(root => Path.Combine(root, dependency)).FirstOrDefault(VirtualFileSystem.Exists);
            if (resolved is not null)
                AppendShaderSource(key, resolved, visited, includeDirectories);
        }
    }

    private static bool IsRayTracing(ShaderStage stage) => stage is ShaderStage.RayGeneration or ShaderStage.Miss
        or ShaderStage.ClosestHit or ShaderStage.AnyHit or ShaderStage.Intersection or ShaderStage.Callable;

    private static string StageName(ShaderStage stage) => stage switch
    {
        ShaderStage.Vertex => "vertex",
        ShaderStage.Fragment => "fragment",
        ShaderStage.Compute => "compute",
        ShaderStage.Geometry => "geometry",
        ShaderStage.TessellationControl => "hull",
        ShaderStage.TessellationEvaluation => "domain",
        ShaderStage.RayGeneration => "raygeneration",
        ShaderStage.Miss => "miss",
        ShaderStage.ClosestHit => "closesthit",
        ShaderStage.AnyHit => "anyhit",
        ShaderStage.Intersection => "intersection",
        ShaderStage.Callable => "callable",
        _ => throw new NotSupportedException($"Shader stage '{stage}' is not supported by the Slang compiler."),
    };
}
