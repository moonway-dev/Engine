using VkSemaphore = Silk.NET.Vulkan.Semaphore;
using Engine.Core;
using Engine.Mathematics;
using Engine.RHI;
using Silk.NET.Core;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace Engine.Backends.Vulkan;

internal unsafe sealed class VulkanPhysicalDevice
{
    private readonly Vk _vk;
    private readonly Instance _instance;

    public PhysicalDevice Handle { get; }

    public PhysicalDeviceProperties Properties { get; }

    public PhysicalDeviceMemoryProperties MemoryProperties { get; }

    public uint GraphicsFamilyIndex { get; }

    public bool GraphicsSubmissionQueueSupportsCompute { get; }

    public uint PresentFamilyIndex { get; }

    public uint ComputeFamilyIndex { get; }

    public uint TransferFamilyIndex { get; }

    public uint GraphicsTimestampValidBits { get; }

    public bool SupportsStorageImageReadWithoutFormat { get; }

    public bool SupportsStorageImageWriteWithoutFormat { get; }

    public bool SupportsR16G16B16A16FloatStorageImage { get; }

    public bool SupportsCompute { get; }

    public bool SupportsDynamicRendering { get; }

    public bool SupportsSynchronization2 { get; }

    public VulkanRayTracingCapabilities RayTracing { get; }

    public string DeviceName
    {
        get
        {
            PhysicalDeviceProperties properties = Properties;
            byte* name = properties.DeviceName;
            return VulkanHelpers.ReadFixedString(name, 256);
        }
    }

    private VulkanPhysicalDevice(
        Vk vk,
        Instance instance,
        PhysicalDevice handle,
        in PhysicalDeviceProperties properties,
        in PhysicalDeviceMemoryProperties memoryProperties,
        uint graphicsFamily,
        bool graphicsSubmissionQueueSupportsCompute,
        uint presentFamily,
        uint computeFamily,
        uint transferFamily,
        uint graphicsTimestampValidBits,
        bool supportsDynamicRendering,
        bool supportsSynchronization2,
        bool supportsStorageImageReadWithoutFormat,
        bool supportsStorageImageWriteWithoutFormat,
        bool supportsR16G16B16A16FloatStorageImage,
        VulkanRayTracingCapabilities rayTracing)
    {
        _vk = vk;
        _instance = instance;
        Handle = handle;
        Properties = properties;
        MemoryProperties = memoryProperties;
        GraphicsFamilyIndex = graphicsFamily;
        GraphicsSubmissionQueueSupportsCompute = graphicsSubmissionQueueSupportsCompute;
        PresentFamilyIndex = presentFamily;
        ComputeFamilyIndex = computeFamily;
        TransferFamilyIndex = transferFamily;
        GraphicsTimestampValidBits = graphicsTimestampValidBits;
        SupportsDynamicRendering = supportsDynamicRendering;
        SupportsSynchronization2 = supportsSynchronization2;
        SupportsStorageImageReadWithoutFormat = supportsStorageImageReadWithoutFormat;
        SupportsStorageImageWriteWithoutFormat = supportsStorageImageWriteWithoutFormat;
        SupportsR16G16B16A16FloatStorageImage = supportsR16G16B16A16FloatStorageImage;
        RayTracing = rayTracing;
        SupportsCompute = graphicsSubmissionQueueSupportsCompute
            && supportsStorageImageReadWithoutFormat
            && supportsStorageImageWriteWithoutFormat
            && supportsR16G16B16A16FloatStorageImage;
    }

    public static VulkanPhysicalDevice Select(
        Vk vk,
        Instance instance,
        KhrSurface? khrSurface,
        SurfaceKHR? surface,
        int? preferredDeviceIndex,
        RayTracingMode rayTracingMode,
        ILogger log)
    {
        if (!Enum.IsDefined(rayTracingMode))
            throw new ArgumentOutOfRangeException(nameof(rayTracingMode));

        uint count = 0;
        vk.EnumeratePhysicalDevices(instance, &count, null);
        if (count == 0)
            throw new VulkanException(Result.ErrorInitializationFailed, "No Vulkan-capable physical devices found");
        PhysicalDevice[] devices = new PhysicalDevice[count];
        fixed (PhysicalDevice* devicesPointer = devices)
            vk.EnumeratePhysicalDevices(instance, &count, devicesPointer);

        VulkanPhysicalDevice? selected = null;
        long bestScore = long.MinValue;
        List<string> rejected = [];

        for (uint i = 0; i < count; i++)
        {
            PhysicalDevice device = devices[i];
            PhysicalDeviceProperties properties = default;
            vk.GetPhysicalDeviceProperties(device, &properties);
            string name = ReadDeviceName(&properties);
            string deviceType = properties.DeviceType switch
            {
                PhysicalDeviceType.DiscreteGpu => "discrete",
                PhysicalDeviceType.IntegratedGpu => "integrated",
                PhysicalDeviceType.VirtualGpu => "virtual",
                PhysicalDeviceType.Cpu => "cpu",
                _ => "other",
            };

            if (properties.ApiVersion < VulkanHelpers.MakeVersion(1, 3, 0))
            {
                rejected.Add($"{name} (Vulkan 1.3 required, device reports {VulkanHelpers.DecodeVersion(properties.ApiVersion)})");
                continue;
            }

            PhysicalDeviceVulkan13Features vulkan13Features = new()
            {
                SType = StructureType.PhysicalDeviceVulkan13Features,
            };
            PhysicalDeviceFeatures2 queriedFeatures = new()
            {
                SType = StructureType.PhysicalDeviceFeatures2,
                PNext = &vulkan13Features,
            };
            vk.GetPhysicalDeviceFeatures2(device, &queriedFeatures);
            if (!vulkan13Features.DynamicRendering || !vulkan13Features.Synchronization2)
            {
                List<string> missing = [];
                if (!vulkan13Features.DynamicRendering)
                    missing.Add("dynamicRendering feature");
                if (!vulkan13Features.Synchronization2)
                    missing.Add("synchronization2 feature");
                rejected.Add($"{name} (required Vulkan 1.3 features unavailable: {string.Join(", ", missing)})");
                continue;
            }

            if (!TryFindQueueFamilies(vk, device, khrSurface, surface, out QueueFamilies families))
            {
                rejected.Add($"{name} (no graphics/present queue pair)");
                continue;
            }

            VulkanRayTracingCapabilities rayTracing = VulkanRayTracingCapabilities.Query(
                vk, device, families.GraphicsSubmissionSupportsCompute);
            if (rayTracingMode == RayTracingMode.Required && !rayTracing.Pipelines)
            {
                rejected.Add($"{name} (ray tracing required: {rayTracing.UnavailableReason})");
                continue;
            }

            long score = properties.DeviceType switch
            {
                PhysicalDeviceType.DiscreteGpu => 1000,
                PhysicalDeviceType.IntegratedGpu => 200,
                PhysicalDeviceType.VirtualGpu => 100,
                _ => 0,
            };
            PhysicalDeviceMemoryProperties memoryProperties = default;
            vk.GetPhysicalDeviceMemoryProperties(device, &memoryProperties);
            for (uint h = 0; h < memoryProperties.MemoryHeapCount; h++)
            {
                if (memoryProperties.MemoryHeaps[(int)h].Flags.HasFlag(MemoryHeapFlags.DeviceLocalBit))
                    score += (long)(memoryProperties.MemoryHeaps[(int)h].Size / (1024UL * 1024UL * 1024UL));
            }

            PhysicalDeviceFeatures coreFeatures = default;
            vk.GetPhysicalDeviceFeatures(device, &coreFeatures);
            FormatProperties hdrStorageFormatProperties = default;
            vk.GetPhysicalDeviceFormatProperties(device, Format.R16G16B16A16Sfloat, &hdrStorageFormatProperties);
            bool supportsStorageImageReadWithoutFormat = coreFeatures.ShaderStorageImageReadWithoutFormat;
            bool supportsStorageImageWriteWithoutFormat = coreFeatures.ShaderStorageImageWriteWithoutFormat;
            bool supportsHdrStorageImage = hdrStorageFormatProperties.OptimalTilingFeatures.HasFlag(FormatFeatureFlags.StorageImageBit);

            if (rayTracingMode == RayTracingMode.Auto && rayTracing.Pipelines)
                score += 300;
            if (preferredDeviceIndex.HasValue)
                score += i == (uint)preferredDeviceIndex.Value ? 100000 : -100000;

            log.Debug($"Vulkan device #{i}: {name} ({deviceType}), score {score}");
            if (score > bestScore)
            {
                bestScore = score;
                selected = new VulkanPhysicalDevice(
                    vk, instance, device, properties, memoryProperties,
                    families.Graphics,
                    families.GraphicsSubmissionSupportsCompute,
                    families.Present,
                    families.Compute,
                    families.Transfer,
                    families.GraphicsTimestampValidBits,
                    vulkan13Features.DynamicRendering,
                    vulkan13Features.Synchronization2,
                    supportsStorageImageReadWithoutFormat,
                    supportsStorageImageWriteWithoutFormat,
                    supportsHdrStorageImage,
                    rayTracing);
            }
        }

        if (selected is null)
            throw new VulkanException(
                Result.ErrorInitializationFailed,
                "No suitable Vulkan device found. Rejected: " + string.Join("; ", rejected));
        log.Information($"Selected Vulkan device: {selected.DeviceName}");
        return selected;
    }

    private static string ReadDeviceName(PhysicalDeviceProperties* properties) =>
        VulkanHelpers.ReadFixedString(properties->DeviceName, 256);

    private static bool TryFindQueueFamilies(
        Vk vk,
        PhysicalDevice device,
        KhrSurface? khrSurface,
        SurfaceKHR? surface,
        out QueueFamilies families)
    {
        families = default;
        uint count = 0;
        vk.GetPhysicalDeviceQueueFamilyProperties(device, &count, null);
        QueueFamilyProperties[] queueFamilies = new QueueFamilyProperties[count];
        fixed (QueueFamilyProperties* pointer = queueFamilies)
            vk.GetPhysicalDeviceQueueFamilyProperties(device, &count, pointer);

        int graphics = -1;
        int present = -1;
        int compute = -1;
        int transfer = -1;

        for (uint i = 0; i < count; i++)
        {
            QueueFamilyProperties family = queueFamilies[i];
            bool supportsPresent = surface is null;
            if (surface is not null && khrSurface is not null)
            {
                khrSurface.GetPhysicalDeviceSurfaceSupport(device, i, surface.Value, out Bool32 presentSupported);
                supportsPresent = presentSupported;
            }
            if (family.QueueFlags.HasFlag(QueueFlags.GraphicsBit))
            {
                if (graphics < 0)
                    graphics = (int)i;
                if (present < 0 && supportsPresent)
                    present = (int)i;
            }
            if (compute < 0 && family.QueueFlags.HasFlag(QueueFlags.ComputeBit))
                compute = (int)i;
            if (transfer < 0 && family.QueueFlags.HasFlag(QueueFlags.TransferBit))
                transfer = (int)i;
        }

        if (graphics < 0)
            return false;
        if (present < 0 && surface is not null)
        {
            for (uint i = 0; i < count; i++)
            {
                khrSurface!.GetPhysicalDeviceSurfaceSupport(device, i, surface.Value, out Bool32 presentSupported);
                if (presentSupported)
                {
                    present = (int)i;
                    break;
                }
            }
            if (present < 0)
                return false;
        }
        if (present < 0)
            present = graphics;
        if (compute < 0)
            compute = graphics;
        if (transfer < 0)
            transfer = graphics;

        families = new QueueFamilies(
            (uint)graphics,
            queueFamilies[graphics].QueueFlags.HasFlag(QueueFlags.ComputeBit),
            (uint)present,
            (uint)compute,
            (uint)transfer,
            queueFamilies[graphics].TimestampValidBits);
        return true;
    }

    private struct QueueFamilies(
        uint graphics,
        bool graphicsSubmissionSupportsCompute,
        uint present,
        uint compute,
        uint transfer,
        uint graphicsTimestampValidBits)
    {
        public uint Graphics = graphics;
        public bool GraphicsSubmissionSupportsCompute = graphicsSubmissionSupportsCompute;
        public uint Present = present;
        public uint Compute = compute;
        public uint Transfer = transfer;
        public uint GraphicsTimestampValidBits = graphicsTimestampValidBits;
    }

    public static bool SupportsDeviceExtension(Vk vk, PhysicalDevice device, string extensionName)
    {
        uint count = 0;
        vk.EnumerateDeviceExtensionProperties(device, (byte*)null, &count, null);
        ExtensionProperties[] extensions = new ExtensionProperties[count];
        fixed (ExtensionProperties* pointer = extensions)
            vk.EnumerateDeviceExtensionProperties(device, (byte*)null, &count, pointer);
        for (uint i = 0; i < count; i++)
        {
            fixed (ExtensionProperties* extension = &extensions[i])
            {
                if (VulkanHelpers.ReadFixedString(extension->ExtensionName, 256) == extensionName)
                    return true;
            }
        }
        return false;
    }

    public IReadOnlyList<string> GetDeviceExtensions()
    {
        uint count = 0;
        _vk.EnumerateDeviceExtensionProperties(Handle, (byte*)null, &count, null);
        ExtensionProperties[] extensions = new ExtensionProperties[count];
        fixed (ExtensionProperties* pointer = extensions)
            _vk.EnumerateDeviceExtensionProperties(Handle, (byte*)null, &count, pointer);
        List<string> names = [];
        for (uint i = 0; i < count; i++)
        {
            fixed (ExtensionProperties* extension = &extensions[i])
                names.Add(VulkanHelpers.ReadFixedString(extension->ExtensionName, 256));
        }
        return names;
    }

    public bool FindMemoryType(uint typeBits, MemoryPropertyFlags requiredFlags, out uint memoryTypeIndex)
    {
        memoryTypeIndex = 0;
        for (uint i = 0; i < MemoryProperties.MemoryTypeCount; i++)
        {
            if ((typeBits & (1u << (int)i)) == 0)
                continue;
            if (MemoryProperties.MemoryTypes[(int)i].PropertyFlags.HasFlag(requiredFlags))
            {
                memoryTypeIndex = i;
                return true;
            }
        }
        return false;
    }

    public ulong GetDedicatedMemory()
    {
        ulong total = 0;
        for (uint i = 0; i < MemoryProperties.MemoryHeapCount; i++)
        {
            if (MemoryProperties.MemoryHeaps[(int)i].Flags.HasFlag(MemoryHeapFlags.DeviceLocalBit))
                total += MemoryProperties.MemoryHeaps[(int)i].Size;
        }
        return total;
    }
}
