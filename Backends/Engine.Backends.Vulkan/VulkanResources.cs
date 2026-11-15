using System.Runtime.CompilerServices;
using Engine.Mathematics;
using Engine.RHI;
using Silk.NET.Vulkan;

using VkBuffer = Silk.NET.Vulkan.Buffer;
using VkViewport = Silk.NET.Vulkan.Viewport;
using VkIndexType = Silk.NET.Vulkan.IndexType;
using VkImageLayout = Silk.NET.Vulkan.ImageLayout;
using VkAttachmentLoadOp = Silk.NET.Vulkan.AttachmentLoadOp;
using VkAttachmentStoreOp = Silk.NET.Vulkan.AttachmentStoreOp;
using VkDescriptorSet = Silk.NET.Vulkan.DescriptorSet;
using VkRect2D = Silk.NET.Vulkan.Rect2D;

namespace Engine.Backends.Vulkan;

using VkViewport = Silk.NET.Vulkan.Viewport;

internal interface IVulkanTexture
{
    Image Handle { get; }

    ImageView View { get; }

    uint Width { get; }

    uint Height { get; }

    TextureFormat Format { get; }
}

internal unsafe sealed class VulkanBuffer : IBuffer
{
    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private readonly VulkanMemoryManager _memory;
    private readonly MemoryAllocation _allocation;
    private bool _disposed;

    public VkBuffer Handle { get; }

    public ulong Size { get; }

    public BufferUsage Usage { get; }

    public MemoryUsage Memory { get; }

    public VulkanBuffer(Vk vk, VulkanDevice device, VulkanMemoryManager memory, in BufferDescription description, string debugName)
    {
        _vk = vk;
        _device = device;
        _memory = memory;
        Size = description.Size;
        Usage = description.Usage;
        Memory = description.Memory;

        BufferCreateInfo createInfo = new()
        {
            SType = StructureType.BufferCreateInfo,
            Size = description.Size,
            Usage = VulkanHelpers.ToVulkanBufferUsage(description.Usage),
            SharingMode = SharingMode.Exclusive,
        };
        VulkanHelpers.Throw(
            vk.CreateBuffer(device.Handle, &createInfo, null, out VkBuffer buffer),
            "vkCreateBuffer");
        Handle = buffer;

        MemoryRequirements requirements = default;
        vk.GetBufferMemoryRequirements(device.Handle, buffer, &requirements);
        _allocation = memory.Allocate(requirements, description.Memory, debugName);
        VulkanHelpers.Throw(
            vk.BindBufferMemory(device.Handle, buffer, _allocation.Memory, _allocation.Offset),
            "vkBindBufferMemory");
    }

    public IntPtr Map()
    {
        if (Memory is MemoryUsage.GpuOnly)
            throw new InvalidOperationException("GpuOnly buffers cannot be mapped.");
        return (IntPtr)_allocation.MappedBase;
    }

    public void Unmap()
    {
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _vk.DestroyBuffer(_device.Handle, Handle, null);
        _memory.Free(_allocation);
    }
}

internal unsafe sealed class VulkanTexture : ITexture, IVulkanTexture
{
    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private readonly VulkanMemoryManager _memory;
    private readonly MemoryAllocation _allocation;
    private bool _disposed;

    public Image Handle { get; }

    public ImageView View { get; }

    public TextureFormat Format { get; }

    public uint Width { get; }

    public uint Height { get; }

    public uint MipLevels { get; }

    public uint ArrayLayers { get; }

    public VulkanTexture(Vk vk, VulkanDevice device, VulkanMemoryManager memory, in TextureDescription description, string debugName)
    {
        _vk = vk;
        _device = device;
        _memory = memory;
        Format = description.Format;
        Width = (uint)description.Extent.Width;
        Height = (uint)description.Extent.Height;
        MipLevels = description.MipLevels == 0 ? 1 : description.MipLevels;
        ArrayLayers = description.ArrayLayers == 0 ? 1 : description.ArrayLayers;

        ImageCreateInfo createInfo = new()
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Extent = new Extent3D(Width, Height, 1),
            MipLevels = MipLevels,
            ArrayLayers = ArrayLayers,
            Format = VulkanHelpers.ToVulkanFormat(description.Format),
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = VulkanHelpers.ToVulkanTextureUsage(description.Usage, description.Format),
            SharingMode = SharingMode.Exclusive,
            InitialLayout = VkImageLayout.Undefined,
        };
        VulkanHelpers.Throw(
            vk.CreateImage(device.Handle, &createInfo, null, out Image image),
            "vkCreateImage");
        Handle = image;

        MemoryRequirements requirements = default;
        vk.GetImageMemoryRequirements(device.Handle, image, &requirements);
        _allocation = memory.Allocate(requirements, MemoryUsage.GpuOnly, debugName);
        VulkanHelpers.Throw(
            vk.BindImageMemory(device.Handle, image, _allocation.Memory, _allocation.Offset),
            "vkBindImageMemory");

        ImageViewCreateInfo viewInfo = new()
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type2D,
            Format = VulkanHelpers.ToVulkanFormat(description.Format),
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = AspectMaskFor(description.Format),
                BaseMipLevel = 0,
                LevelCount = MipLevels,
                BaseArrayLayer = 0,
                LayerCount = ArrayLayers,
            },
        };
        VulkanHelpers.Throw(
            vk.CreateImageView(device.Handle, &viewInfo, null, out ImageView view),
            "vkCreateImageView");
        View = view;
    }

    internal static ImageAspectFlags AspectMaskFor(TextureFormat format) => format switch
    {
        TextureFormat.D16Unorm or TextureFormat.D32Float => ImageAspectFlags.DepthBit,
        TextureFormat.D24UnormS8Uint or TextureFormat.D32FloatS8Uint => ImageAspectFlags.DepthBit | ImageAspectFlags.StencilBit,
        _ => ImageAspectFlags.ColorBit,
    };

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _vk.DestroyImageView(_device.Handle, View, null);
        _vk.DestroyImage(_device.Handle, Handle, null);
        _memory.Free(_allocation);
    }
}

internal unsafe sealed class VulkanSwapchainImage : ITexture, IVulkanTexture
{
    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private bool _disposed;

    public Image Handle { get; }

    public ImageView View { get; }

    public TextureFormat Format { get; }

    public uint Width { get; }

    public uint Height { get; }

    public uint MipLevels => 1;

    public uint ArrayLayers => 1;

    public VulkanSwapchainImage(Vk vk, VulkanDevice device, Image image, ImageView view, TextureFormat format, uint width, uint height)
    {
        _vk = vk;
        _device = device;
        Handle = image;
        View = view;
        Format = format;
        Width = width;
        Height = height;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _vk.DestroyImageView(_device.Handle, View, null);
    }
}

internal unsafe sealed class VulkanCommandBuffer : ICommandBuffer
{
    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private readonly CommandBuffer _handle;
    private PipelineLayout _boundLayout;
    private PipelineBindPoint _boundBindPoint = PipelineBindPoint.Graphics;
    private bool _recording;
    private bool _renderPassActive;

    public VulkanCommandBuffer(Vk vk, VulkanDevice device, CommandBuffer handle)
    {
        _vk = vk;
        _device = device;
        _handle = handle;
    }

    internal CommandBuffer Handle => _handle;

    internal bool HasRecorded { get; private set; }

    internal bool IsRecording => _recording;

    internal void ResetState()
    {
        _recording = false;
        _renderPassActive = false;
        HasRecorded = false;
    }

    internal void MarkRecorded() => HasRecorded = true;

    public void Begin()
    {
        CommandBufferBeginInfo beginInfo = new()
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };
        VulkanHelpers.Throw(_vk.BeginCommandBuffer(_handle, &beginInfo), "vkBeginCommandBuffer");
        _recording = true;
    }

    public void End()
    {
        if (!_recording)
            throw new InvalidOperationException("Command buffer is not recording.");
        if (_renderPassActive)
            throw new InvalidOperationException("Render pass is still active.");
        VulkanHelpers.Throw(_vk.EndCommandBuffer(_handle), "vkEndCommandBuffer");
        _recording = false;
    }

    public void BeginRendering(in RenderPassInfo info)
    {
        if (!_recording)
            throw new InvalidOperationException("Command buffer is not recording.");
        if (_renderPassActive)
            throw new InvalidOperationException("Render pass is already active.");

        uint colorCount = (uint)info.ColorAttachments.Length;
        RenderingAttachmentInfo* colorAttachments = stackalloc RenderingAttachmentInfo[Math.Max(1, (int)colorCount)];
        ClearValue* clearValues = stackalloc ClearValue[Math.Max(1, (int)colorCount)];
        for (int i = 0; i < info.ColorAttachments.Length; i++)
        {
            ColorAttachmentInfo attachmentInfo = info.ColorAttachments[i];
            IVulkanTexture texture = ResolveTexture(attachmentInfo.Texture);
            clearValues[i].Color.Float32_0 = attachmentInfo.ClearColor.X;
            clearValues[i].Color.Float32_1 = attachmentInfo.ClearColor.Y;
            clearValues[i].Color.Float32_2 = attachmentInfo.ClearColor.Z;
            clearValues[i].Color.Float32_3 = attachmentInfo.ClearColor.W;
            colorAttachments[i] = new RenderingAttachmentInfo
            {
                SType = StructureType.RenderingAttachmentInfo,
                ImageView = texture.View,
                ImageLayout = VkImageLayout.ColorAttachmentOptimal,
                LoadOp = (VkAttachmentLoadOp)attachmentInfo.LoadOp,
                StoreOp = (VkAttachmentStoreOp)attachmentInfo.StoreOp,
                ClearValue = clearValues[i],
            };
        }

        RenderingAttachmentInfo depthAttachment = default;
        RenderingAttachmentInfo* depthPointer = null;
        if (info.DepthAttachment is DepthAttachmentInfo depthInfo)
        {
            IVulkanTexture depthTexture = ResolveTexture(depthInfo.Texture);
            depthAttachment = new RenderingAttachmentInfo
            {
                SType = StructureType.RenderingAttachmentInfo,
                ImageView = depthTexture.View,
                ImageLayout = VkImageLayout.DepthAttachmentOptimal,
                LoadOp = (VkAttachmentLoadOp)depthInfo.LoadOp,
                StoreOp = (VkAttachmentStoreOp)depthInfo.StoreOp,
                ClearValue = new ClearValue { DepthStencil = new ClearDepthStencilValue(depthInfo.ClearDepth, 0) },
            };
            depthPointer = &depthAttachment;
        }

        RenderingInfo rendering = new()
        {
            SType = StructureType.RenderingInfo,
            RenderArea = new Rect2D(
                new Offset2D((int)info.RenderArea.X, (int)info.RenderArea.Y),
                new Extent2D((uint)info.RenderArea.Width, (uint)info.RenderArea.Height)),
            LayerCount = 1,
            ColorAttachmentCount = colorCount,
            PColorAttachments = colorAttachments,
            PDepthAttachment = depthPointer,
        };
        _vk.CmdBeginRendering(_handle, &rendering);
        _renderPassActive = true;
        HasRecorded = true;
    }

    public void EndRendering()
    {
        if (!_renderPassActive)
            throw new InvalidOperationException("No active render pass.");
        _vk.CmdEndRendering(_handle);
        _renderPassActive = false;
    }

    public void SetViewport(in Engine.RHI.Viewport viewport)
    {
        VkViewport vkViewport = new(viewport.X, viewport.Y, viewport.Width, viewport.Height, viewport.MinDepth, viewport.MaxDepth);
        _vk.CmdSetViewport(_handle, 0, 1, &vkViewport);
    }

    public void SetScissor(in Rect scissor)
    {
        Rect2D rect2D = new(new Offset2D((int)scissor.X, (int)scissor.Y), new Extent2D((uint)scissor.Width, (uint)scissor.Height));
        _vk.CmdSetScissor(_handle, 0, 1, &rect2D);
    }

    public void BindGraphicsPipeline(IGraphicsPipeline pipeline)
    {
        if (pipeline is not VulkanGraphicsPipeline vkPipeline)
            throw new ArgumentException("Pipeline does not belong to the Vulkan backend.", nameof(pipeline));
        _vk.CmdBindPipeline(_handle, PipelineBindPoint.Graphics, vkPipeline.Handle);
        _boundLayout = vkPipeline.Program.Layout;
        _boundBindPoint = PipelineBindPoint.Graphics;
    }

    public void BindComputePipeline(IComputePipeline pipeline)
    {
        if (pipeline is not VulkanComputePipeline vkPipeline)
            throw new ArgumentException("Pipeline does not belong to the Vulkan backend.", nameof(pipeline));
        _vk.CmdBindPipeline(_handle, PipelineBindPoint.Compute, vkPipeline.Handle);
        _boundLayout = vkPipeline.Program.Layout;
        _boundBindPoint = PipelineBindPoint.Compute;
    }

    public void BindDescriptorSet(IDescriptorSet set, uint setIndex, ReadOnlySpan<uint> dynamicOffsets = default)
    {
        if (set is not VulkanDescriptorSet vkSet)
            throw new ArgumentException("Descriptor set does not belong to the Vulkan backend.", nameof(set));
        VkDescriptorSet descriptorSet = vkSet.Handle;
        uint* offsets = null;
        if (dynamicOffsets.Length > 0)
        {
            uint* offsetCopy = stackalloc uint[dynamicOffsets.Length];
            for (int i = 0; i < dynamicOffsets.Length; i++)
                offsetCopy[i] = dynamicOffsets[i];
            offsets = offsetCopy;
        }
        _vk.CmdBindDescriptorSets(
            _handle,
            _boundBindPoint,
            vkSet.ProgramLayout,
            setIndex,
            1,
            &descriptorSet,
            (uint)dynamicOffsets.Length,
            offsets);
    }

    public void BindVertexBuffer(uint slot, IBuffer buffer, ulong offset = 0)
    {
        VkBuffer vkBuffer = ResolveBuffer(buffer);
        ulong offsetCopy = offset;
        _vk.CmdBindVertexBuffers(_handle, slot, 1, &vkBuffer, &offsetCopy);
    }

    public void BindIndexBuffer(IBuffer buffer, Engine.RHI.IndexType indexType)
    {
        VkBuffer vkBuffer = ResolveBuffer(buffer);
        VkIndexType vkIndexType = indexType == Engine.RHI.IndexType.UInt16 ? VkIndexType.Uint16 : VkIndexType.Uint32;
        _vk.CmdBindIndexBuffer(_handle, vkBuffer, 0, vkIndexType);
    }

    public void PushConstants(ReadOnlySpan<byte> data, ShaderStages stages, uint offset = 0)
    {
        fixed (byte* pointer = data)
            _vk.CmdPushConstants(
                _handle,
                _boundLayout,
                VulkanHelpers.ToVulkanStageFlags(stages),
                offset,
                (uint)data.Length,
                pointer);
    }

    public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0) =>
        _vk.CmdDraw(_handle, vertexCount, instanceCount, firstVertex, firstInstance);

    public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int vertexOffset = 0, uint firstInstance = 0) =>
        _vk.CmdDrawIndexed(_handle, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);

    public void Dispatch(uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1)
    {
        _vk.CmdDispatch(_handle, groupCountX, groupCountY, groupCountZ);
        HasRecorded = true;
    }

    public void CopyBufferToBuffer(IBuffer source, IBuffer destination, ulong size, ulong sourceOffset = 0, ulong destinationOffset = 0)
    {
        BufferCopy copy = new()
        {
            SrcOffset = sourceOffset,
            DstOffset = destinationOffset,
            Size = size,
        };
        _vk.CmdCopyBuffer(_handle, ResolveBuffer(source), ResolveBuffer(destination), 1, &copy);
        HasRecorded = true;
    }

    public void CopyBufferToImage(IBuffer source, ITexture destination, ulong sourceOffset = 0)
    {
        IVulkanTexture texture = ResolveTexture(destination);
        BufferImageCopy copy = new()
        {
            BufferOffset = sourceOffset,
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
            ImageExtent = new Extent3D(texture.Width, texture.Height, 1),
        };
        _vk.CmdCopyBufferToImage(_handle, ResolveBuffer(source), texture.Handle, VkImageLayout.TransferDstOptimal, 1, &copy);
        HasRecorded = true;
    }

    public void ImageBarrier(ITexture texture, Engine.RHI.ImageLayout oldLayout, Engine.RHI.ImageLayout newLayout)
    {
        VkImageLayout oldVkLayout = VulkanHelpers.ToVulkanLayout(oldLayout);
        VkImageLayout newVkLayout = VulkanHelpers.ToVulkanLayout(newLayout);
        (AccessFlags _, AccessFlags destinationAccess) = VulkanHelpers.LayoutAccess(newVkLayout);
        (AccessFlags sourceAccess, AccessFlags _) = VulkanHelpers.LayoutAccess(oldVkLayout);
        ImageBarrier(
            texture,
            oldLayout,
            newLayout,
            PipelineStages.AllCommands,
            FromVulkanAccess(sourceAccess),
            PipelineStages.AllCommands,
            FromVulkanAccess(destinationAccess));
    }

    public void ImageBarrier(
        ITexture texture,
        Engine.RHI.ImageLayout oldLayout,
        Engine.RHI.ImageLayout newLayout,
        PipelineStages sourceStages,
        ResourceAccess sourceAccess,
        PipelineStages destinationStages,
        ResourceAccess destinationAccess,
        TextureSubresourceRange subresources = default)
    {
        IVulkanTexture vkTexture = ResolveTexture(texture);
        uint mipLevelCount = subresources.MipLevelCount is 0 or uint.MaxValue ? texture.MipLevels - subresources.BaseMipLevel : subresources.MipLevelCount;
        uint arrayLayerCount = subresources.ArrayLayerCount is 0 or uint.MaxValue ? texture.ArrayLayers - subresources.BaseArrayLayer : subresources.ArrayLayerCount;
        ImageMemoryBarrier barrier = new()
        {
            SType = StructureType.ImageMemoryBarrier,
            SrcAccessMask = VulkanHelpers.ToVulkanAccess(sourceAccess),
            DstAccessMask = VulkanHelpers.ToVulkanAccess(destinationAccess),
            OldLayout = VulkanHelpers.ToVulkanLayout(oldLayout),
            NewLayout = VulkanHelpers.ToVulkanLayout(newLayout),
            SrcQueueFamilyIndex = uint.MaxValue,
            DstQueueFamilyIndex = uint.MaxValue,
            Image = vkTexture.Handle,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = VulkanTexture.AspectMaskFor(texture.Format),
                BaseMipLevel = subresources.BaseMipLevel,
                LevelCount = mipLevelCount,
                BaseArrayLayer = subresources.BaseArrayLayer,
                LayerCount = arrayLayerCount,
            },
        };
        _vk.CmdPipelineBarrier(
            _handle,
            VulkanHelpers.ToVulkanPipelineStages(sourceStages),
            VulkanHelpers.ToVulkanPipelineStages(destinationStages),
            DependencyFlags.None,
            0, null, 0, null, 1, &barrier);
        HasRecorded = true;
    }

    private static ResourceAccess FromVulkanAccess(AccessFlags access)
    {
        ResourceAccess result = ResourceAccess.None;
        if (access.HasFlag(AccessFlags.IndirectCommandReadBit)) result |= ResourceAccess.IndirectRead;
        if (access.HasFlag(AccessFlags.IndexReadBit)) result |= ResourceAccess.IndexRead;
        if (access.HasFlag(AccessFlags.VertexAttributeReadBit)) result |= ResourceAccess.VertexRead;
        if (access.HasFlag(AccessFlags.UniformReadBit)) result |= ResourceAccess.UniformRead;
        if (access.HasFlag(AccessFlags.ShaderReadBit)) result |= ResourceAccess.ShaderRead;
        if (access.HasFlag(AccessFlags.ShaderWriteBit)) result |= ResourceAccess.ShaderWrite;
        if (access.HasFlag(AccessFlags.ColorAttachmentReadBit)) result |= ResourceAccess.ColorRead;
        if (access.HasFlag(AccessFlags.ColorAttachmentWriteBit)) result |= ResourceAccess.ColorWrite;
        if (access.HasFlag(AccessFlags.DepthStencilAttachmentReadBit)) result |= ResourceAccess.DepthRead;
        if (access.HasFlag(AccessFlags.DepthStencilAttachmentWriteBit)) result |= ResourceAccess.DepthWrite;
        if (access.HasFlag(AccessFlags.TransferReadBit)) result |= ResourceAccess.TransferRead;
        if (access.HasFlag(AccessFlags.TransferWriteBit)) result |= ResourceAccess.TransferWrite;
        return result;
    }

    public void BufferBarrier(IBuffer buffer, ulong size, ulong offset = 0) =>
        BufferBarrier(
            buffer,
            size,
            PipelineStages.Transfer,
            ResourceAccess.TransferWrite,
            PipelineStages.VertexInput | PipelineStages.VertexShader | PipelineStages.FragmentShader | PipelineStages.ComputeShader,
            ResourceAccess.VertexRead | ResourceAccess.IndexRead | ResourceAccess.UniformRead | ResourceAccess.ShaderRead,
            offset);

    public void BufferBarrier(
        IBuffer buffer,
        ulong size,
        PipelineStages sourceStages,
        ResourceAccess sourceAccess,
        PipelineStages destinationStages,
        ResourceAccess destinationAccess,
        ulong offset = 0)
    {
        BufferMemoryBarrier barrier = new()
        {
            SType = StructureType.BufferMemoryBarrier,
            SrcAccessMask = VulkanHelpers.ToVulkanAccess(sourceAccess),
            DstAccessMask = VulkanHelpers.ToVulkanAccess(destinationAccess),
            SrcQueueFamilyIndex = uint.MaxValue,
            DstQueueFamilyIndex = uint.MaxValue,
            Buffer = ResolveBuffer(buffer),
            Offset = offset,
            Size = size,
        };
        _vk.CmdPipelineBarrier(
            _handle,
            VulkanHelpers.ToVulkanPipelineStages(sourceStages),
            VulkanHelpers.ToVulkanPipelineStages(destinationStages),
            DependencyFlags.None,
            0, null,
            1, &barrier,
            0, null);
        HasRecorded = true;
    }

    public unsafe void CopyImage(ITexture source, Engine.RHI.ImageLayout srcLayout, ITexture destination, Engine.RHI.ImageLayout dstLayout)
    {
        IVulkanTexture srcTexture = ResolveTexture(source);
        IVulkanTexture dstTexture = ResolveTexture(destination);
        ImageCopy copy = new()
        {
            SrcSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.ColorBit,
                MipLevel = 0,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
            SrcOffset = new Offset3D(0, 0, 0),
            DstSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.ColorBit,
                MipLevel = 0,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
            DstOffset = new Offset3D(0, 0, 0),
            Extent = new Extent3D(srcTexture.Width, srcTexture.Height, 1),
        };
        _vk.CmdCopyImage(
            _handle,
            srcTexture.Handle,
            VulkanHelpers.ToVulkanLayout(srcLayout),
            dstTexture.Handle,
            VulkanHelpers.ToVulkanLayout(dstLayout),
            1,
            &copy);
        HasRecorded = true;
    }

    private IVulkanTexture ResolveTexture(ITexture texture) => texture as IVulkanTexture
        ?? throw new ArgumentException("Texture does not belong to the Vulkan backend.", nameof(texture));

    private VkBuffer ResolveBuffer(IBuffer buffer) => buffer is VulkanBuffer vkBuffer
        ? vkBuffer.Handle
        : throw new ArgumentException("Buffer does not belong to the Vulkan backend.", nameof(buffer));

    public void Dispose()
    {
    }
}
