using System.Diagnostics;
using System.Security.Cryptography;
using Engine.Core;
using Engine.Shaders;

namespace Engine.Editor.Build;

public sealed class BuildRunner
{
    private static readonly string PlayerCsprojRelative = Path.Combine("Player", "EnginePlayer", "EnginePlayer.csproj");
    private static readonly string ShaderSourceRelative = Path.Combine("Samples", "Basic3D", "Shaders");

    private readonly BuildRequest _request;
    private readonly List<BuildStepReport> _steps = [];
    private readonly List<string> _warnings = [];
    private readonly List<string> _errors = [];

    public BuildRunner(BuildRequest request)
    {
        _request = request ?? throw new ArgumentNullException(nameof(request));
    }

    public BuildReport Run()
    {
        Stopwatch total = Stopwatch.StartNew();
        Log($"building '{_request.Profile.ProductName}' v{_request.Profile.Version} [{_request.Profile.Platform}/{_request.Profile.Mode}/{_request.Profile.Configuration}]");
        try
        {
            string enginePlayerCsproj = Validate();
            string outputRoot = PrepareOutput();
            string shaderDirectory = WarmShaders();
            Dictionary<string, byte[]> entries = BundleStep(shaderDirectory);
            byte[] packKey = RandomNumberGenerator.GetBytes(PackCrypto.KeySizeBytes);
            string keyHex = Convert.ToHexString(packKey);
            (int entryCount, int chunkCount, long packedBytes) = WritePacks(outputRoot, entries, packKey);
            string executable = PublishStep(enginePlayerCsproj, outputRoot, keyHex);
            if (_errors.Count > 0)
                return Finish(total, outputRoot, string.Empty, success: false);
            FinalizeReport(outputRoot, executable, entryCount, chunkCount, packedBytes, entries);
            return Finish(total, outputRoot, executable, success: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _errors.Add(exception.Message);
            Log($"error: {exception.Message}");
            string outputRoot = OutputRoot();
            return Finish(total, outputRoot, string.Empty, success: false);
        }
    }

    private BuildReport Finish(Stopwatch total, string outputRoot, string executable, bool success)
    {
        total.Stop();
        Log(success
            ? $"build succeeded in {total.Elapsed.TotalSeconds:F1}s → {outputRoot}"
            : "build failed");
        return new BuildReport
        {
            Success = success,
            Duration = total.Elapsed,
            OutputDirectory = outputRoot,
            ExecutablePath = executable,
            FileCount = success ? CountFiles(outputRoot) : 0,
            TotalBytes = success ? MeasureBytes(outputRoot) : 0,
            Warnings = _warnings,
            Errors = _errors,
            Steps = _steps,
        };
    }

    private string OutputRoot() => Path.Combine(
        Path.GetFullPath(_request.ProjectRoot),
        _request.Profile.OutputDirectory,
        $"{_request.Profile.Platform}-{_request.Profile.Mode.ToLowerInvariant()}-{_request.Profile.Configuration.ToLowerInvariant()}",
        SanitizeAssemblyName(_request.Profile.ProductName));

    private bool IsShippingBuild => _request.Profile.Configuration.Equals("Release", StringComparison.OrdinalIgnoreCase);

    private string ReportPath(string outputRoot) => IsShippingBuild
        ? Path.Combine(Path.GetDirectoryName(outputRoot.TrimEnd(Path.DirectorySeparatorChar))!, $"{SanitizeAssemblyName(_request.Profile.ProductName)}.build-report.json")
        : Path.Combine(outputRoot, "build-report.json");

    private string Validate()
    {
        Stopwatch step = Stopwatch.StartNew();
        string engineRoot = Path.GetFullPath(_request.EngineRoot);
        string playerCsproj = Path.Combine(engineRoot, PlayerCsprojRelative);
        if (!File.Exists(playerCsproj))
            throw new InvalidOperationException($"Player runtime project not found: {playerCsproj}");
        if (!Directory.Exists(_request.ProjectRoot))
            throw new InvalidOperationException($"Project root not found: {_request.ProjectRoot}");
        if (_request.Profile.Platform is not ("win-x64" or "linux-x64" or "osx-arm64"))
            throw new InvalidOperationException($"Unsupported platform '{_request.Profile.Platform}'.");
        if (_request.Profile.Configuration is not ("Debug" or "Release"))
            throw new InvalidOperationException($"Unsupported build configuration '{_request.Profile.Configuration}'. Use 'Debug' or 'Release'.");
        if (!Directory.Exists(Path.Combine(engineRoot, ShaderSourceRelative)))
            throw new InvalidOperationException($"Shader sources not found: {Path.Combine(engineRoot, ShaderSourceRelative)}");
        AddStep("Validate", step, $"engine={engineRoot}");
        return playerCsproj;
    }

    private string PrepareOutput()
    {
        Stopwatch step = Stopwatch.StartNew();
        string outputRoot = OutputRoot();
        string publishDirectory = Path.Combine(
            Path.GetFullPath(_request.ProjectRoot),
            _request.Profile.OutputDirectory,
            ".publish");
        DeleteDirectory(publishDirectory);
        try
        {
            DeleteDirectory(outputRoot);
            Directory.CreateDirectory(outputRoot);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            string alternate = $"{outputRoot}-{DateTime.UtcNow:HHmmss}";
            _warnings.Add($"Output directory '{outputRoot}' is locked ({exception.Message}); building to '{alternate}'.");
            Log($"warning: output directory is locked, building to {alternate}");
            DeleteDirectory(alternate);
            Directory.CreateDirectory(alternate);
            outputRoot = alternate;
        }
        AddStep("Prepare output", step, outputRoot);
        return outputRoot;
    }

    private string WarmShaders()
    {
        Stopwatch step = Stopwatch.StartNew();
        string shaderDirectory = Path.Combine(Path.GetFullPath(_request.EngineRoot), ShaderSourceRelative);
        string? pinnedCompiler = Path.Combine(Path.GetFullPath(_request.EngineRoot), "Tools", "Slang", OperatingSystem.IsWindows() ? "slangc.exe" : "slangc");
        SlangShaderCompiler compiler = File.Exists(pinnedCompiler)
            ? new SlangShaderCompiler(cacheDirectory: Path.Combine(Path.GetFullPath(_request.ProjectRoot), "Cache", "Shaders"), slangcPath: pinnedCompiler)
            : new SlangShaderCompiler(cacheDirectory: Path.Combine(Path.GetFullPath(_request.ProjectRoot), "Cache", "Shaders"));
        if (!File.Exists(pinnedCompiler))
            _warnings.Add($"Repository slang compiler not found at '{pinnedCompiler}'; the shader cache may not match the player's compiler.");
        ShaderWarmup.WarmupResult result = ShaderWarmup.CompileAll(compiler, shaderDirectory, Log);
        if (result.Compiled == 0)
            throw new InvalidOperationException("Shader warmup compiled no variants; check slangc availability.");
        string? identity = compiler.TryGetCompilerIdentity();
        if (identity is not null)
            File.WriteAllText(Path.Combine(Path.GetFullPath(_request.ProjectRoot), "Cache", "Shaders", ".compiler-id"), identity);
        else
            _warnings.Add("Could not capture a compiler fingerprint; players without slangc will fail to match the shader cache.");
        AddStep("Warm shaders", step, $"{result.Compiled} program variants compiled, {result.Failed} failed");
        return shaderDirectory;
    }

    private Dictionary<string, byte[]> BundleStep(string shaderDirectory)
    {
        Stopwatch step = Stopwatch.StartNew();
        bool managed = _request.Profile.Mode.Equals("Managed", StringComparison.OrdinalIgnoreCase);
        AssetBundler.BundleResult result = AssetBundler.Collect(_request, shaderDirectory, includeScriptAssembly: managed, Log);
        if (result.Scenes.Count == 0)
            _warnings.Add("No scenes were bundled; the player will start with an empty scene.");
        AddStep("Bundle assets", step, $"{result.Entries.Count} files, {result.ScriptCount} script(s)");
        return result.Entries;
    }

    private (int EntryCount, int ChunkCount, long PackedBytes) WritePacks(string outputRoot, Dictionary<string, byte[]> entries, byte[] packKey)
    {
        Stopwatch step = Stopwatch.StartNew();
        AssetPackWriter.PackResult result = AssetPackWriter.Write(Path.Combine(outputRoot, "Data"), entries, packKey, Log);
        AddStep("Pack assets", step, $"{result.EntryCount} entries → {result.ChunkCount} chunks, {result.TotalBytes / (1024.0 * 1024.0):F1} MB, AES-256-GCM");
        return (result.EntryCount, result.ChunkCount, result.TotalBytes);
    }

    private string PublishStep(string enginePlayerCsproj, string outputRoot, string keyHex)
    {
        Stopwatch step = Stopwatch.StartNew();
        string publishDirectory = Path.Combine(
            Path.GetFullPath(_request.ProjectRoot),
            _request.Profile.OutputDirectory,
            ".publish");
        Directory.CreateDirectory(publishDirectory);
        bool aot = !_request.Profile.Mode.Equals("Managed", StringComparison.OrdinalIgnoreCase);
        string assemblyName = SanitizeAssemblyName(_request.Profile.ProductName);
        List<string> arguments =
        [
            "publish", enginePlayerCsproj,
            "-c", "Release",
            "-r", _request.Profile.Platform,
            "--nologo",
            "-v", "q",
            $"-p:GameAssemblyName={assemblyName}",
            $"-p:GamePackKey={keyHex}",
            "-o", publishDirectory,
        ];
        if (aot)
        {
            arguments.Add("-p:PublishAot=true");
            arguments.Add("-p:IlcGenerateCompleteTypeMetadata=true");
            arguments.Add("-p:InvariantGlobalization=true");
            string scriptsRoot = Path.Combine(Path.GetFullPath(_request.ProjectRoot), "Scripts");
            List<string> classNames = AssetBundler.ListScriptClassNames(scriptsRoot);
            string? generatedRoot = null;
            if (classNames.Count > 0)
            {
                generatedRoot = Path.Combine(Path.GetTempPath(), $"engine-build-{Guid.NewGuid():N}");
                Directory.CreateDirectory(generatedRoot);
                File.WriteAllText(Path.Combine(generatedRoot, "GameScripts.g.cs"), AssetBundler.GenerateScriptComponents(classNames));
                arguments.Add($"-p:GameScriptsRoot={scriptsRoot}{Path.DirectorySeparatorChar}");
                arguments.Add($"-p:GameGeneratedRoot={generatedRoot}{Path.DirectorySeparatorChar}");
                Log($"aot: {classNames.Count} script class(es) compiled into the native executable");
            }
            else
            {
                Log("aot: no ScriptBehaviour classes found; player will run without user scripts");
            }
            try
            {
                RunDotNet(arguments, "publish (Native AOT)");
            }
            finally
            {
                if (generatedRoot is not null)
                    DeleteDirectory(generatedRoot);
            }
        }
        else
        {
            arguments.Add("--self-contained");
            arguments.Add("true");
            arguments.Add("-p:PublishSingleFile=true");
            arguments.Add("-p:IncludeNativeLibrariesForSelfExtract=false");
            arguments.Add("-p:EnableCompressionInSingleFile=true");
            RunDotNet(arguments, "publish (managed single-file)");
        }
        string executable = Organize(publishDirectory, outputRoot);
        AddStep("Publish runtime", step, $"{_request.Profile.Mode} → {Path.GetFileName(executable)}");
        return executable;
    }

    private string Organize(string publishDirectory, string outputRoot)
    {
        string assemblyName = SanitizeAssemblyName(_request.Profile.ProductName);
        string? executable = null;
        foreach (string file in Directory.EnumerateFiles(publishDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            string name = Path.GetFileName(file);
            string extension = Path.GetExtension(name).ToLowerInvariant();
            bool isNative = extension is ".dll" or ".so" or ".dylib";
            bool isExecutable = name.Equals($"{assemblyName}.exe", StringComparison.OrdinalIgnoreCase)
                || name.Equals(assemblyName, StringComparison.OrdinalIgnoreCase);
            if (extension == ".pdb")
                continue;
            if (!isNative && !isExecutable)
                continue;
            File.Move(file, Path.Combine(outputRoot, name), overwrite: true);
            if (isExecutable)
                executable = Path.Combine(outputRoot, name);
        }
        DeleteDirectory(publishDirectory);
        if (executable is null)
            throw new InvalidOperationException($"Published output did not contain an executable named '{assemblyName}'.");
        return executable;
    }

    private void RunDotNet(List<string> arguments, string stepName)
    {
        Log($"dotnet {stepName}…");
        ProcessStartInfo startInfo = new()
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetFullPath(_request.EngineRoot),
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the dotnet CLI. Is the .NET SDK installed?");
        string output = process.StandardOutput.ReadToEndAsync().GetAwaiter().GetResult();
        string error = process.StandardError.ReadToEndAsync().GetAwaiter().GetResult();
        if (!process.WaitForExit(1_800_000))
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException($"{stepName} timed out after 30 minutes.");
        }
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.Contains("warning", StringComparison.OrdinalIgnoreCase) || line.Contains("error", StringComparison.OrdinalIgnoreCase))
                Log(line);
        }
        if (process.ExitCode != 0)
        {
            string combined = error + "\n" + output;
            string tail = string.Join(Environment.NewLine, combined
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .TakeLast(30));
            if (combined.Contains("Platform linker not found", StringComparison.OrdinalIgnoreCase))
            {
                _errors.Add("Native AOT requires the MSVC linker: install Visual Studio Build Tools with the 'Desktop development with C++' workload, or switch the build mode to Managed.");
                Log("error: Native AOT requires the MSVC linker (Visual Studio 'Desktop development with C++' workload). Managed mode works without it.");
            }
            else
            {
                _errors.Add($"{stepName} failed with exit code {process.ExitCode}.");
            }
            Log(tail);
            Log($"{stepName} failed with exit code {process.ExitCode}.");
        }
    }

    private void FinalizeReport(string outputRoot, string executable, int entryCount, int chunkCount, long packedBytes, Dictionary<string, byte[]> entries)
    {
        Stopwatch step = Stopwatch.StartNew();
        string reportPath = ReportPath(outputRoot);
        bool aot = !_request.Profile.Mode.Equals("Managed", StringComparison.OrdinalIgnoreCase);
        BuildReport report = new()
        {
            Success = true,
            Duration = TimeSpan.Zero,
            OutputDirectory = outputRoot,
            ExecutablePath = executable,
            FileCount = CountFiles(outputRoot),
            TotalBytes = MeasureBytes(outputRoot),
            Warnings = _warnings,
            Errors = _errors,
            Steps = _steps,
        };
        File.WriteAllText(reportPath, System.Text.Json.JsonSerializer.Serialize(new
        {
            report.Success,
            report.Duration,
            report.OutputDirectory,
            report.ExecutablePath,
            report.FileCount,
            report.TotalBytes,
            report.Warnings,
            report.Errors,
            report.Steps,
            packaging = new
            {
                entryCount,
                chunkCount,
                packedBytes,
                encrypted = true,
                encryption = "AES-256-GCM, key embedded in the executable",
                protection = aot
                    ? "NativeAOT (no managed IL) + encrypted asset packs"
                    : "managed single-file IL + encrypted asset packs; use Native AOT mode for decompilation resistance",
            },
            chunks = System.IO.Directory.GetFiles(Path.Combine(outputRoot, "Data", "packs"), "*.pack", SearchOption.AllDirectories)
                .Select(path => new
                {
                    name = Path.GetRelativePath(outputRoot, path).Replace('\\', '/'),
                    bytes = new FileInfo(path).Length,
                    sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(),
                })
                .OrderBy(chunk => chunk.name)
                .ToList(),
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Log($"report: {reportPath}");
        AddStep("Write report", step, Path.GetFileName(reportPath));
    }

    private static int CountFiles(string directory)
    {
        int count = 0;
        try
        {
            foreach (string _ in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                count++;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
        return count;
    }

    private static long MeasureBytes(string directory)
    {
        long bytes = 0;
        try
        {
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                bytes += new FileInfo(file).Length;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
        return bytes;
    }

    private void AddStep(string name, Stopwatch stopwatch, string details)
    {
        stopwatch.Stop();
        _steps.Add(new BuildStepReport { Name = name, Duration = stopwatch.Elapsed, Details = details });
        Log($"{name}: {stopwatch.Elapsed.TotalSeconds:F1}s {details}");
    }

    private void Log(string message) => _request.Log?.Invoke(message);

    private static void DeleteDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            return;
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                TryDelete(file);
            foreach (string subDirectory in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories).OrderByDescending(value => value.Length))
                TryDelete(subDirectory);
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception retryException) when (retryException is IOException or UnauthorizedAccessException)
            {
                throw new IOException($"Could not clear build directory '{directory}': {retryException.Message}", retryException);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
            else if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static string SanitizeAssemblyName(string name)
    {
        string sanitized = new(name.Trim().Select(character =>
            char.IsLetterOrDigit(character) || character is '.' or '_' ? character : '_').ToArray());
        sanitized = sanitized.TrimEnd('.', '_');
        if (sanitized.Length == 0)
            sanitized = "Game";
        if (char.IsDigit(sanitized[0]))
            sanitized = "Game" + sanitized;
        return sanitized;
    }
}
