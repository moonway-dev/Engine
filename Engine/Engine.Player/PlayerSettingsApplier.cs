using System.Text.Json;
using Engine.Core;
using Engine.Rendering;

namespace Engine.Player;

internal static class PlayerSettingsApplier
{
    public static bool ReadVSync(string projectRoot)
    {
        using JsonDocument? document = TryLoad(Path.Combine(projectRoot, "Project.project"));
        if (document is null)
            return true;
        return document.RootElement.TryGetProperty("VSync", out JsonElement value) && value.ValueKind == JsonValueKind.True;
    }

    public static void Apply(RendererModule renderer, string projectRoot)
    {
        using JsonDocument? document = TryLoad(Path.Combine(projectRoot, "Project.project"));
        if (document is null)
            return;
        JsonElement root = document.RootElement;

        renderer.Exposure = GetFloat(root, "Exposure", renderer.Exposure);
        renderer.LodPixelBudget = GetFloat(root, "LodPixelBudget", renderer.LodPixelBudget);
        renderer.ShadowIntensity = GetFloat(root, "ShadowIntensity", renderer.ShadowIntensity);
        renderer.ShadowConstantBias = GetFloat(root, "ShadowConstantBias", renderer.ShadowConstantBias);
        renderer.ShadowSlopeBias = GetFloat(root, "ShadowSlopeBias", renderer.ShadowSlopeBias);
        if (TryGetInt(root, "GiMode", out int giMode))
            renderer.GiMode = (GlobalIlluminationMode)giMode;
        renderer.ScreenSpaceGIIntensity = GetFloat(root, "ScreenSpaceGiIntensity", renderer.ScreenSpaceGIIntensity);
        renderer.ScreenSpaceGIRadius = GetFloat(root, "ScreenSpaceGiRadius", renderer.ScreenSpaceGIRadius);
        renderer.RayTracingEnabled = GetBool(root, "RayTracingEnabled", renderer.RayTracingEnabled);
        renderer.RayTracingReflectionsEnabled = GetBool(root, "RayTracingReflectionsEnabled", renderer.RayTracingReflectionsEnabled);
        renderer.RayTracingReflectionStrength = GetFloat(root, "RayTracingReflectionStrength", renderer.RayTracingReflectionStrength);
        renderer.RayTracingSamples = GetInt(root, "RayTracingSamples", renderer.RayTracingSamples);
        renderer.RayTracingResolutionScale = GetFloat(root, "RayTracingResolutionScale", renderer.RayTracingResolutionScale);
        renderer.RayTracingRayBias = GetFloat(root, "RayTracingRayBias", renderer.RayTracingRayBias);
        renderer.RayTracingMaxDistance = GetFloat(root, "RayTracingMaxDistance", renderer.RayTracingMaxDistance);
        renderer.RayTracingReflectionRoughnessLimit = GetFloat(root, "RayTracingReflectionRoughnessLimit", renderer.RayTracingReflectionRoughnessLimit);
        if (TryGetInt(root, "ShadowMode", out int shadowMode))
            renderer.ShadowMode = (ShadowMode)shadowMode;
        if (TryGetInt(root, "RayTracingShadowQuality", out int shadowQuality))
            renderer.RayTracingShadowQuality = (RayTracingQualityPreset)shadowQuality;
        if (TryGetInt(root, "RayTracingReflectionQuality", out int reflectionQuality))
            renderer.RayTracingReflectionQuality = (RayTracingQualityPreset)reflectionQuality;
        if (TryGetInt(root, "RayTracingGiQuality", out int giQuality))
            renderer.RayTracingGiQuality = (RayTracingQualityPreset)giQuality;
        renderer.RayTracingAdaptiveQualityEnabled = GetBool(root, "RayTracingAdaptiveQuality", renderer.RayTracingAdaptiveQualityEnabled);

        if (root.TryGetProperty("Shadows", out JsonElement shadows) && shadows.ValueKind == JsonValueKind.Object)
        {
            CascadedShadowSettings current = renderer.ShadowSettings;
            CascadedShadowSettings settings = new()
            {
                Resolution = GetInt(shadows, "Resolution", current.Resolution),
                Distance = GetFloat(shadows, "Distance", current.Distance),
                SplitLambda = GetFloat(shadows, "SplitLambda", current.SplitLambda),
                BlendFraction = GetFloat(shadows, "BlendFraction", current.BlendFraction),
                FadeFraction = GetFloat(shadows, "FadeFraction", current.FadeFraction),
                Strength = GetFloat(shadows, "Strength", current.Strength),
                ReceiverBias = GetFloat(shadows, "ReceiverBias", current.ReceiverBias),
                NormalBias = GetFloat(shadows, "NormalBias", current.NormalBias),
                AngularRadius = GetFloat(shadows, "AngularRadius", current.AngularRadius),
                FilterRadius = GetFloat(shadows, "FilterRadius", current.FilterRadius),
                Quality = TryGetInt(shadows, "Quality", out int quality) ? (ShadowQuality)quality : current.Quality,
                DebugCascades = GetBool(shadows, "DebugCascades", current.DebugCascades),
            };
            try
            {
                settings.Validate();
                renderer.ShadowSettings = settings;
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }
    }

    private static JsonDocument? TryLoad(string path)
    {
        string? json = VirtualFileSystem.TryReadAllText(path);
        if (json is null)
            return null;
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static float GetFloat(JsonElement element, string name, float fallback) =>
        TryGetFloat(element, name, out float value) ? value : fallback;

    private static int GetInt(JsonElement element, string name, int fallback) =>
        TryGetInt(element, name, out int value) ? value : fallback;

    private static bool GetBool(JsonElement element, string name, bool fallback) =>
        TryGetBool(element, name, out bool value) ? value : fallback;

    private static bool TryGetFloat(JsonElement element, string name, out float value)
    {
        value = default;
        if (!element.TryGetProperty(name, out JsonElement property))
            return false;
        if (property.ValueKind == JsonValueKind.Number && property.TryGetSingle(out float number))
        {
            value = number;
            return true;
        }
        return false;
    }

    private static bool TryGetInt(JsonElement element, string name, out int value)
    {
        value = default;
        if (!element.TryGetProperty(name, out JsonElement property))
            return false;
        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int number))
        {
            value = number;
            return true;
        }
        return false;
    }

    private static bool TryGetBool(JsonElement element, string name, out bool value)
    {
        value = default;
        if (!element.TryGetProperty(name, out JsonElement property))
            return false;
        if (property.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = property.GetBoolean();
            return true;
        }
        return false;
    }
}
