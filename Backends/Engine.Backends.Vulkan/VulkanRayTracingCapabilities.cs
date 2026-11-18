using Engine.RHI;
using Silk.NET.Vulkan;

namespace Engine.Backends.Vulkan;

internal unsafe sealed record VulkanRayTracingCapabilities
{
    public bool BufferDeviceAddress { get; init; }
    public bool AccelerationStructures { get; init; }
    public bool Pipelines { get; init; }
    public bool RayQuery { get; init; }
    public bool NonUniformSampledImages { get; init; }
    public RayTracingLimits Limits { get; init; } = new();
    public string? BufferDeviceAddressUnavailableReason { get; init; }
    public string? AccelerationStructuresUnavailableReason { get; init; }
    public string? PipelineUnavailableReason { get; init; }
    public string? RayQueryUnavailableReason { get; init; }

    public string? UnavailableReason => PipelineUnavailableReason;

    public static VulkanRayTracingCapabilities Query(Vk vk, PhysicalDevice physical, bool graphicsSubmissionQueueSupportsCompute)
    {
        bool hasAs = VulkanPhysicalDevice.SupportsDeviceExtension(vk, physical, VulkanHelpers.ExtensionAccelerationStructure);
        bool hasPipeline = VulkanPhysicalDevice.SupportsDeviceExtension(vk, physical, VulkanHelpers.ExtensionRayTracingPipeline);
        bool hasDeferred = VulkanPhysicalDevice.SupportsDeviceExtension(vk, physical, VulkanHelpers.ExtensionDeferredHostOperations);
        bool hasQuery = VulkanPhysicalDevice.SupportsDeviceExtension(vk, physical, VulkanHelpers.ExtensionRayQuery);
        PhysicalDeviceRayQueryFeaturesKHR query = new() { SType = StructureType.PhysicalDeviceRayQueryFeaturesKhr };
        PhysicalDeviceRayTracingPipelineFeaturesKHR pipeline = new()
        {
            SType = StructureType.PhysicalDeviceRayTracingPipelineFeaturesKhr,
            PNext = hasQuery ? &query : null,
        };
        PhysicalDeviceAccelerationStructureFeaturesKHR acceleration = new()
        {
            SType = StructureType.PhysicalDeviceAccelerationStructureFeaturesKhr,
            PNext = hasPipeline ? &pipeline : hasQuery ? &query : null,
        };
        PhysicalDeviceVulkan12Features features12 = new()
        {
            SType = StructureType.PhysicalDeviceVulkan12Features,
            PNext = hasAs ? &acceleration : hasPipeline ? &pipeline : hasQuery ? &query : null,
        };
        PhysicalDeviceFeatures2 features = new() { SType = StructureType.PhysicalDeviceFeatures2, PNext = &features12 };
        vk.GetPhysicalDeviceFeatures2(physical, &features);

        bool bda = features12.BufferDeviceAddress;
        bool accelerationStructures = graphicsSubmissionQueueSupportsCompute
            && bda
            && hasAs
            && hasDeferred
            && acceleration.AccelerationStructure;
        bool pipelines = accelerationStructures && hasPipeline && pipeline.RayTracingPipeline;
        bool rayQuery = accelerationStructures && hasQuery && query.RayQuery;

        string? bdaReason = bda ? null : "bufferDeviceAddress feature";
        List<string> asMissing = [];
        if (!graphicsSubmissionQueueSupportsCompute)
            asMissing.Add("no selected graphics/compute queue supports VK_QUEUE_COMPUTE_BIT");
        if (!bda)
            asMissing.Add("bufferDeviceAddress feature");
        if (!hasAs)
            asMissing.Add(VulkanHelpers.ExtensionAccelerationStructure);
        if (!hasDeferred)
            asMissing.Add(VulkanHelpers.ExtensionDeferredHostOperations);
        if (hasAs && !acceleration.AccelerationStructure)
            asMissing.Add("accelerationStructure feature");
        string? asReason = accelerationStructures ? null : "Acceleration structures unavailable: " + string.Join(", ", asMissing);

        List<string> pipelineMissing = [];
        if (!accelerationStructures)
            pipelineMissing.Add(asReason!);
        if (!hasPipeline)
            pipelineMissing.Add(VulkanHelpers.ExtensionRayTracingPipeline);
        if (hasPipeline && !pipeline.RayTracingPipeline)
            pipelineMissing.Add("rayTracingPipeline feature");
        string? pipelineReason = pipelines ? null : "Ray tracing pipeline unavailable: " + string.Join(", ", pipelineMissing);

        List<string> queryMissing = [];
        if (!accelerationStructures)
            queryMissing.Add(asReason!);
        if (!hasQuery)
            queryMissing.Add(VulkanHelpers.ExtensionRayQuery);
        if (hasQuery && !query.RayQuery)
            queryMissing.Add("rayQuery feature");
        string? queryReason = rayQuery ? null : "Ray query unavailable: " + string.Join(", ", queryMissing);

        RayTracingLimits limits = new();
        if (accelerationStructures)
        {
            PhysicalDeviceRayTracingPipelinePropertiesKHR pipelineProperties = new()
            {
                SType = StructureType.PhysicalDeviceRayTracingPipelinePropertiesKhr,
            };
            PhysicalDeviceAccelerationStructurePropertiesKHR asProperties = new()
            {
                SType = StructureType.PhysicalDeviceAccelerationStructurePropertiesKhr,
                PNext = pipelines ? &pipelineProperties : null,
            };
            PhysicalDeviceProperties2 properties = new() { SType = StructureType.PhysicalDeviceProperties2, PNext = &asProperties };
            vk.GetPhysicalDeviceProperties2(physical, &properties);
            limits = new RayTracingLimits
            {
                ShaderGroupHandleSize = pipelines ? pipelineProperties.ShaderGroupHandleSize : 0,
                ShaderGroupHandleAlignment = pipelines ? pipelineProperties.ShaderGroupHandleAlignment : 0,
                ShaderGroupBaseAlignment = pipelines ? pipelineProperties.ShaderGroupBaseAlignment : 0,
                MaxShaderGroupStride = pipelines ? pipelineProperties.MaxShaderGroupStride : 0,
                MaxRayRecursionDepth = pipelines ? pipelineProperties.MaxRayRecursionDepth : 0,
                MaxRayDispatchInvocationCount = pipelines ? pipelineProperties.MaxRayDispatchInvocationCount : 0,
                MinScratchAlignment = asProperties.MinAccelerationStructureScratchOffsetAlignment,
                MaxGeometryCount = asProperties.MaxGeometryCount,
                MaxPrimitiveCount = asProperties.MaxPrimitiveCount,
                MaxInstanceCount = asProperties.MaxInstanceCount,
            };
        }

        return new VulkanRayTracingCapabilities
        {
            BufferDeviceAddress = bda,
            AccelerationStructures = accelerationStructures,
            Pipelines = pipelines,
            RayQuery = rayQuery,
            NonUniformSampledImages = features12.ShaderSampledImageArrayNonUniformIndexing,
            Limits = limits,
            BufferDeviceAddressUnavailableReason = bdaReason,
            AccelerationStructuresUnavailableReason = asReason,
            PipelineUnavailableReason = pipelineReason,
            RayQueryUnavailableReason = queryReason,
        };
    }
}
