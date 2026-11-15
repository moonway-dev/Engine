using System.Runtime.CompilerServices;
using Engine.Core;
using Engine.RHI;
using Silk.NET.Core;
using Silk.NET.Vulkan;

using VkBuffer = Silk.NET.Vulkan.Buffer;
using VkImageLayout = Silk.NET.Vulkan.ImageLayout;

namespace Engine.Backends.Vulkan;

internal unsafe sealed class VulkanStagingUploader : IDisposable
{
    public const ulong StagingBufferSize = 16UL * 1024UL * 1024UL;

    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private readonly VulkanMemoryManager _memory;
    private readonly ILogger _log;
    private readonly CommandPool _commandPool;
    private readonly CommandBuffer _commandBuffer;
    private readonly Fence _fence;
    private readonly Queue _queue;
    private StagingBuffer? _staging;
    private ulong _stagingOffset;
    private bool _recording;
    private bool _hasCommands;

    public VulkanStagingUploader(Vk vk, VulkanDevice device, VulkanMemoryManager memory, ILogger log)
    {
        _vk = vk;
        _device = device;
        _memory = memory;
        _log = log;

        CommandPoolCreateInfo poolInfo = new()
        {
            SType = StructureType.CommandPoolCreateInfo,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit | CommandPoolCreateFlags.TransientBit,
            QueueFamilyIndex = device.Physical.GraphicsFamilyIndex,
        };
        VulkanHelpers.Throw(vk.CreateCommandPool(device.Handle, &poolInfo, null, out CommandPool pool), "vkCreateCommandPool (upload)");
        _commandPool = pool;

        CommandBufferAllocateInfo allocateInfo = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = pool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1,
        };
        VulkanHelpers.Throw(vk.AllocateCommandBuffers(device.Handle, &allocateInfo, out CommandBuffer commandBuffer), "vkAllocateCommandBuffers (upload)");
        _commandBuffer = commandBuffer;

        FenceCreateInfo fenceInfo = new() { SType = StructureType.FenceCreateInfo };
        VulkanHelpers.Throw(vk.CreateFence(device.Handle, &fenceInfo, null, out Fence fence), "vkCreateFence (upload)");
        _fence = fence;

        _queue = device.GraphicsQueue;
    }

    private StagingBuffer GetStaging(ulong requiredSize)
    {
        if (_staging is not null && _staging.Value.Size >= requiredSize)
            return _staging.Value;
        _staging?.Dispose();
        _staging = new StagingBuffer(_vk, _device, _memory, Math.Max(requiredSize, StagingBufferSize));
        _stagingOffset = 0;
        return _staging.Value;
    }

    private ulong ReserveSpace(ulong size)
    {
        ulong offset = (_stagingOffset + 3UL) & ~3UL;
        if (_staging is null || offset + size > _staging.Value.Size)
        {
            Execute();
            GetStaging(size);
            offset = 0;
        }
        _stagingOffset = offset + size;
        return offset;
    }

    public void UploadBuffer(IBuffer destination, ReadOnlySpan<byte> data, ulong destinationOffset)
    {
        if (destinationOffset > destination.Size || (ulong)data.Length > destination.Size - destinationOffset)
            throw new ArgumentOutOfRangeException(nameof(data));
        while (!data.IsEmpty)
        {
            int length = Math.Min(data.Length, (int)StagingBufferSize);
            UploadBufferChunk(destination, data[..length], destinationOffset);
            destinationOffset += (ulong)length;
            data = data[length..];
        }
    }

    private void UploadBufferChunk(IBuffer destination, ReadOnlySpan<byte> data, ulong destinationOffset)
    {
        ulong stagingOffset = ReserveSpace((ulong)data.Length);
        BeginRecording();
        StagingBuffer staging = _staging!.Value;
        data.CopyTo(new Span<byte>((byte*)staging.Mapped + stagingOffset, data.Length));
        BufferCopy copy = new()
        {
            SrcOffset = stagingOffset,
            DstOffset = destinationOffset,
            Size = (ulong)data.Length,
        };
        _vk.CmdCopyBuffer(_commandBuffer, staging.Buffer, GetVkBuffer(destination), 1, &copy);
        BufferMemoryBarrier barrier = new()
        {
            SType = StructureType.BufferMemoryBarrier,
            SrcAccessMask = AccessFlags.TransferWriteBit,
            DstAccessMask = DestinationAccess(destination.Usage),
            SrcQueueFamilyIndex = uint.MaxValue,
            DstQueueFamilyIndex = uint.MaxValue,
            Buffer = GetVkBuffer(destination),
            Offset = destinationOffset,
            Size = (ulong)data.Length,
        };
        _vk.CmdPipelineBarrier(
            _commandBuffer,
            PipelineStageFlags.TransferBit,
            DestinationStages(destination.Usage),
            DependencyFlags.None,
            0, null, 1, &barrier, 0, null);
        _hasCommands = true;
    }

    private static AccessFlags DestinationAccess(BufferUsage usage)
    {
        AccessFlags access = 0;
        if (usage.HasFlag(BufferUsage.Vertex))
            access |= AccessFlags.VertexAttributeReadBit;
        if (usage.HasFlag(BufferUsage.Index))
            access |= AccessFlags.IndexReadBit;
        if (usage.HasFlag(BufferUsage.Uniform))
            access |= AccessFlags.UniformReadBit;
        if (usage.HasFlag(BufferUsage.Storage))
            access |= AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit;
        if (usage.HasFlag(BufferUsage.Indirect))
            access |= AccessFlags.IndirectCommandReadBit;
        if (usage.HasFlag(BufferUsage.TransferSource))
            access |= AccessFlags.TransferReadBit;
        return access == 0 ? AccessFlags.MemoryReadBit : access;
    }

    private static PipelineStageFlags DestinationStages(BufferUsage usage)
    {
        PipelineStageFlags stages = 0;
        if (usage.HasFlag(BufferUsage.Vertex) || usage.HasFlag(BufferUsage.Index))
            stages |= PipelineStageFlags.VertexInputBit;
        if (usage.HasFlag(BufferUsage.Uniform) || usage.HasFlag(BufferUsage.Storage))
            stages |= PipelineStageFlags.VertexShaderBit | PipelineStageFlags.FragmentShaderBit | PipelineStageFlags.ComputeShaderBit;
        if (usage.HasFlag(BufferUsage.Indirect))
            stages |= PipelineStageFlags.DrawIndirectBit;
        if (usage.HasFlag(BufferUsage.TransferSource))
            stages |= PipelineStageFlags.TransferBit;
        return stages == 0 ? PipelineStageFlags.AllCommandsBit : stages;
    }

    public void UploadTexture(ITexture destination, ReadOnlySpan<byte> data)
    {
        ulong stagingOffset = ReserveSpace((ulong)data.Length);
        BeginRecording();
        StagingBuffer staging = _staging!.Value;
        data.CopyTo(new Span<byte>((byte*)staging.Mapped + stagingOffset, data.Length));

        IVulkanTexture vkTexture = GetVkTexture(destination);
        ImageMemoryBarrier preBarrier = new()
        {
            SType = StructureType.ImageMemoryBarrier,
            SrcAccessMask = 0,
            DstAccessMask = AccessFlags.TransferWriteBit,
            OldLayout = VkImageLayout.Undefined,
            NewLayout = VkImageLayout.TransferDstOptimal,
            SrcQueueFamilyIndex = uint.MaxValue,
            DstQueueFamilyIndex = uint.MaxValue,
            Image = vkTexture.Handle,
            SubresourceRange = FullColorRange,
        };
        _vk.CmdPipelineBarrier(
            _commandBuffer,
            PipelineStageFlags.TopOfPipeBit,
            PipelineStageFlags.TransferBit,
            DependencyFlags.None,
            0, null, 0, null, 1, &preBarrier);

        BufferImageCopy copy = new()
        {
            BufferOffset = stagingOffset,
            BufferRowLength = 0,
            BufferImageHeight = 0,
            ImageSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.ColorBit,
                MipLevel = 0,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
            ImageOffset = new Offset3D(0, 0, 0),
            ImageExtent = new Extent3D(vkTexture.Width, vkTexture.Height, 1),
        };
        _vk.CmdCopyBufferToImage(
            _commandBuffer,
            staging.Buffer,
            vkTexture.Handle,
            VkImageLayout.TransferDstOptimal,
            1,
            &copy);

        ImageMemoryBarrier postBarrier = new()
        {
            SType = StructureType.ImageMemoryBarrier,
            SrcAccessMask = AccessFlags.TransferWriteBit,
            DstAccessMask = AccessFlags.ShaderReadBit,
            OldLayout = VkImageLayout.TransferDstOptimal,
            NewLayout = VkImageLayout.ShaderReadOnlyOptimal,
            SrcQueueFamilyIndex = uint.MaxValue,
            DstQueueFamilyIndex = uint.MaxValue,
            Image = vkTexture.Handle,
            SubresourceRange = FullColorRange,
        };
        _vk.CmdPipelineBarrier(
            _commandBuffer,
            PipelineStageFlags.TransferBit,
            PipelineStageFlags.FragmentShaderBit,
            DependencyFlags.None,
            0, null, 0, null, 1, &postBarrier);
        _hasCommands = true;
    }

    private static ImageSubresourceRange FullColorRange => new()
    {
        AspectMask = ImageAspectFlags.ColorBit,
        BaseMipLevel = 0,
        LevelCount = 1,
        BaseArrayLayer = 0,
        LayerCount = 1,
    };

    private void BeginRecording()
    {
        if (_recording)
            return;
        CommandBufferBeginInfo beginInfo = new()
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };
        VulkanHelpers.Throw(_vk.BeginCommandBuffer(_commandBuffer, &beginInfo), "vkBeginCommandBuffer (upload)");
        _recording = true;
    }

    public void Execute()
    {
        if (!_recording)
            return;
        _recording = false;
        if (!_hasCommands)
        {
            _stagingOffset = 0;
            return;
        }
        _hasCommands = false;
        VulkanHelpers.Throw(_vk.EndCommandBuffer(_commandBuffer), "vkEndCommandBuffer (upload)");
        CommandBuffer commandBufferCopy = _commandBuffer;
        SubmitInfo submitInfo = new()
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBufferCopy,
        };
        VulkanHelpers.Throw(_vk.QueueSubmit(_queue, 1, &submitInfo, _fence), "vkQueueSubmit (upload)");
        fixed (Fence* fencePointer = &_fence)
        {
            VulkanHelpers.Throw(
                _vk.WaitForFences(_device.Handle, 1, fencePointer, new Bool32(true), ulong.MaxValue),
                "vkWaitForFences (upload)");
            VulkanHelpers.Throw(_vk.ResetFences(_device.Handle, 1, fencePointer), "vkResetFences (upload)");
        }
        VulkanHelpers.Throw(
            _vk.ResetCommandPool(_device.Handle, _commandPool, CommandPoolResetFlags.None),
            "vkResetCommandPool (upload)");
        _stagingOffset = 0;
    }

    internal IVulkanTexture GetVkTexture(ITexture texture) => texture as IVulkanTexture
        ?? throw new ArgumentException("Texture does not belong to the Vulkan backend.", nameof(texture));

    internal VkBuffer GetVkBuffer(IBuffer buffer) => buffer is VulkanBuffer vkBuffer
        ? vkBuffer.Handle
        : throw new ArgumentException("Buffer does not belong to the Vulkan backend.", nameof(buffer));

    public void Dispose()
    {
        _staging?.Dispose();
        _vk.DestroyFence(_device.Handle, _fence, null);
        _vk.DestroyCommandPool(_device.Handle, _commandPool, null);
    }
}

internal unsafe readonly struct StagingBuffer : IDisposable
{
    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private readonly VulkanMemoryManager _memory;
    private readonly MemoryAllocation _allocation;

    public VkBuffer Buffer { get; }

    public ulong Size { get; }

    public void* Mapped { get; }

    public StagingBuffer(Vk vk, VulkanDevice device, VulkanMemoryManager memory, ulong size)
    {
        _vk = vk;
        _device = device;
        _memory = memory;
        Size = size;

        BufferCreateInfo createInfo = new()
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = BufferUsageFlags.TransferSrcBit,
            SharingMode = SharingMode.Exclusive,
        };
        VulkanHelpers.Throw(vk.CreateBuffer(device.Handle, &createInfo, null, out VkBuffer buffer), "vkCreateBuffer (staging)");
        Buffer = buffer;

        MemoryRequirements requirements = default;
        vk.GetBufferMemoryRequirements(device.Handle, buffer, &requirements);
        _allocation = memory.Allocate(requirements, MemoryUsage.CpuOnly, "Staging");
        VulkanHelpers.Throw(
            vk.BindBufferMemory(device.Handle, buffer, _allocation.Memory, _allocation.Offset),
            "vkBindBufferMemory (staging)");
        Mapped = _allocation.MappedBase;
    }

    public void Dispose()
    {
        _vk.DestroyBuffer(_device.Handle, Buffer, null);
        _memory.Free(_allocation);
    }
}
