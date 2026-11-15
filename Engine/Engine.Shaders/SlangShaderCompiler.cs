using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Engine.Core;
using Engine.RHI;

namespace Engine.Shaders;

public sealed record ShaderCompilationRequest
{
    public required string SourcePath { get; init; }
    public required string EntryPoint { get; init; }
    public required ShaderStage Stage { get; init; }
    public IReadOnlyDictionary<string, string?> Defines { get; init; } = new Dictionary<string, string?>();
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
    private readonly string _slangcPath;

    public SlangShaderCompiler(ILogger? log = null, string? cacheDirectory = null, string? slangcPath = null)
    {
        _log = log;
        _cacheDirectory = cacheDirectory ?? DefaultCacheDirectory;
        _slangcPath = slangcPath ?? LocateSlangCompiler();
    }

    public static string LocateSlangCompiler()
    {
        string? environmentPath = Environment.GetEnvironmentVariable("ENGINE_SLANGC");
        if (!string.IsNullOrEmpty(environmentPath) && File.Exists(environmentPath))
            return Path.GetFullPath(environmentPath);

        string executableName = OperatingSystem.IsWindows() ? "slangc.exe" : "slangc";
        if (OperatingSystem.IsWindows())
        {
            string sdkRoot = Environment.GetEnvironmentVariable("VULKAN_SDK") ?? string.Empty;
            if (string.IsNullOrEmpty(sdkRoot))
            {
                string[] sdkRoots =
                [
                    "C:\\VulkanSDK",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LunarG"),
                ];
                foreach (string candidateRoot in sdkRoots)
                {
                    if (!Directory.Exists(candidateRoot))
                        continue;
                    string[] versions = Directory.GetDirectories(candidateRoot, "*", SearchOption.TopDirectoryOnly);
                    if (versions.Length > 0)
                    {
                        sdkRoot = versions
                            .OrderByDescending(static version => version, StringComparer.OrdinalIgnoreCase)
                            .First();
                        break;
                    }
                }
            }
            if (!string.IsNullOrEmpty(sdkRoot))
            {
                string sdkCandidate = Path.Combine(sdkRoot, "Bin", executableName);
                if (File.Exists(sdkCandidate))
                    return sdkCandidate;
            }
        }
        string? current = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && current is not null; i++)
        {
            string candidate = Path.Combine(current, "Tools", "Slang", executableName);
            if (File.Exists(candidate))
                return candidate;
            current = Path.GetDirectoryName(current.TrimEnd(Path.DirectorySeparatorChar));
        }
        string workingDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Tools", "Slang", executableName);
        if (File.Exists(workingDirectory))
            return workingDirectory;
        string pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (string directory in pathVariable.Split(Path.PathSeparator))
        {
            string candidate = Path.Combine(directory.Trim(), executableName);
            if (File.Exists(candidate))
                return candidate;
        }
        throw new FileNotFoundException(
            "slangc not found. Set ENGINE_SLANGC environment variable, place the compiler under Tools/Slang/, or add it to PATH.");
    }

    public ShaderCompilationResult Compile(ShaderCompilationRequest request)
    {
        string sourceFullPath = Path.GetFullPath(request.SourcePath);
        if (!File.Exists(sourceFullPath))
            throw new FileNotFoundException($"Shader source '{request.SourcePath}' not found.");

        string cacheKey = ComputeCacheKey(request, sourceFullPath);
        string cachePath = Path.Combine(_cacheDirectory, cacheKey + ".spv");
        bool fromCache = File.Exists(cachePath);
        byte[] spirV;
        if (fromCache)
        {
            spirV = File.ReadAllBytes(cachePath);
        }
        else
        {
            spirV = InvokeSlangc(request, sourceFullPath);
            Directory.CreateDirectory(_cacheDirectory);
            File.WriteAllBytes(cachePath, spirV);
        }
        PipelineReflection reflection = SpirVReflectionParser.Parse(spirV, request.Stage);
        return new ShaderCompilationResult
        {
            StageCode = new ShaderStageCode
            {
                Stage = request.Stage,
                SpirV = spirV,
                EntryPoint = "main",
            },
            Reflection = reflection,
            FromCache = fromCache,
        };
    }

    public ShaderProgramDescription CompileProgram(
        string sourcePath,
        IEnumerable<(ShaderStage Stage, string EntryPoint)> entries,
        IReadOnlyDictionary<string, string?>? defines = null,
        string? programName = null)
    {
        List<ShaderStageCode> stages = [];
        List<PipelineReflection> reflections = [];
        foreach ((ShaderStage stage, string entryPoint) in entries)
        {
            ShaderCompilationResult result = Compile(new ShaderCompilationRequest
            {
                SourcePath = sourcePath,
                EntryPoint = entryPoint,
                Stage = stage,
                Defines = defines ?? new Dictionary<string, string?>(),
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
        Dictionary<uint, DescriptorSetLayoutBuilder> builders = [];
        List<Engine.RHI.PushConstantRange> pushRanges = [];
        HashSet<uint> vertexInputs = [];
        foreach (PipelineReflection reflection in reflections)
        {
            foreach (uint vertexLocation in reflection.VertexInputs)
                vertexInputs.Add(vertexLocation);
            foreach (DescriptorSetLayout set in reflection.DescriptorSets)
            {
                if (!builders.TryGetValue(set.SetIndex, out DescriptorSetLayoutBuilder? builder))
                {
                    builder = new DescriptorSetLayoutBuilder();
                    builders[set.SetIndex] = builder;
                }
                foreach (DescriptorBinding binding in set.Bindings)
                    builder.Add(binding.Binding, binding.Type, binding.Stages, binding.ArrayCount, string.Empty);
            }
            foreach (Engine.RHI.PushConstantRange range in reflection.PushConstants)
            {
                Engine.RHI.PushConstantRange? existing = pushRanges.FirstOrDefault(r => r.Offset == range.Offset);
                if (existing is not null)
                {
                    int index = pushRanges.IndexOf(existing);
                    pushRanges[index] = existing with { Stages = existing.Stages | range.Stages, Size = Math.Max(existing.Size, range.Size) };
                }
                else
                {
                    pushRanges.Add(range);
                }
            }
        }
        return new PipelineReflection
        {
            PushConstants = pushRanges,
            DescriptorSets = builders.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Value.Build(kvp.Key)).ToList(),
            VertexInputs = [.. vertexInputs],
        };
    }

    private sealed class DescriptorSetLayoutBuilder
    {
        private readonly Dictionary<uint, DescriptorBinding> _bindings = [];

        public void Add(uint binding, DescriptorType type, ShaderStages stages, uint arrayCount, string name)
        {
            if (_bindings.TryGetValue(binding, out DescriptorBinding? existing))
            {
                _bindings[binding] = existing with { Stages = existing.Stages | stages };
                return;
            }
            _bindings[binding] = new DescriptorBinding
            {
                Binding = binding,
                Type = type,
                Stages = stages,
                ArrayCount = Math.Max(1u, arrayCount),
            };
        }

        public DescriptorSetLayout Build(uint setIndex) => new()
        {
            SetIndex = setIndex,
            Bindings = _bindings.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Value).ToList(),
        };
    }

    private byte[] InvokeSlangc(ShaderCompilationRequest request, string sourceFullPath)
    {
        string temporaryOutput = Path.Combine(Path.GetTempPath(), $"engine-shader-{Guid.NewGuid():N}.spv");
        try
        {
            List<string> arguments =
            [
                $"\"{sourceFullPath}\"",
                "-target", "spirv",
                "-entry", request.EntryPoint,
                "-stage", StageName(request.Stage),
                "-o", $"\"{temporaryOutput}\"",
                "-matrix-layout-row-major",
                "-O1",
            ];
            foreach ((string key, string? value) in request.Defines)
                arguments.Add(value is null ? $"-D{key}" : $"-D{key}={value}");
            string includeDirectory = Path.GetDirectoryName(sourceFullPath)!;
            arguments.Add("-I");
            arguments.Add($"\"{includeDirectory}\"");

            ProcessStartInfo startInfo = new()
            {
                FileName = _slangcPath,
                Arguments = string.Join(' ', arguments),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using Process process = Process.Start(startInfo) ?? throw new ShaderCompilationException("Failed to start slangc.", string.Empty);
            string output = process.StandardOutput.ReadToEnd();
            string errors = process.StandardError.ReadToEnd();
            process.WaitForExit(60000);

            if (process.ExitCode != 0)
                throw new ShaderCompilationException(
                    $"Shader compilation failed: '{Path.GetFileName(sourceFullPath)}' [{request.Stage} {request.EntryPoint}]",
                    string.IsNullOrWhiteSpace(errors) ? output : errors);
            if (!File.Exists(temporaryOutput))
                throw new ShaderCompilationException(
                    $"Shader compiler produced no output: '{Path.GetFileName(sourceFullPath)}' [{request.Stage} {request.EntryPoint}]",
                    errors);
            _log?.Debug($"Shader compiled: {Path.GetFileName(sourceFullPath)} [{request.Stage} {request.EntryPoint}]");
            return File.ReadAllBytes(temporaryOutput);
        }
        finally
        {
            if (File.Exists(temporaryOutput))
                File.Delete(temporaryOutput);
        }
    }

    private static string ComputeCacheKey(ShaderCompilationRequest request, string sourceFullPath)
    {
        StringBuilder key = new();
        key.Append(request.EntryPoint).Append('|').Append(request.Stage).Append('|');
        foreach ((string define, string? value) in request.Defines.OrderBy(kvp => kvp.Key))
            key.Append(define).Append('=').Append(value ?? string.Empty).Append(';');
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        AppendShaderSource(key, sourceFullPath, visited);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void AppendShaderSource(StringBuilder key, string path, HashSet<string> visited)
    {
        string fullPath = Path.GetFullPath(path);
        if (!visited.Add(fullPath))
            return;
        string source = File.ReadAllText(fullPath);
        key.Append(fullPath).Append('|').Append(source);
        string directory = Path.GetDirectoryName(fullPath)!;
        foreach (Match match in Regex.Matches(source, @"(?m)^\s*import\s+([A-Za-z_][A-Za-z0-9_\.]*)\s*;"))
        {
            string modulePath = Path.Combine(directory, match.Groups[1].Value.Replace('.', Path.DirectorySeparatorChar) + ".slang");
            if (File.Exists(modulePath))
                AppendShaderSource(key, modulePath, visited);
        }
    }

    private static string StageName(ShaderStage stage) => stage switch
    {
        ShaderStage.Vertex => "vertex",
        ShaderStage.Fragment => "fragment",
        ShaderStage.Compute => "compute",
        ShaderStage.Geometry => "geometry",
        ShaderStage.TessellationControl => "hull",
        ShaderStage.TessellationEvaluation => "domain",
        _ => throw new NotSupportedException($"Shader stage '{stage}' is not supported by the Slang compiler."),
    };
}
