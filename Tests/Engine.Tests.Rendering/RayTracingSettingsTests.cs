using Camera = Engine.Rendering.Camera;
using Light = Engine.Rendering.Light;
using System.Reflection;
using Engine.Core;
using Engine.Mathematics;
using Engine.Backends.Null;
using Engine.Editor;
using Engine.Rendering;
using Engine.RHI;
using Engine.Scene;
using Xunit;

namespace Engine.Tests.Rendering;

public sealed class RayTracingSettingsTests
{
    private static string ShaderDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string path = Path.Combine(directory.FullName, "Samples", "Basic3D", "Shaders");
            if (File.Exists(Path.Combine(path, "PBR.slang")))
                return path;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Basic3D shaders were not found.");
    }

    [Theory]
    [InlineData(0.25f, 1)]
    [InlineData(0.5f, 4)]
    [InlineData(1f, 8)]
    public void ProjectSettings_RoundTripAndApplyPreserveRayTracingSettings(float scale, int samples)
    {
        string directory = Path.Combine(Path.GetTempPath(), "EngineRtSettings", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "Project.project");
        try
        {
            ProjectSettings settings = new()
            {
                RayTracingEnabled = false,
                GiMode = GlobalIlluminationMode.RayTraced,
                ShadowMode = ShadowMode.RayTraced,
                RayTracingReflectionsEnabled = false,
                RayTracingSamples = samples,
                RayTracingResolutionScale = scale,
                RayTracingRayBias = 0.003f,
                RayTracingMaxDistance = 125f,
                RayTracingReflectionRoughnessLimit = 0.65f,
                RayTracingShadowQuality = RayTracingQualityPreset.Low,
                RayTracingReflectionQuality = RayTracingQualityPreset.Ultra,
                RayTracingGiQuality = RayTracingQualityPreset.Medium,
                RayTracingAdaptiveQuality = false,
            };
            settings.Save(path);
            ProjectSettings loaded = ProjectSettings.Load(path);
            RendererModule renderer = new(new RhiModule(new NullRHIFactory()), ShaderDirectory());
            loaded.ApplyTo(renderer);
            Assert.False(renderer.RayTracingEnabled);
            Assert.Equal(GlobalIlluminationMode.RayTraced, renderer.GiMode);
            Assert.Equal(ShadowMode.RayTraced, renderer.ShadowMode);
            Assert.False(renderer.RayTracingReflectionsEnabled);
            Assert.Equal(samples, renderer.RayTracingSamples);
            Assert.Equal(scale, renderer.RayTracingResolutionScale);
            Assert.Equal(0.003f, renderer.RayTracingRayBias);
            Assert.Equal(125f, renderer.RayTracingMaxDistance);
            Assert.Equal(0.65f, renderer.RayTracingReflectionRoughnessLimit);
            Assert.Equal(RayTracingQualityPreset.Low, renderer.RayTracingShadowQuality);
            Assert.Equal(RayTracingQualityPreset.Ultra, renderer.RayTracingReflectionQuality);
            Assert.Equal(RayTracingQualityPreset.Medium, renderer.RayTracingGiQuality);
            Assert.False(renderer.RayTracingAdaptiveQualityEnabled);
            renderer.RayTracingEnabled = true;
            renderer.GiMode = GlobalIlluminationMode.ScreenSpace;
            renderer.ShadowMode = ShadowMode.Off;
            renderer.RayTracingShadowQuality = RayTracingQualityPreset.Medium;
            renderer.RayTracingAdaptiveQualityEnabled = true;
            ProjectSettings captured = ProjectSettings.FromRenderer(renderer, false);
            Assert.True(captured.RayTracingEnabled);
            Assert.Equal(GlobalIlluminationMode.ScreenSpace, captured.GiMode);
            Assert.Equal(ShadowMode.Off, captured.ShadowMode);
            Assert.Equal(scale, captured.RayTracingResolutionScale);
            Assert.Equal(samples, captured.RayTracingSamples);
            Assert.Equal(RayTracingQualityPreset.Medium, captured.RayTracingShadowQuality);
            Assert.Equal(RayTracingQualityPreset.Ultra, captured.RayTracingReflectionQuality);
            Assert.Equal(RayTracingQualityPreset.Medium, captured.RayTracingGiQuality);
            Assert.True(captured.RayTracingAdaptiveQuality);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ProjectSettings_MigratesLegacyBooleanFields()
    {
        string directory = Path.Combine(Path.GetTempPath(), "EngineRtSettings", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "Project.project");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, """
                {
                  "Exposure": 1.2,
                  "RayTracingEnabled": true,
                  "RayTracingShadowsEnabled": true,
                  "RayTracingGiEnabled": false,
                  "ScreenSpaceGiEnabled": false,
                  "RayTracingSamples": 2,
                  "VSync": true
                }
                """);
            ProjectSettings loaded = ProjectSettings.Load(path);
            Assert.Equal(ShadowMode.RayTraced, loaded.ShadowMode);
            Assert.Equal(GlobalIlluminationMode.Off, loaded.GiMode);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData(false, RayTracingQualityEffect.Reflections, RayTracingQualityPreset.Low, 400, 225)]
    [InlineData(true, RayTracingQualityEffect.Reflections, RayTracingQualityPreset.Medium, 400, 225)]
    [InlineData(false, RayTracingQualityEffect.Gi, RayTracingQualityPreset.High, 1200, 675)]
    [InlineData(true, RayTracingQualityEffect.Gi, RayTracingQualityPreset.Ultra, 800, 450)]
    public void LogicalExtent_UsesEachViewAndEffectPreset(bool gameView, RayTracingQualityEffect effect, RayTracingQualityPreset preset, int width, int height)
    {
        using NullDevice device = new();
        using RenderGraph graph = new(device, new LoggerFactory(new MemoryLogSink()).CreateLogger("RT quality"));
        RendererModule renderer = new(new RhiModule(new NullRHIFactory()), ShaderDirectory());
        renderer.RayTracingAdaptiveQualityEnabled = false;
        renderer.RayTracingReflectionQuality = preset;
        renderer.RayTracingGiQuality = preset;
        string fieldName = effect == RayTracingQualityEffect.Reflections ? "RtRawReflection" : "GiTarget";
        fieldName = gameView ? "_game" + fieldName : "_" + char.ToLowerInvariant(fieldName[0]) + fieldName[1..];
        Size2D allocated = gameView ? new(800, 450) : new(1600, 900);
        RenderGraphResource resource = graph.DeclareTransientTexture("RT output", new TextureDescription { Extent = allocated, Format = TextureFormat.R16G16B16A16Float });
        typeof(RendererModule).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(renderer, resource);
        Size2D extent = (Size2D)typeof(RendererModule).GetMethod("RayTracingLogicalExtent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(renderer, [gameView, effect])!;
        Assert.Equal(new Size2D(width, height), extent);
        Assert.Equal(allocated, resource.Description.Extent);
    }

    [Fact]
    public void SceneSync_ResolvesSharedMaterialsAndTextureRevisionsOncePerFrame()
    {
        RendererModule renderer = new(new RhiModule(new NullRHIFactory()), ShaderDirectory());
        AssetReference asset = new("guid:0123456789abcdef0123456789abcdef");
        AssetReference texture = new("guid:fedcba9876543210fedcba9876543210");
        MaterialProperties properties = new() { AlbedoTexture = texture, Roughness = 0.4f };
        int materialCalls = 0;
        int textureCalls = 0;
        renderer.MaterialResolver = _ => { materialCalls++; return new(properties, 1); };
        renderer.TextureRevisionResolver = (_, _) => { textureCalls++; return 1; };
        MethodInfo sync = typeof(RendererModule).GetMethod("SyncMaterial", BindingFlags.Instance | BindingFlags.NonPublic)!;
        MeshRenderer first = new() { MaterialAsset = asset };
        MeshRenderer second = new() { MaterialAsset = asset };
        System.Collections.IDictionary states = (System.Collections.IDictionary)typeof(RendererModule)
            .GetField("_sceneMaterialStates", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer)!;
        Type revisionsType = typeof(RendererModule).GetNestedType("TextureRevisions", BindingFlags.NonPublic)!;
        Type stateType = typeof(RendererModule).GetNestedType("SceneMaterialState", BindingFlags.NonPublic)!;
        object revisions = Activator.CreateInstance(revisionsType, [1L, 0L, 0L, 0L, 0L, 0L])!;
        states[first] = Activator.CreateInstance(stateType, [properties, 1L, revisions]);
        states[second] = Activator.CreateInstance(stateType, [properties, 1L, revisions]);
        Material material = new();
        sync.Invoke(renderer, [first, material, false]);
        sync.Invoke(renderer, [second, material, false]);
        Assert.Equal(1, materialCalls);
        Assert.Equal(1, textureCalls);
        typeof(RendererModule).GetMethod("SyncScene", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(renderer, null);
        sync.Invoke(renderer, [second, material, false]);
        Assert.Equal(2, materialCalls);
        Assert.Equal(2, textureCalls);
    }

    [Fact]
    public void RendererGiAndShadowModesValidateInput()
    {
        RendererModule renderer = new(new RhiModule(new NullRHIFactory()), ShaderDirectory());
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.GiMode = (GlobalIlluminationMode)42);
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.ShadowMode = (ShadowMode)42);
    }

    [Fact]
    public void RayTracingSettings_RejectInvalidDispatchParameters()
    {
        RendererModule renderer = new(new RhiModule(new NullRHIFactory()), ShaderDirectory());
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.RayTracingSamples = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.RayTracingSamples = 9);
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.RayTracingResolutionScale = float.NaN);
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.RayTracingResolutionScale = 0f);
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.RayTracingResolutionScale = 2f);
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.RayTracingRayBias = -1f);
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.RayTracingMaxDistance = float.PositiveInfinity);
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.RayTracingReflectionRoughnessLimit = 0f);
    }

    [Fact]
    public void ProjectSettings_LoadLegacyFileUsesDefaultQualityPresets()
    {
        string directory = Path.Combine(Path.GetTempPath(), "EngineRtSettings", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "Project.project");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, """
                {
                  "Exposure": 1,
                  "RayTracingEnabled": true,
                  "RayTracingSamples": 2,
                  "VSync": true
                }
                """);
            ProjectSettings loaded = ProjectSettings.Load(path);
            Assert.Equal(RayTracingQualityPreset.High, loaded.RayTracingShadowQuality);
            Assert.Equal(RayTracingQualityPreset.High, loaded.RayTracingReflectionQuality);
            Assert.Equal(RayTracingQualityPreset.High, loaded.RayTracingGiQuality);
            Assert.True(loaded.RayTracingAdaptiveQuality);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }
}
