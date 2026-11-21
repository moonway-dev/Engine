using System.Reflection;
using System.Text.Json;
using Engine.Backends.Null;
using Engine.Backends.Vulkan;
using Engine.Core;
using Engine.Mathematics;
using Engine.Physics;
using Engine.Platform;
using Engine.RHI;
using Engine.Rendering;
using Engine.Scripting;
using Engine.Scene;

namespace Engine.Player;

public static class PlayerApplication
{
    public const string PackKeyMetadataName = "EnginePackKey";

    public static int Run(string[] args)
    {
        PlayerOptions options = PlayerOptions.Parse(args);
        string projectRoot = ResolveProjectRoot(options.ProjectPath);
        AssetPackSet? packs = MountPack(projectRoot);
        PlayerBuildInfo buildInfo = PlayerBuildInfo.Load(projectRoot);
        if (options.ScenePath is null && buildInfo.StartupScene.Length > 0)
        {
            string startup = Path.Combine(projectRoot, buildInfo.StartupScene.Replace('/', Path.DirectorySeparatorChar));
            if (VirtualFileSystem.Exists(startup))
                options.WithScenePath(startup);
        }

        RhiModule rhiModule = CreateRhiModule(options, PlayerSettingsApplier.ReadVSync(projectRoot));
        RendererModule renderer = new(rhiModule, Path.Combine(projectRoot, "Shaders"));
        renderer.ShaderCacheDirectory = Path.Combine(projectRoot, "Cache", "Shaders");
        PlayerSettingsApplier.Apply(renderer, projectRoot);
        PhysicsModule physics = new();
        ApplyPhysicsLayerConfig(projectRoot, physics);
        ScriptingModule scripting = new(Path.Combine(projectRoot, "Scripts"))
        {
            Physics = physics,
            AssetsRoot = projectRoot,
            PrecompiledScripts = buildInfo.PrecompiledScripts,
        };
        LoadScriptAssembly(packs, buildInfo, scripting);
        SceneBehavioursModule behaviours = new();
        PlayerRuntimeModule player = new(options, rhiModule, renderer, physics, scripting, behaviours, projectRoot);

        ApplicationBuilder builder = ApplicationBuilder
            .Create()
            .AddLoggerSink(new FileLogSink(Path.Combine("Logs"), "player"));
        if (!OperatingSystem.IsWindows() || Console.IsOutputRedirected)
            builder.AddLoggerSink(new ConsoleLogSink(LogLevel.Information));
        builder
            .AddModule(new PlatformModule(new WindowDescription
            {
                Title = string.IsNullOrWhiteSpace(buildInfo.ProductName) ? "Engine Player" : buildInfo.ProductName,
                Width = options.Width,
                Height = options.Height,
            }))
            .AddModule(rhiModule)
            .AddModule(scripting)
            .AddModule(physics)
            .AddModule(behaviours)
            .AddModule(player)
            .AddModule(renderer);
        Application application = builder.Build();
        int exitCode;
        try
        {
            application.Run();
            exitCode = application.Context.ExitCode;
        }
        finally
        {
            VirtualFileSystem.UnmountAll();
            packs?.Dispose();
        }
        return exitCode;
    }

    private static void ApplyPhysicsLayerConfig(string projectRoot, PhysicsModule physics)
    {
        string? json = VirtualFileSystem.TryReadAllText(Path.Combine(projectRoot, "Project.project"));
        if (json is null)
            return;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return;
            if (document.RootElement.TryGetProperty("PhysicsLayers", out JsonElement layersElement) && layersElement.ValueKind == JsonValueKind.Array)
            {
                List<string> names = [];
                foreach (JsonElement name in layersElement.EnumerateArray())
                    if (name.ValueKind == JsonValueKind.String)
                        names.Add(name.GetString() ?? string.Empty);
                Engine.Physics.PhysicsLayers.SetNames(names);
            }
            if (document.RootElement.TryGetProperty("LayerCollisionMasks", out JsonElement masksElement) && masksElement.ValueKind == JsonValueKind.Array)
            {
                List<long> masks = [];
                foreach (JsonElement mask in masksElement.EnumerateArray())
                    if (mask.ValueKind == JsonValueKind.Number && mask.TryGetInt64(out long value))
                        masks.Add(value);
                physics.SetLayerMasks(masks);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
        }
    }

    private static AssetPackSet? MountPack(string projectRoot)
    {
        string manifestPath = Path.Combine(projectRoot, "manifest.bin");
        if (!File.Exists(manifestPath))
            return null;
        byte[]? key = ReadEmbeddedPackKey();
        AssetPackSet packs = AssetPackSet.Load(manifestPath, key);
        VirtualFileSystem.Mount(projectRoot, packs);
        return packs;
    }

    private static byte[]? ReadEmbeddedPackKey()
    {
        Assembly assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        foreach (AssemblyMetadataAttribute metadata in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (metadata.Key.Equals(PackKeyMetadataName, StringComparison.Ordinal))
            {
                try
                {
                    return PackCrypto.ParseKeyHex(metadata.Value ?? string.Empty);
                }
                catch (Exception exception) when (exception is InvalidDataException or FormatException or ArgumentException)
                {
                    throw new InvalidDataException("The embedded pack key is invalid; the build is corrupted.");
                }
            }
        }
        return null;
    }

    private static void LoadScriptAssembly(AssetPackSet? packs, PlayerBuildInfo buildInfo, ScriptingModule scripting)
    {
        if (packs is null)
            return;
        scripting.PrecompiledScripts = true;
        const string key = "Scripts/Scripts.dll";
        if (!packs.Exists(key))
            return;
        using Stream stream = packs.OpenRead(key);
        using MemoryStream memory = new();
        stream.CopyTo(memory);
        Assembly.Load(memory.ToArray());
    }

    internal static string ResolveProjectRoot(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            string full = Path.GetFullPath(explicitPath);
            if (File.Exists(full) && full.EndsWith(".project", StringComparison.OrdinalIgnoreCase))
                return Path.GetDirectoryName(full) ?? throw new DirectoryNotFoundException($"Project directory for '{full}' not found.");
            if (Directory.Exists(full))
                return full;
            throw new DirectoryNotFoundException($"Project directory '{full}' not found.");
        }
        string packaged = Path.Combine(AppContext.BaseDirectory, "Data");
        if (!File.Exists(Path.Combine(packaged, "manifest.bin")) && !File.Exists(Path.Combine(packaged, "Project.project")))
            packaged = Path.Combine(AppContext.BaseDirectory, "data");
        if (File.Exists(Path.Combine(packaged, "manifest.bin")) || File.Exists(Path.Combine(packaged, "Project.project")))
            return packaged;
        string? current = Directory.GetCurrentDirectory();
        for (int i = 0; i < 6 && current is not null; i++)
        {
            string candidate = Path.Combine(current, "Projects", "Showcase");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "Project.project")))
                return candidate;
            current = Path.GetDirectoryName(current);
        }
        throw new DirectoryNotFoundException(
            "No packaged build at '<exe>/Data' and no local Projects/Showcase found. Pass --project <path>.");
    }

    private static RhiModule CreateRhiModule(PlayerOptions options, bool vsync)
    {
        RHIConfiguration configuration = new()
        {
            ApplicationName = "EnginePlayer",
            EnableValidation = options.Validation,
            VSync = options.VSync && vsync,
        };
        if (options.RequestedBackend == GraphicsBackend.Null)
            return new RhiModule(new NullRHIFactory(), configuration);
        VulkanRHIFactory vulkanFactory = new();
        if (!vulkanFactory.IsSupported())
            return new RhiModule(new NullRHIFactory(), configuration);
        return new RhiModule(vulkanFactory, configuration);
    }
}

public sealed class PlayerBuildInfo
{
    public string ProductName { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Mode { get; init; } = string.Empty;
    public string StartupScene { get; init; } = string.Empty;

    public bool PrecompiledScripts => Mode.Equals("Aot", StringComparison.OrdinalIgnoreCase);

    public static PlayerBuildInfo Load(string projectRoot)
    {
        string buildInfoPath = Path.Combine(projectRoot, "build.json");
        string? json = VirtualFileSystem.TryReadAllText(buildInfoPath);
        if (json is not null)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;
                return new PlayerBuildInfo
                {
                    ProductName = ReadString(root, "productName", string.Empty),
                    Version = ReadString(root, "version", string.Empty),
                    Mode = ReadString(root, "mode", string.Empty),
                    StartupScene = ReadString(root, "startupScene", string.Empty),
                };
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
            }
        }
        string? projectJson = VirtualFileSystem.TryReadAllText(Path.Combine(projectRoot, "Project.project"));
        if (projectJson is null)
            return new PlayerBuildInfo();
        try
        {
            using JsonDocument document = JsonDocument.Parse(projectJson);
            JsonElement root = document.RootElement;
            JsonElement build = root.TryGetProperty("Build", out JsonElement buildElement) && buildElement.ValueKind == JsonValueKind.Object
                ? buildElement
                : default;
            return new PlayerBuildInfo
            {
                ProductName = build.ValueKind == JsonValueKind.Object ? ReadString(build, "ProductName", string.Empty) : string.Empty,
                Version = build.ValueKind == JsonValueKind.Object ? ReadString(build, "Version", string.Empty) : string.Empty,
                Mode = build.ValueKind == JsonValueKind.Object ? ReadString(build, "Mode", string.Empty) : string.Empty,
                StartupScene = ReadString(root, "LastScene", string.Empty),
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new PlayerBuildInfo();
        }
    }

    private static string ReadString(JsonElement element, string name, string fallback)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
            return fallback;
        return value.GetString() ?? fallback;
    }
}
