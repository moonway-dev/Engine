using VkSemaphore = Silk.NET.Vulkan.Semaphore;
using Engine.Core;
using Engine.RHI;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace Engine.Backends.Vulkan;

internal unsafe sealed class VulkanDevice : IDisposable
{
    private readonly Vk _vk;
    private readonly Instance _instance;
    private readonly ILogger _log;
    private KhrSwapchain? _swapchainExtension;
    private KhrAccelerationStructure? _accelerationStructureExtension;
    private KhrRayTracingPipeline? _rayTracingPipelineExtension;
    internal object QueueLock { get; } = new();

    public VulkanPhysicalDevice Physical { get; }

    public Device Handle { get; }

    public Queue GraphicsQueue { get; }

    public Queue PresentQueue { get; }

    public Queue ComputeQueue { get; }

    public Queue TransferQueue { get; }

    public bool SupportsCompute { get; }

    public bool SupportsRayTracing { get; }

    public bool SupportsAccelerationStructures { get; }

    public bool SupportsRayQuery { get; }

    public bool SupportsBufferDeviceAddress { get; }

    public IReadOnlyList<string> EnabledExtensions { get; }

    public string? RayTracingUnavailableReason { get; }

    public KhrSwapchain SwapchainExtension =>
        _swapchainExtension ?? throw new VulkanException(Result.ErrorExtensionNotPresent, "VK_KHR_swapchain extension");

    public KhrAccelerationStructure AccelerationStructureExtension =>
        _accelerationStructureExtension ?? throw new VulkanException(Result.ErrorExtensionNotPresent, "VK_KHR_acceleration_structure extension");

    public KhrRayTracingPipeline RayTracingPipelineExtension =>
        _rayTracingPipelineExtension ?? throw new VulkanException(Result.ErrorExtensionNotPresent, "VK_KHR_ray_tracing_pipeline extension");

    public VulkanDevice(Vk vk, Instance instance, VulkanPhysicalDevice physical, RayTracingMode rayTracingMode, ILogger log)
    {
        _vk = vk;
        _instance = instance;
        _log = log;
        Physical = physical;
        SupportsCompute = physical.SupportsCompute;
        bool enableOptionalRt = rayTracingMode != RayTracingMode.Off;
        SupportsBufferDeviceAddress = enableOptionalRt && physical.RayTracing.BufferDeviceAddress;
        SupportsAccelerationStructures = enableOptionalRt && physical.RayTracing.AccelerationStructures;
        SupportsRayTracing = enableOptionalRt && physical.RayTracing.Pipelines;
        SupportsRayQuery = enableOptionalRt && physical.RayTracing.RayQuery;
        RayTracingUnavailableReason = SupportsRayTracing ? null : physical.RayTracing.UnavailableReason;
        if (rayTracingMode == RayTracingMode.Required && !SupportsRayTracing)
            throw new VulkanException(Result.ErrorFeatureNotPresent,
                RayTracingUnavailableReason ?? "Ray tracing is required but unavailable.");

        List<string> requiredExtensions = [VulkanHelpers.ExtensionSwapchain];
        if (SupportsAccelerationStructures)
        {
            requiredExtensions.Add(VulkanHelpers.ExtensionDeferredHostOperations);
            requiredExtensions.Add(VulkanHelpers.ExtensionAccelerationStructure);
        }
        if (SupportsRayTracing)
            requiredExtensions.Add(VulkanHelpers.ExtensionRayTracingPipeline);
        if (SupportsRayQuery)
            requiredExtensions.Add(VulkanHelpers.ExtensionRayQuery);
        EnabledExtensions = requiredExtensions.ToArray();
        foreach (string extension in requiredExtensions)
        {
            if (!VulkanPhysicalDevice.SupportsDeviceExtension(vk, physical.Handle, extension))
                throw new VulkanException(Result.ErrorExtensionNotPresent, $"Device extension '{extension}'");
        }

        PhysicalDeviceFeatures enabledCoreFeatures = new()
        {
            ShaderStorageImageReadWithoutFormat = SupportsCompute,
            ShaderStorageImageWriteWithoutFormat = SupportsCompute,
        };
        PhysicalDeviceRayQueryFeaturesKHR rayQueryFeatures = new()
        {
            SType = StructureType.PhysicalDeviceRayQueryFeaturesKhr,
            RayQuery = SupportsRayQuery,
        };
        PhysicalDeviceRayTracingPipelineFeaturesKHR rayTracingPipelineFeatures = new()
        {
            SType = StructureType.PhysicalDeviceRayTracingPipelineFeaturesKhr,
            PNext = SupportsRayQuery ? &rayQueryFeatures : null,
            RayTracingPipeline = SupportsRayTracing,
        };
        PhysicalDeviceAccelerationStructureFeaturesKHR accelerationStructureFeatures = new()
        {
            SType = StructureType.PhysicalDeviceAccelerationStructureFeaturesKhr,
            PNext = SupportsRayTracing ? &rayTracingPipelineFeatures : SupportsRayQuery ? &rayQueryFeatures : null,
            AccelerationStructure = SupportsAccelerationStructures,
        };
        PhysicalDeviceVulkan12Features features12 = new()
        {
            SType = StructureType.PhysicalDeviceVulkan12Features,
            PNext = SupportsAccelerationStructures ? &accelerationStructureFeatures
                : SupportsRayTracing ? &rayTracingPipelineFeatures
                : SupportsRayQuery ? &rayQueryFeatures : null,
            BufferDeviceAddress = SupportsBufferDeviceAddress,
            ShaderSampledImageArrayNonUniformIndexing = SupportsRayTracing && physical.RayTracing.NonUniformSampledImages,
        };
        PhysicalDeviceVulkan11Features features11 = new()
        {
            SType = StructureType.PhysicalDeviceVulkan11Features,
            PNext = &features12,
            ShaderDrawParameters = true,
        };
        PhysicalDeviceVulkan13Features features13 = new()
        {
            SType = StructureType.PhysicalDeviceVulkan13Features,
            PNext = &features11,
            DynamicRendering = physical.SupportsDynamicRendering,
            Synchronization2 = physical.SupportsSynchronization2,
        };

        List<DeviceQueueCreateInfo> queueInfos = [];
        float priority = 1f;
        foreach (uint family in new[] { physical.GraphicsFamilyIndex, physical.PresentFamilyIndex, physical.ComputeFamilyIndex, physical.TransferFamilyIndex }.Distinct())
        {
            queueInfos.Add(new DeviceQueueCreateInfo
            {
                SType = StructureType.DeviceQueueCreateInfo,
                QueueFamilyIndex = family,
                QueueCount = 1,
                PQueuePriorities = &priority,
            });
        }

        DeviceQueueCreateInfo* queueInfoPointer = stackalloc DeviceQueueCreateInfo[queueInfos.Count];
        for (int i = 0; i < queueInfos.Count; i++)
            queueInfoPointer[i] = queueInfos[i];

        uint extensionCount = 0;
        byte** extensionPointers = VulkanHelpers.AllocUtf8Array(requiredExtensions, out extensionCount);
        try
        {
            DeviceCreateInfo createInfo = new()
            {
                SType = StructureType.DeviceCreateInfo,
                PNext = &features13,
                QueueCreateInfoCount = (uint)queueInfos.Count,
                PQueueCreateInfos = queueInfoPointer,
                EnabledExtensionCount = extensionCount,
                PpEnabledExtensionNames = extensionPointers,
                PEnabledFeatures = &enabledCoreFeatures,
            };
            vk.CreateDevice(physical.Handle, &createInfo, null, out Device device)
                .ThrowIfNotSuccess(log, "vkCreateDevice");
            Handle = device;
        }
        finally
        {
            VulkanHelpers.FreeUtf8Array(extensionPointers, requiredExtensions.Count);
        }

        GraphicsQueue = GetQueue(physical.GraphicsFamilyIndex);
        PresentQueue = GetQueue(physical.PresentFamilyIndex);
        ComputeQueue = GetQueue(physical.ComputeFamilyIndex);
        TransferQueue = GetQueue(physical.TransferFamilyIndex);

        if (!vk.TryGetDeviceExtension(instance, Handle, out KhrSwapchain? swapchainExtension))
            throw new VulkanException(Result.ErrorExtensionNotPresent, "Loading VK_KHR_swapchain");
        _swapchainExtension = swapchainExtension;
        if (SupportsAccelerationStructures)
        {
            if (!vk.TryGetDeviceExtension(instance, Handle, out KhrAccelerationStructure? accelerationStructureExtension))
                throw new VulkanException(Result.ErrorExtensionNotPresent, "Loading VK_KHR_acceleration_structure");
            _accelerationStructureExtension = accelerationStructureExtension;
        }
        if (SupportsRayTracing)
        {
            if (!vk.TryGetDeviceExtension(instance, Handle, out KhrRayTracingPipeline? rayTracingPipelineExtension))
                throw new VulkanException(Result.ErrorExtensionNotPresent, "Loading VK_KHR_ray_tracing_pipeline");
            _rayTracingPipelineExtension = rayTracingPipelineExtension;
        }
        log.Debug($"Vulkan logical device created ({queueInfos.Count} queue family(ies), compute IBL: {SupportsCompute}, BDA: {SupportsBufferDeviceAddress}, AS: {SupportsAccelerationStructures}, RT pipeline: {SupportsRayTracing}, ray query: {SupportsRayQuery})");
    }

    private Queue GetQueue(uint familyIndex)
    {
        _vk.GetDeviceQueue(Handle, familyIndex, 0, out Queue queue);
        return queue;
    }

    public Result WaitIdle()
    {
        lock (QueueLock)
            return _vk.DeviceWaitIdle(Handle);
    }

    public void WaitForPresentation()
    {
        Result graphicsResult;
        lock (QueueLock)
            graphicsResult = _vk.QueueWaitIdle(GraphicsQueue);
        VulkanHelpers.Throw(graphicsResult, "vkQueueWaitIdle (graphics presentation)");
        if (PresentQueue.Handle != GraphicsQueue.Handle)
        {
            Result presentResult;
            lock (QueueLock)
                presentResult = _vk.QueueWaitIdle(PresentQueue);
            VulkanHelpers.Throw(presentResult, "vkQueueWaitIdle (present)");
        }
    }

    public void Dispose()
    {
        _vk.DestroyDevice(Handle, null);
    }
}
