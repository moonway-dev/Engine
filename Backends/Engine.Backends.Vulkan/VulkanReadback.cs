using Engine.Core;
using Engine.RHI;
using Silk.NET.Core;
using Silk.NET.Vulkan;
using VkBuffer = Silk.NET.Vulkan.Buffer;
using VkImageLayout = Silk.NET.Vulkan.ImageLayout;

namespace Engine.Backends.Vulkan;

internal unsafe sealed class VulkanReadback : IDisposable
{
    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private readonly VulkanMemoryManager _memory;
    private readonly CommandPool _commandPool;
    private readonly Fence _fence;
    private bool _disposed;

    public unsafe VulkanReadback(Vk vk, VulkanDevice device, VulkanMemoryManager memory)
    {
        _vk = vk;
        _device = device;
        _memory = memory;
        CommandPoolCreateInfo poolInfo = new()
        {
            SType = StructureType.CommandPoolCreateInfo,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit | CommandPoolCreateFlags.TransientBit,
            QueueFamilyIndex = device.Physical.GraphicsFamilyIndex,
        };
        VulkanHelpers.Throw(vk.CreateCommandPool(device.Handle, &poolInfo, null, out CommandPool pool), "vkCreateCommandPool (readback)");
        _commandPool = pool;
        FenceCreateInfo fenceInfo = new() { SType = StructureType.FenceCreateInfo };
        VulkanHelpers.Throw(vk.CreateFence(device.Handle, &fenceInfo, null, out Fence fence), "vkCreateFence (readback)");
        _fence = fence;
    }

    public unsafe byte[] ReadTexturePixels(IVulkanTexture texture, VkImageLayout currentLayout, ulong? byteSizeOverride = null)
    {
        _vk.QueueWaitIdle(_device.PresentQueue);
        ulong byteSize = byteSizeOverride ?? (ulong)(texture.Width * texture.Height * 4);
        ReadbackStaging staging = CreateStaging(byteSize);
        try
        {
            CommandBufferAllocateInfo allocateInfo = new()
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = _commandPool,
                Level = CommandBufferLevel.Primary,
                CommandBufferCount = 1,
            };
            VulkanHelpers.Throw(_vk.AllocateCommandBuffers(_device.Handle, &allocateInfo, out CommandBuffer commandBuffer), "vkAllocateCommandBuffers (readback)");

            ImageMemoryBarrier barrier = new()
            {
                SType = StructureType.ImageMemoryBarrier,
                SrcAccessMask = 0,
                DstAccessMask = AccessFlags.TransferReadBit,
                OldLayout = currentLayout,
                NewLayout = VkImageLayout.TransferSrcOptimal,
                SrcQueueFamilyIndex = uint.MaxValue,
                DstQueueFamilyIndex = uint.MaxValue,
                Image = texture.Handle,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = VulkanTexture.AspectMaskFor(texture.Format),
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1,
                },
            };
            ImageAspectFlags copyAspect = VulkanTexture.AspectMaskFor(texture.Format);
            BufferImageCopy copy = new()
            {
                BufferOffset = 0,
                BufferRowLength = 0,
                BufferImageHeight = 0,
                ImageSubresource = new ImageSubresourceLayers
                {
                    AspectMask = copyAspect,
                    MipLevel = 0,
                    BaseArrayLayer = 0,
                    LayerCount = 1,
                },
                ImageOffset = new Offset3D(0, 0, 0),
                ImageExtent = new Extent3D(texture.Width, texture.Height, 1),
            };

            CommandBufferBeginInfo beginInfo = new()
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
            };
            VulkanHelpers.Throw(_vk.BeginCommandBuffer(commandBuffer, &beginInfo), "vkBeginCommandBuffer (readback)");
            _vk.CmdPipelineBarrier(
                commandBuffer,
                PipelineStageFlags.AllCommandsBit,
                PipelineStageFlags.TransferBit,
                DependencyFlags.None,
                0, null, 0, null, 1, &barrier);
            _vk.CmdCopyImageToBuffer(commandBuffer, texture.Handle, VkImageLayout.TransferSrcOptimal, staging.Handle, 1, &copy);

            ImageMemoryBarrier restoreBarrier = barrier with
            {
                SrcAccessMask = AccessFlags.TransferReadBit,
                DstAccessMask = 0,
                OldLayout = VkImageLayout.TransferSrcOptimal,
                NewLayout = currentLayout,
            };
            _vk.CmdPipelineBarrier(
                commandBuffer,
                PipelineStageFlags.TransferBit,
                PipelineStageFlags.AllCommandsBit,
                DependencyFlags.None,
                0, null, 0, null, 1, &restoreBarrier);
            VulkanHelpers.Throw(_vk.EndCommandBuffer(commandBuffer), "vkEndCommandBuffer (readback)");

            CommandBuffer handle = commandBuffer;
            SubmitInfo submitInfo = new()
            {
                SType = StructureType.SubmitInfo,
                CommandBufferCount = 1,
                PCommandBuffers = &handle,
            };
            VulkanHelpers.Throw(_vk.QueueSubmit(_device.GraphicsQueue, 1, &submitInfo, _fence), "vkQueueSubmit (readback)");
            fixed (Fence* fencePointer = &_fence)
            {
                _vk.WaitForFences(_device.Handle, 1, fencePointer, new Bool32(true), ulong.MaxValue);
                _vk.ResetFences(_device.Handle, 1, fencePointer);
            }

            byte[] pixels = new byte[byteSize];
            fixed (byte* destination = pixels)
                System.Runtime.CompilerServices.Unsafe.CopyBlock(destination, staging.Mapped, (uint)byteSize);
            return pixels;
        }
        finally
        {
            _vk.DestroyBuffer(_device.Handle, staging.Handle, null);
            _memory.Free(staging.Allocation);
        }
    }

    private unsafe ReadbackStaging CreateStaging(ulong size)
    {
        BufferCreateInfo createInfo = new()
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = BufferUsageFlags.TransferDstBit,
            SharingMode = SharingMode.Exclusive,
        };
        VulkanHelpers.Throw(_vk.CreateBuffer(_device.Handle, &createInfo, null, out VkBuffer buffer), "vkCreateBuffer (readback)");
        MemoryRequirements requirements = default;
        _vk.GetBufferMemoryRequirements(_device.Handle, buffer, &requirements);
        MemoryAllocation allocation = _memory.Allocate(requirements, MemoryUsage.CpuToGpu, "Readback staging");
        VulkanHelpers.Throw(
            _vk.BindBufferMemory(_device.Handle, buffer, allocation.Memory, allocation.Offset),
            "vkBindBufferMemory (readback)");
        return new ReadbackStaging(buffer, allocation, size);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _vk.DestroyFence(_device.Handle, _fence, null);
        _vk.DestroyCommandPool(_device.Handle, _commandPool, null);
    }
}

internal unsafe readonly struct ReadbackStaging
{
    public VkBuffer Handle { get; }
    public MemoryAllocation Allocation { get; }
    public ulong Size { get; }
    public void* Mapped => Allocation.MappedBase;

    public ReadbackStaging(VkBuffer handle, MemoryAllocation allocation, ulong size)
    {
        Handle = handle;
        Allocation = allocation;
        Size = size;
    }
}
