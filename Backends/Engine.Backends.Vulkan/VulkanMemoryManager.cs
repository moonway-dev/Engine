using Engine.Core;
using Engine.RHI;
using Silk.NET.Vulkan;

namespace Engine.Backends.Vulkan;

internal unsafe sealed class VulkanMemoryManager : IMemoryAllocator
{
    public const ulong DeviceHeapSize = 256UL * 1024UL * 1024UL;
    public const ulong HostHeapSize = 128UL * 1024UL * 1024UL;
    public const ulong DedicatedAllocationThreshold = 64UL * 1024UL * 1024UL;

    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private readonly VulkanPhysicalDevice _physical;
    private readonly ILogger _log;
    private readonly List<VulkanMemoryHeap> _heaps = [];
    private readonly List<MemoryAllocation> _liveAllocations = [];
    private readonly object _lock = new();

    public VulkanMemoryManager(Vk vk, VulkanDevice device, VulkanPhysicalDevice physical, ILogger log)
    {
        _vk = vk;
        _device = device;
        _physical = physical;
        _log = log;
    }

    public ulong UsedMemory { get; private set; }

    public ulong AllocatedMemory
    {
        get
        {
            lock (_lock)
                return (ulong)_heaps.Sum(h => (decimal)h.Allocator.Capacity);
        }
    }

    public MemoryAllocation Allocate(MemoryRequirements requirements, MemoryUsage usage, string debugName)
    {
        lock (_lock)
        {
            bool hostVisible = usage is MemoryUsage.CpuToGpu or MemoryUsage.CpuOnly or MemoryUsage.GpuToCpu;
            MemoryPropertyFlags requiredFlags = VulkanHelpers.ToVulkanMemoryFlags(usage);
            if (requirements.Size >= DedicatedAllocationThreshold)
                return AllocateDedicated(requirements, requiredFlags, hostVisible, debugName);

            VulkanMemoryHeap? heap = FindHeap(hostVisible, requirements.Size, requirements.MemoryTypeBits, requiredFlags);
            if (heap is null)
            {
                heap = CreateHeap(hostVisible ? HostHeapSize : DeviceHeapSize, hostVisible, requirements.MemoryTypeBits);
                _heaps.Add(heap);
            }
            if (!heap.Allocator.TryAllocate(requirements.Size, requirements.Alignment, out ulong offset))
            {
                heap = CreateHeap(hostVisible ? HostHeapSize : DeviceHeapSize, hostVisible, requirements.MemoryTypeBits);
                _heaps.Add(heap);
                if (!heap.Allocator.TryAllocate(requirements.Size, requirements.Alignment, out offset))
                    throw new VulkanException(Result.ErrorOutOfDeviceMemory, $"Suballocation of {requirements.Size} bytes for '{debugName}'");
            }
            MemoryAllocation allocation = new(heap, offset, requirements.Size, debugName);
            _liveAllocations.Add(allocation);
            UsedMemory += requirements.Size;
            return allocation;
        }
    }

    private MemoryAllocation AllocateDedicated(MemoryRequirements requirements, MemoryPropertyFlags requiredFlags, bool hostVisible, string debugName)
    {
        if (!_physical.FindMemoryType(requirements.MemoryTypeBits, requiredFlags, out uint memoryTypeIndex))
            throw new VulkanException(Result.ErrorOutOfDeviceMemory, $"No memory type for dedicated allocation '{debugName}'");
        MemoryAllocateInfo allocateInfo = new()
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = requirements.Size,
            MemoryTypeIndex = memoryTypeIndex,
        };
        VulkanHelpers.Throw(
            _vk.AllocateMemory(_device.Handle, &allocateInfo, null, out DeviceMemory memory),
            $"vkAllocateMemory (dedicated '{debugName}')");
        void* mappedBase = null;
        if (hostVisible)
            VulkanHelpers.Throw(_vk.MapMemory(_device.Handle, memory, 0, ulong.MaxValue, 0, &mappedBase), $"vkMapMemory '{debugName}'");
        MemoryAllocation allocation = new MemoryAllocation(memory, requirements.Size, mappedBase, debugName);
        lock (_lock)
        {
            _liveAllocations.Add(allocation);
            UsedMemory += requirements.Size;
        }
        return allocation;
    }

    private VulkanMemoryHeap? FindHeap(bool hostVisible, ulong size, uint memoryTypeBits, MemoryPropertyFlags requiredFlags)
    {
        foreach (VulkanMemoryHeap heap in _heaps)
        {
            if (heap.HostVisible != hostVisible)
                continue;
            if ((memoryTypeBits & (1u << (int)heap.MemoryTypeIndex)) == 0)
                continue;
            if (heap.Allocator.LargestFreeRange() < size)
                continue;
            return heap;
        }
        return null;
    }

    private VulkanMemoryHeap CreateHeap(ulong capacity, bool hostVisible, uint memoryTypeBits)
    {
        MemoryPropertyFlags flags = hostVisible
            ? MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit
            : MemoryPropertyFlags.DeviceLocalBit;
        if (!_physical.FindMemoryType(memoryTypeBits, flags, out uint memoryTypeIndex))
            throw new VulkanException(Result.ErrorOutOfDeviceMemory, "No memory type for heap allocation");

        MemoryAllocateInfo allocateInfo = new()
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = capacity,
            MemoryTypeIndex = memoryTypeIndex,
        };
        VulkanHelpers.Throw(
            _vk.AllocateMemory(_device.Handle, &allocateInfo, null, out DeviceMemory memory),
            $"vkAllocateMemory ({(hostVisible ? "host" : "device")} heap {capacity / (1024 * 1024)} MB)");

        void* mappedBase = null;
        if (hostVisible)
            VulkanHelpers.Throw(_vk.MapMemory(_device.Handle, memory, 0, ulong.MaxValue, 0, &mappedBase), "vkMapMemory (heap)");

        _log.Debug($"GPU memory heap created: {capacity / (1024 * 1024)} MB {(hostVisible ? "host-visible" : "device-local")}");
        return new VulkanMemoryHeap(_vk, _device, memory, capacity, hostVisible, mappedBase, memoryTypeIndex);
    }

    public void Free(MemoryAllocation allocation)
    {
        lock (_lock)
        {
            _liveAllocations.Remove(allocation);
            UsedMemory -= allocation.Size;
            if (allocation.Heap is not null)
                allocation.Heap.Allocator.Free(allocation.Offset, allocation.Size);
            else
            {
                if (allocation.MappedBase is not null)
                    _vk.UnmapMemory(_device.Handle, allocation.Memory);
                _vk.FreeMemory(_device.Handle, allocation.Memory, null);
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_liveAllocations.Count > 0)
            {
                _log.Warning($"GPU memory leak detected: {_liveAllocations.Count} live allocation(s)");
                foreach (MemoryAllocation allocation in _liveAllocations)
                    _log.Warning($"  leaked: '{allocation.DebugName}' ({allocation.Size} bytes)");
            }
            foreach (VulkanMemoryHeap heap in _heaps)
                heap.Dispose();
            _heaps.Clear();
            _liveAllocations.Clear();
            UsedMemory = 0;
        }
    }
}

internal unsafe readonly struct MemoryAllocation
{
    public VulkanMemoryHeap? Heap { get; }
    public DeviceMemory Memory { get; }
    public ulong Offset { get; }
    public ulong Size { get; }
    public void* MappedBase { get; }
    public string DebugName { get; }

    public bool IsHostVisible => MappedBase is not null;

    public MemoryAllocation(VulkanMemoryHeap heap, ulong offset, ulong size, string debugName)
    {
        Heap = heap;
        Memory = heap.Memory;
        Offset = offset;
        Size = size;
        MappedBase = heap.MappedBase is not null
            ? (void*)((nint)heap.MappedBase + (nint)offset)
            : null;
        DebugName = debugName;
    }

    public MemoryAllocation(DeviceMemory memory, ulong size, void* mappedBase, string debugName)
    {
        Heap = null;
        Memory = memory;
        Offset = 0;
        Size = size;
        MappedBase = mappedBase;
        DebugName = debugName;
    }


}

internal unsafe sealed class VulkanMemoryHeap : IDisposable
{
    private readonly Vk _vk;
    private readonly VulkanDevice _device;

    public DeviceMemory Memory { get; }

    public ulong Capacity { get; }

    public bool HostVisible { get; }

    public uint MemoryTypeIndex { get; }

    public void* MappedBase { get; }

    public MemoryBlockAllocator Allocator { get; }

    public VulkanMemoryHeap(Vk vk, VulkanDevice device, DeviceMemory memory, ulong capacity, bool hostVisible, void* mappedBase, uint memoryTypeIndex)
    {
        _vk = vk;
        _device = device;
        Memory = memory;
        Capacity = capacity;
        HostVisible = hostVisible;
        MemoryTypeIndex = memoryTypeIndex;
        MappedBase = mappedBase;
        Allocator = new MemoryBlockAllocator(0, capacity);
    }

    public void Dispose()
    {
        if (MappedBase is not null)
            _vk.UnmapMemory(_device.Handle, Memory);
        _vk.FreeMemory(_device.Handle, Memory, null);
    }
}
