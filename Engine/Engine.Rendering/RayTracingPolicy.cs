using Engine.RHI;

namespace Engine.Rendering;

public static class RayTracingPolicy
{
    public static bool IsSupported(FeatureInfo features) =>
        features.SupportsAccelerationStructures && features.SupportsRayTracing;

    public static bool ShouldEnable(RayTracingMode mode, FeatureInfo features)
    {
        ArgumentNullException.ThrowIfNull(features);
        bool supported = IsSupported(features);
        if (mode == RayTracingMode.Required && !supported)
        {
            string reason = features.RayTracingUnavailableReason is { Length: > 0 } unavailableReason
                ? $" {unavailableReason}"
                : string.Empty;
            throw new NotSupportedException(
                $"Ray tracing was required, but the device must support both acceleration structures and ray-tracing pipelines.{reason}");
        }

        return mode switch
        {
            RayTracingMode.Off => false,
            RayTracingMode.Auto => supported,
            RayTracingMode.Required => true,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
    }
}
