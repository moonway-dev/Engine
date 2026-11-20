using Engine.Backends.Null;
using Engine.Editor;
using Engine.Rendering;
using Engine.RHI;
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
