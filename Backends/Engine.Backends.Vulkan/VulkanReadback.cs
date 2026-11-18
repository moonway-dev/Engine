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
    private Fence _fence;
    private readonly object _operationLock = new();
    private readonly VulkanQuarantine<(nint CommandBuffer, nint StagingBuffer), (CommandBuffer CommandBuffer, ReadbackStaging Staging)> _quarantined = new();
    private bool _disposed;
    private bool _unusable;

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
        lock (_operationLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_unusable)
                throw new InvalidOperationException("Vulkan readback is unusable after an unconfirmed submission failure.");
            return ReadTexturePixelsCore(texture, currentLayout, byteSizeOverride);
        }
    }

    private unsafe byte[] ReadTexturePixelsCore(IVulkanTexture texture, VkImageLayout currentLayout, ulong? byteSizeOverride)
    {
        ulong byteSize = byteSizeOverride ?? (ulong)(texture.Width * texture.Height * 4);
        Result presentIdle;
        lock (_device.QueueLock)
            presentIdle = _vk.QueueWaitIdle(_device.PresentQueue);
        VulkanHelpers.Throw(presentIdle, "vkQueueWaitIdle (readback presentation)");
        ReadbackStaging staging = CreateStaging(byteSize);
        CommandBuffer commandBuffer = default;
        bool queueAccepted = false;
        bool completionConfirmed = false;
        bool resourcesQuarantined = false;
        try
        {
            CommandBufferAllocateInfo allocateInfo = new()
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = _commandPool,
                Level = CommandBufferLevel.Primary,
                CommandBufferCount = 1,
            };
            VulkanHelpers.Throw(_vk.AllocateCommandBuffers(_device.Handle, &allocateInfo, out commandBuffer), "vkAllocateCommandBuffers (readback)");

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
            Result submitResult;
            lock (_device.QueueLock)
                submitResult = _vk.QueueSubmit(_device.GraphicsQueue, 1, &submitInfo, _fence);
            if (submitResult != Result.Success)
            {
                VulkanException submitFailure = new(submitResult, "vkQueueSubmit (readback)");
                Result idleResult = _device.WaitIdle();
                if (idleResult != Result.Success)
                {
                    Quarantine(commandBuffer, staging);
                    resourcesQuarantined = true;
                    throw new AggregateException(
                        submitFailure,
                        new VulkanException(idleResult, "vkDeviceWaitIdle after readback submit failure"));
                }
                completionConfirmed = true;
                try
                {
                    RecoverFence();
                }
                catch (Exception recoveryFailure)
                {
                    throw new AggregateException(submitFailure, recoveryFailure);
                }
                throw submitFailure;
            }
            queueAccepted = true;
            Result waitResult;
            fixed (Fence* fencePointer = &_fence)
                waitResult = _vk.WaitForFences(_device.Handle, 1, fencePointer, new Bool32(true), ulong.MaxValue);
            if (waitResult != Result.Success)
            {
                VulkanException waitFailure = new(waitResult, "vkWaitForFences (readback)");
                Result idleResult = _device.WaitIdle();
                if (idleResult != Result.Success)
                {
                    Quarantine(commandBuffer, staging);
                    resourcesQuarantined = true;
                    throw new AggregateException(waitFailure, new VulkanException(idleResult, "vkDeviceWaitIdle after readback wait failure"));
                }
                completionConfirmed = true;
                RecoverFence();
                throw waitFailure;
            }

            completionConfirmed = true;
            byte[] pixels = new byte[byteSize];
            fixed (byte* destination = pixels)
                System.Runtime.CompilerServices.Unsafe.CopyBlock(destination, staging.Mapped, (uint)byteSize);
            Result resetResult;
            fixed (Fence* fencePointer = &_fence)
                resetResult = _vk.ResetFences(_device.Handle, 1, fencePointer);
            if (resetResult != Result.Success)
            {
                RecoverFence();
                throw new VulkanException(resetResult, "vkResetFences (readback)");
            }
            return pixels;
        }
        finally
        {
            if (!resourcesQuarantined)
            {
                if (commandBuffer.Handle != 0 && queueAccepted && !completionConfirmed)
                    Quarantine(commandBuffer, staging);
                else
                {
                    if (commandBuffer.Handle != 0)
                        _vk.FreeCommandBuffers(_device.Handle, _commandPool, 1, &commandBuffer);
                    _vk.DestroyBuffer(_device.Handle, staging.Handle, null);
                    _memory.Free(staging.Allocation);
                }
            }
        }
    }

    private void Quarantine(CommandBuffer commandBuffer, ReadbackStaging staging)
    {
        _unusable = true;
        _quarantined.TryAdd((commandBuffer.Handle, (nint)staging.Handle.Handle), (commandBuffer, staging));
    }

    internal Result RetireAfterConfirmedDeviceIdle()
    {
        lock (_operationLock)
        {
            try
            {
                _quarantined.DrainAfterConfirmedIdle(deviceIdleConfirmed: true, release: resource =>
                {
                    CommandBuffer commandBuffer = resource.CommandBuffer;
                    _vk.FreeCommandBuffers(_device.Handle, _commandPool, 1, &commandBuffer);
                    _vk.DestroyBuffer(_device.Handle, resource.Staging.Handle, null);
                    _memory.Free(resource.Staging.Allocation);
                });
                if (_fence.Handle == 0 || _unusable)
                    RecoverFence();
                if (_quarantined.Count == 0 && _fence.Handle != 0)
                    _unusable = false;
                return Result.Success;
            }
            catch
            {
                _unusable = true;
                throw;
            }
        }
    }

    private void RecoverFence()
    {
        Fence oldFence = _fence;
        _fence = default;
        if (oldFence.Handle != 0)
            _vk.DestroyFence(_device.Handle, oldFence, null);
        FenceCreateInfo fenceInfo = new() { SType = StructureType.FenceCreateInfo };
        Result createResult = _vk.CreateFence(_device.Handle, &fenceInfo, null, out Fence fence);
        if (createResult != Result.Success)
        {
            _unusable = true;
            throw new VulkanException(createResult, "vkCreateFence (readback recovery)");
        }
        _fence = fence;
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
        MemoryAllocation allocation = default;
        bool allocationCreated = false;
        try
        {
            MemoryRequirements requirements = default;
            _vk.GetBufferMemoryRequirements(_device.Handle, buffer, &requirements);
            allocation = _memory.Allocate(requirements, MemoryUsage.CpuToGpu, "Readback staging");
            allocationCreated = true;
            VulkanHelpers.Throw(
                _vk.BindBufferMemory(_device.Handle, buffer, allocation.Memory, allocation.Offset),
                "vkBindBufferMemory (readback)");
            return new ReadbackStaging(buffer, allocation, size);
        }
        catch
        {
            _vk.DestroyBuffer(_device.Handle, buffer, null);
            if (allocationCreated)
                _memory.Free(allocation);
            throw;
        }
    }

    public void Dispose()
    {
        lock (_operationLock)
        {
            if (_disposed)
                return;
            if (_quarantined.Count != 0)
                throw new InvalidOperationException("Cannot dispose Vulkan readback while quarantined resources remain without confirmed device idle.");
            _disposed = true;
            _vk.DestroyFence(_device.Handle, _fence, null);
            _vk.DestroyCommandPool(_device.Handle, _commandPool, null);
        }
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
