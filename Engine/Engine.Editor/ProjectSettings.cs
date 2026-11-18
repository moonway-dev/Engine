using System.Text.Json;
using Engine.Mathematics;
using Engine.Rendering;

namespace Engine.Editor;

public sealed class ProjectSettings
{
    public float Exposure = 1f;
    public float LodPixelBudget = 320f;
    public CascadedShadowSettings Shadows = new();
    public float ShadowConstantBias = 1.25f;
    public float ShadowSlopeBias = 1.75f;
    public bool ScreenSpaceGiEnabled = true;
    public float ScreenSpaceGiIntensity = 0.5f;
    public float ScreenSpaceGiRadius = 1.5f;
    public bool RayTracingEnabled = true;
    public bool RayTracingShadowsEnabled = true;
    public bool RayTracingReflectionsEnabled = true;
    public bool RayTracingGiEnabled = true;
    public float RayTracingReflectionStrength = 0.6f;
    public int RayTracingSamples = 2;
    public float RayTracingResolutionScale = 1f;
    public float RayTracingRayBias = 0.002f;
    public float RayTracingMaxDistance = 200f;
    public float RayTracingReflectionRoughnessLimit = 0.3f;
    public RayTracingQualityPreset RayTracingShadowQuality = RayTracingQualityPreset.High;
    public RayTracingQualityPreset RayTracingReflectionQuality = RayTracingQualityPreset.High;
    public RayTracingQualityPreset RayTracingGiQuality = RayTracingQualityPreset.High;
    public bool RayTracingAdaptiveQuality = true;
    public bool VSync = true;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        IncludeFields = true,
    };

    public static ProjectSettings Load(string path)
    {
        if (!File.Exists(path))
            return new ProjectSettings();
        ProjectSettings settings = JsonSerializer.Deserialize<ProjectSettings>(File.ReadAllText(path), SerializerOptions)
            ?? throw new InvalidOperationException($"Project settings file '{path}' is empty or invalid.");
        settings.Shadows.Validate();
        return settings;
    }

    public void Save(string path)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, JsonSerializer.Serialize(this, SerializerOptions));
    }

    public void ApplyTo(RendererModule renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        renderer.Exposure = Exposure;
        renderer.LodPixelBudget = LodPixelBudget;
        renderer.ShadowSettings = Shadows;
        renderer.ShadowConstantBias = ShadowConstantBias;
        renderer.ShadowSlopeBias = ShadowSlopeBias;
        renderer.ScreenSpaceGIEnabled = ScreenSpaceGiEnabled;
        renderer.ScreenSpaceGIIntensity = ScreenSpaceGiIntensity;
        renderer.ScreenSpaceGIRadius = ScreenSpaceGiRadius;
        renderer.RayTracingEnabled = RayTracingEnabled;
        renderer.RayTracingShadowsEnabled = RayTracingShadowsEnabled;
        renderer.RayTracingReflectionsEnabled = RayTracingReflectionsEnabled;
        renderer.RayTracingGiEnabled = RayTracingGiEnabled;
        renderer.RayTracingReflectionStrength = RayTracingReflectionStrength;
        renderer.RayTracingSamples = RayTracingSamples;
        renderer.RayTracingResolutionScale = RayTracingResolutionScale;
        renderer.RayTracingRayBias = RayTracingRayBias;
        renderer.RayTracingMaxDistance = RayTracingMaxDistance;
        renderer.RayTracingReflectionRoughnessLimit = RayTracingReflectionRoughnessLimit;
        renderer.RayTracingShadowQuality = RayTracingShadowQuality;
        renderer.RayTracingReflectionQuality = RayTracingReflectionQuality;
        renderer.RayTracingGiQuality = RayTracingGiQuality;
        renderer.RayTracingAdaptiveQualityEnabled = RayTracingAdaptiveQuality;
    }

    public static ProjectSettings FromRenderer(RendererModule renderer, bool vsync)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        return new ProjectSettings
        {
            Exposure = renderer.Exposure,
            LodPixelBudget = renderer.LodPixelBudget,
            Shadows = renderer.ShadowSettings,
            ShadowConstantBias = renderer.ShadowConstantBias,
            ShadowSlopeBias = renderer.ShadowSlopeBias,
            ScreenSpaceGiEnabled = renderer.ScreenSpaceGIEnabled,
            ScreenSpaceGiIntensity = renderer.ScreenSpaceGIIntensity,
            ScreenSpaceGiRadius = renderer.ScreenSpaceGIRadius,
            RayTracingEnabled = renderer.RayTracingEnabled,
            RayTracingShadowsEnabled = renderer.RayTracingShadowsEnabled,
            RayTracingReflectionsEnabled = renderer.RayTracingReflectionsEnabled,
            RayTracingGiEnabled = renderer.RayTracingGiEnabled,
            RayTracingReflectionStrength = renderer.RayTracingReflectionStrength,
            RayTracingSamples = renderer.RayTracingSamples,
            RayTracingResolutionScale = renderer.RayTracingResolutionScale,
            RayTracingRayBias = renderer.RayTracingRayBias,
            RayTracingMaxDistance = renderer.RayTracingMaxDistance,
            RayTracingReflectionRoughnessLimit = renderer.RayTracingReflectionRoughnessLimit,
            RayTracingShadowQuality = renderer.RayTracingShadowQuality,
            RayTracingReflectionQuality = renderer.RayTracingReflectionQuality,
            RayTracingGiQuality = renderer.RayTracingGiQuality,
            RayTracingAdaptiveQuality = renderer.RayTracingAdaptiveQualityEnabled,
            VSync = vsync,
        };
    }
}
