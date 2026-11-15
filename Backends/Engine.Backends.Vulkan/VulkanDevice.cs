using VkSemaphore = Silk.NET.Vulkan.Semaphore;
using Engine.Core;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace Engine.Backends.Vulkan;

internal unsafe sealed class VulkanDevice : IDisposable
{
    private readonly Vk _vk;
    private readonly Instance _instance;
    private readonly ILogger _log;
    private KhrSwapchain? _swapchainExtension;

    public VulkanPhysicalDevice Physical { get; }

    public Device Handle { get; }

    public Queue GraphicsQueue { get; }

    public Queue PresentQueue { get; }

    public Queue ComputeQueue { get; }

    public Queue TransferQueue { get; }

    public KhrSwapchain SwapchainExtension =>
        _swapchainExtension ?? throw new VulkanException(Result.ErrorExtensionNotPresent, "VK_KHR_swapchain extension");

    public VulkanDevice(Vk vk, Instance instance, VulkanPhysicalDevice physical, ILogger log)
    {
        _vk = vk;
        _instance = instance;
        _log = log;
        Physical = physical;

        List<string> requiredExtensions = [VulkanHelpers.ExtensionSwapchain];
        foreach (string extension in requiredExtensions)
        {
            if (!VulkanPhysicalDevice.SupportsDeviceExtension(vk, physical.Handle, extension))
                throw new VulkanException(Result.ErrorExtensionNotPresent, $"Device extension '{extension}'");
        }

        PhysicalDeviceVulkan11Features features11 = new()
        {
            SType = StructureType.PhysicalDeviceVulkan11Features,
            ShaderDrawParameters = true,
        };
        PhysicalDeviceVulkan13Features features13 = new()
        {
            SType = StructureType.PhysicalDeviceVulkan13Features,
            PNext = &features11,
            DynamicRendering = true,
            Synchronization2 = true,
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

        log.Debug($"Vulkan logical device created ({queueInfos.Count} queue family(ies))");
    }

    private Queue GetQueue(uint familyIndex)
    {
        _vk.GetDeviceQueue(Handle, familyIndex, 0, out Queue queue);
        return queue;
    }

    public void WaitIdle() => _vk.DeviceWaitIdle(Handle);

    public void WaitForPresentation()
    {
        _vk.QueueWaitIdle(GraphicsQueue);
        if (PresentQueue.Handle != GraphicsQueue.Handle)
            _vk.QueueWaitIdle(PresentQueue);
    }

    public void Dispose()
    {
        _vk.DestroyDevice(Handle, null);
    }
}
