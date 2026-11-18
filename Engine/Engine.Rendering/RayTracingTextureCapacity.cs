using Engine.RHI;

namespace Engine.Rendering;

internal static class RayTracingTextureCapacity
{
    public const int TexturesPerMaterial = 6;
    public const int MaxMaterialSets = 256;
    private const uint ReflectionClosestHitFixedSampledImages = 3;
    private const uint ReflectionPipelineFixedSampledImages = 6;

    public static int Calculate(FeatureInfo features)
    {
        ArgumentNullException.ThrowIfNull(features);
        return Calculate(features.MaxPerStageSampledImages, features.MaxDescriptorSetSampledImages);
    }

    internal static int Calculate(uint maxPerStageSampledImages, uint maxDescriptorSetSampledImages)
    {
        uint perStageAvailable = maxPerStageSampledImages > ReflectionClosestHitFixedSampledImages
            ? maxPerStageSampledImages - ReflectionClosestHitFixedSampledImages
            : 0;
        uint descriptorSetAvailable = maxDescriptorSetSampledImages > ReflectionPipelineFixedSampledImages
            ? maxDescriptorSetSampledImages - ReflectionPipelineFixedSampledImages
            : 0;
        uint available = Math.Min(perStageAvailable, descriptorSetAvailable);
        uint maximumArraySize = (uint)(MaxMaterialSets * TexturesPerMaterial);
        uint capacity = Math.Min(available, maximumArraySize) / TexturesPerMaterial * TexturesPerMaterial;
        if (capacity < TexturesPerMaterial)
        {
            throw new NotSupportedException(
                "Ray tracing sampled-image limits cannot fit the fixed RT reflection descriptors and one six-texture material set.");
        }
        return (int)capacity;
    }
}
