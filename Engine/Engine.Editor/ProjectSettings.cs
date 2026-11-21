using System.Text.Json;
using Engine.Mathematics;
using Engine.Rendering;
using Engine.Editor.Build;
using Engine.Physics;

namespace Engine.Editor;

public sealed class ProjectSettings
{
    public float Exposure = 1f;
    public float LodPixelBudget = 320f;
    public CascadedShadowSettings Shadows = new();
    public float ShadowIntensity = 1f;
    public float ShadowConstantBias = 1.25f;
    public float ShadowSlopeBias = 1.75f;
    public GlobalIlluminationMode GiMode = GlobalIlluminationMode.ScreenSpace;
    public float ScreenSpaceGiIntensity = 0.5f;
    public float ScreenSpaceGiRadius = 1.5f;
    public bool RayTracingEnabled = true;
    public bool RayTracingReflectionsEnabled = true;
    public float RayTracingReflectionStrength = 0.6f;
    public int RayTracingSamples = 2;
    public float RayTracingResolutionScale = 1f;
    public float RayTracingRayBias = 0.002f;
    public float RayTracingMaxDistance = 200f;
    public float RayTracingReflectionRoughnessLimit = 0.3f;
    public ShadowMode ShadowMode = ShadowMode.Cascaded;
    public RayTracingQualityPreset RayTracingShadowQuality = RayTracingQualityPreset.High;
    public RayTracingQualityPreset RayTracingReflectionQuality = RayTracingQualityPreset.High;
    public RayTracingQualityPreset RayTracingGiQuality = RayTracingQualityPreset.High;
    public bool RayTracingAdaptiveQuality = true;
    public string LastScene = string.Empty;
    public bool VSync = true;
    public List<string> PhysicsLayers = ["Default"];
    public List<long> LayerCollisionMasks = [-1L];
    public EngineBuildProfile Build = new();

    public long MaskForLayer(int layer)
    {
        if (layer < 0)
            return -1L;
        if (layer < LayerCollisionMasks.Count)
            return LayerCollisionMasks[layer];
        return -1L;
    }

    public void NormalizeLayers()
    {
        if (PhysicsLayers.Count == 0)
            PhysicsLayers.Add("Default");
        if (PhysicsLayers.Count > global::Engine.Physics.PhysicsLayers.MaxLayers)
            PhysicsLayers.RemoveRange(global::Engine.Physics.PhysicsLayers.MaxLayers, PhysicsLayers.Count - global::Engine.Physics.PhysicsLayers.MaxLayers);
        if (LayerCollisionMasks.Count > PhysicsLayers.Count)
            LayerCollisionMasks.RemoveRange(PhysicsLayers.Count, LayerCollisionMasks.Count - PhysicsLayers.Count);
        while (LayerCollisionMasks.Count < PhysicsLayers.Count)
            LayerCollisionMasks.Add(-1L);
        if (string.IsNullOrWhiteSpace(PhysicsLayers[0]))
            PhysicsLayers[0] = "Default";
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        IncludeFields = true,
    };

    public static ProjectSettings Load(string path)
    {
        if (!File.Exists(path))
            return new ProjectSettings();
        string json = File.ReadAllText(path);
        ProjectSettings settings = JsonSerializer.Deserialize<ProjectSettings>(json, SerializerOptions)
            ?? throw new InvalidOperationException($"Project settings file '{path}' is empty or invalid.");
        settings.ApplyLegacyFields(json);
        settings.Shadows.Validate();
        return settings;
    }

    private void ApplyLegacyFields(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return;
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "ScreenSpaceGiEnabled" when property.Value.ValueKind == JsonValueKind.True && GiMode == GlobalIlluminationMode.ScreenSpace:
                        GiMode = GlobalIlluminationMode.ScreenSpace;
                        break;
                    case "ScreenSpaceGiEnabled" when property.Value.ValueKind == JsonValueKind.False:
                        GiMode = GlobalIlluminationMode.Off;
                        break;
                    case "RayTracingGiEnabled" when property.Value.ValueKind == JsonValueKind.True && RayTracingEnabled:
                        GiMode = GlobalIlluminationMode.RayTraced;
                        break;
                    case "RayTracingShadowsEnabled" when property.Value.ValueKind == JsonValueKind.True && RayTracingEnabled:
                        ShadowMode = ShadowMode.RayTraced;
                        break;
                    case "RayTracingShadowsEnabled" when property.Value.ValueKind == JsonValueKind.False && ShadowMode == ShadowMode.Cascaded:
                        ShadowMode = ShadowMode.Cascaded;
                        break;
                }
            }
        }
        catch (JsonException)
        {
        }
    }

    public static void WriteLastScene(string projectRoot, string scenePath)
    {
        ProjectSettings settings = Load(projectFile(projectRoot));
        settings.LastScene = Path.GetRelativePath(projectRoot, scenePath).Replace(Path.DirectorySeparatorChar, '/');
        settings.Save(projectFile(projectRoot));
    }

    private static string projectFile(string projectRoot) => Path.Combine(projectRoot, "Project.project");

    public static string? ReadLastScene(string projectRoot)
    {
        try
        {
            string projectFile = Path.Combine(projectRoot, "Project.project");
            if (!File.Exists(projectFile))
                return null;
            string relative = Load(projectFile).LastScene;
            if (string.IsNullOrWhiteSpace(relative))
                return null;
            string full = Path.Combine(projectRoot, relative);
            return File.Exists(full) ? full : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
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
        renderer.ShadowIntensity = ShadowIntensity;
        renderer.ShadowConstantBias = ShadowConstantBias;
        renderer.ShadowSlopeBias = ShadowSlopeBias;
        renderer.GiMode = GiMode;
        renderer.ScreenSpaceGIIntensity = ScreenSpaceGiIntensity;
        renderer.ScreenSpaceGIRadius = ScreenSpaceGiRadius;
        renderer.RayTracingEnabled = RayTracingEnabled;
        renderer.RayTracingReflectionsEnabled = RayTracingReflectionsEnabled;
        renderer.RayTracingReflectionStrength = RayTracingReflectionStrength;
        renderer.RayTracingSamples = RayTracingSamples;
        renderer.RayTracingResolutionScale = RayTracingResolutionScale;
        renderer.RayTracingRayBias = RayTracingRayBias;
        renderer.RayTracingMaxDistance = RayTracingMaxDistance;
        renderer.RayTracingReflectionRoughnessLimit = RayTracingReflectionRoughnessLimit;
        renderer.ShadowMode = ShadowMode;
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
            ShadowIntensity = renderer.ShadowIntensity,
            ShadowConstantBias = renderer.ShadowConstantBias,
            ShadowSlopeBias = renderer.ShadowSlopeBias,
            GiMode = renderer.GiMode,
            ScreenSpaceGiIntensity = renderer.ScreenSpaceGIIntensity,
            ScreenSpaceGiRadius = renderer.ScreenSpaceGIRadius,
            RayTracingEnabled = renderer.RayTracingEnabled,
            RayTracingReflectionsEnabled = renderer.RayTracingReflectionsEnabled,
            RayTracingReflectionStrength = renderer.RayTracingReflectionStrength,
            RayTracingSamples = renderer.RayTracingSamples,
            RayTracingResolutionScale = renderer.RayTracingResolutionScale,
            RayTracingRayBias = renderer.RayTracingRayBias,
            RayTracingMaxDistance = renderer.RayTracingMaxDistance,
            RayTracingReflectionRoughnessLimit = renderer.RayTracingReflectionRoughnessLimit,
            ShadowMode = renderer.ShadowMode,
            RayTracingShadowQuality = renderer.RayTracingShadowQuality,
            RayTracingReflectionQuality = renderer.RayTracingReflectionQuality,
            RayTracingGiQuality = renderer.RayTracingGiQuality,
            RayTracingAdaptiveQuality = renderer.RayTracingAdaptiveQualityEnabled,
            VSync = vsync,
        };
    }
}
