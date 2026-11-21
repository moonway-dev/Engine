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

    uint Depth { get; }

    TextureFormat Format { get; }

    ImageView GetView(TextureViewType type);

    bool BelongsTo(VulkanDevice device);

    IDisposable Retain();
}

internal unsafe sealed class VulkanBuffer : IBuffer
{
    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private readonly VulkanMemoryManager _memory;
    private readonly MemoryAllocation _allocation;
    private readonly object _lifetimeLock = new();
    private int _retainedCount;
    private bool _disposeRequested;
    private bool _disposed;

    public VkBuffer Handle { get; }

    public ulong Size { get; }

    public BufferUsage Usage { get; }

    public MemoryUsage Memory { get; }

    public ulong DeviceAddress { get; }

    internal bool BelongsTo(VulkanDevice device) => ReferenceEquals(_device, device);

    public VulkanBuffer(Vk vk, VulkanDevice device, VulkanMemoryManager memory, in BufferDescription description, string debugName)
    {
        _vk = vk;
        _device = device;
        _memory = memory;
        if (description.Size == 0)
            throw new ArgumentOutOfRangeException(nameof(description), "Buffer size must be non-zero.");
        bool needsDeviceAddress = description.Usage.HasFlag(BufferUsage.DeviceAddress)
            || description.Usage.HasFlag(BufferUsage.AccelerationStructureBuildInput)
            || description.Usage.HasFlag(BufferUsage.AccelerationStructureStorage)
            || description.Usage.HasFlag(BufferUsage.ShaderBindingTable);
        if (needsDeviceAddress && !device.SupportsBufferDeviceAddress)
            throw new NotSupportedException("Buffer device addresses are unavailable on this Vulkan device.");
        if (needsDeviceAddress && !description.Usage.HasFlag(BufferUsage.DeviceAddress))
            throw new ArgumentException("Acceleration-structure and shader-binding-table buffers must include DeviceAddress usage.", nameof(description));
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
        MemoryAllocation allocation = default;
        bool allocated = false;
        try
        {
            allocation = memory.Allocate(requirements, description.Memory, debugName, needsDeviceAddress);
            allocated = true;
            VulkanHelpers.Throw(
                vk.BindBufferMemory(device.Handle, buffer, allocation.Memory, allocation.Offset),
                "vkBindBufferMemory");
            if (description.Usage.HasFlag(BufferUsage.DeviceAddress))
            {
                BufferDeviceAddressInfo addressInfo = new()
                {
                    SType = StructureType.BufferDeviceAddressInfo,
                    Buffer = buffer,
                };
                DeviceAddress = vk.GetBufferDeviceAddress(device.Handle, &addressInfo);
                if (DeviceAddress == 0)
                    throw new VulkanException(Result.ErrorFeatureNotPresent, $"vkGetBufferDeviceAddress returned zero for '{debugName}'");
            }
            _allocation = allocation;
        }
        catch
        {
            if (allocated)
                memory.Free(allocation);
            vk.DestroyBuffer(device.Handle, buffer, null);
            throw;
        }
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

    internal IDisposable Retain()
    {
        lock (_lifetimeLock)
        {
            ObjectDisposedException.ThrowIf(_disposeRequested, this);
            _retainedCount++;
            return new VulkanResourceLease(ReleaseRetained);
        }
    }

    public void Dispose()
    {
        lock (_lifetimeLock)
        {
            if (_disposeRequested)
                return;
            _disposeRequested = true;
            if (_retainedCount == 0)
                DestroyNative();
        }
    }

    private void ReleaseRetained()
    {
        lock (_lifetimeLock)
        {
            if (_retainedCount <= 0)
                throw new InvalidOperationException("Buffer retention count underflow.");
            _retainedCount--;
            if (_disposeRequested && _retainedCount == 0)
                DestroyNative();
        }
    }

    private void DestroyNative()
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
    private readonly object _lifetimeLock = new();
    private int _retainedCount;
    private bool _disposeRequested;
    private bool _disposed;

    public Image Handle { get; }

    public ImageView View { get; }

    public TextureFormat Format { get; }

    public uint Width { get; }

    public uint Height { get; }

    public uint MipLevels { get; }

    public uint ArrayLayers { get; }

    public uint Depth { get; }

    public TextureDimension Dimension { get; }

    private ImageView CubeView { get; set; }

    public VulkanTexture(Vk vk, VulkanDevice device, VulkanMemoryManager memory, in TextureDescription description, string debugName)
    {
        _vk = vk;
        _device = device;
        _memory = memory;
        Format = description.Format;
        Width = (uint)description.Extent.Width;
        Height = (uint)description.Extent.Height;
        Depth = Math.Max(1u, description.Depth);
        MipLevels = description.MipLevels == 0 ? 1 : description.MipLevels;
        ArrayLayers = description.ArrayLayers == 0
            ? (description.Dimension == TextureDimension.Cube ? 6u : 1u)
            : description.ArrayLayers;
        Dimension = description.Dimension;
        if (Dimension == TextureDimension.Cube && (Width != Height || ArrayLayers != 6))
            throw new ArgumentException("Cube textures must be square and have exactly six array layers.", nameof(description));
        if (Dimension == TextureDimension.Texture2D && ArrayLayers != 1)
            throw new ArgumentException("2D textures must have exactly one array layer.", nameof(description));
        if (Dimension == TextureDimension.Texture3D && ArrayLayers != 1)
            throw new ArgumentException("3D textures must have exactly one array layer.", nameof(description));
        if (Dimension is < TextureDimension.Texture2D or > TextureDimension.Texture3D)
            throw new ArgumentOutOfRangeException(nameof(description), "Texture dimension is not supported.");

        ImageCreateInfo createInfo = new()
        {
            SType = StructureType.ImageCreateInfo,
            Flags = Dimension == TextureDimension.Cube ? (ImageCreateFlags)0x10 : 0,
            ImageType = Dimension == TextureDimension.Texture3D ? ImageType.Type3D : ImageType.Type2D,
            Extent = new Extent3D(Width, Height, Dimension == TextureDimension.Texture3D ? Depth : 1),
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
            ViewType = Dimension switch
            {
                TextureDimension.Texture2D => ImageViewType.Type2D,
                TextureDimension.Texture2DArray => ImageViewType.Type2DArray,
                TextureDimension.Cube => ImageViewType.TypeCube,
                TextureDimension.Texture3D => ImageViewType.Type3D,
                _ => throw new ArgumentOutOfRangeException(nameof(description)),
            },
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
        if (Dimension == TextureDimension.Cube)
            CubeView = view;
    }

    public ImageView GetView(TextureViewType type) => type switch
    {
        TextureViewType.Texture2D when Dimension == TextureDimension.Texture2D => View,
        TextureViewType.Texture2DArray when Dimension == TextureDimension.Texture2DArray => View,
        TextureViewType.Cube when Dimension == TextureDimension.Cube => CubeView,
        TextureViewType.Texture3D when Dimension == TextureDimension.Texture3D => View,
        _ => throw new ArgumentException($"Texture dimension '{Dimension}' cannot provide a '{type}' view.", nameof(type)),
    };

    public bool BelongsTo(VulkanDevice device) => ReferenceEquals(_device, device);

    public IDisposable Retain()
    {
        lock (_lifetimeLock)
        {
            ObjectDisposedException.ThrowIf(_disposeRequested, this);
            _retainedCount++;
            return new VulkanResourceLease(ReleaseRetained);
        }
    }

    private void ReleaseRetained()
    {
        lock (_lifetimeLock)
        {
            if (_retainedCount <= 0)
                throw new InvalidOperationException("Texture retention count underflow.");
            _retainedCount--;
            if (_disposeRequested && _retainedCount == 0)
                DestroyNative();
        }
    }

    public VulkanTextureView CreateView(in TextureViewDescription description)
    {
        TextureViewDescription normalized = TextureViewValidation.ValidateAndNormalize(this, description);
        ImageViewCreateInfo viewInfo = new()
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = Handle,
            ViewType = normalized.Type switch
            {
                TextureViewType.Texture2D => ImageViewType.Type2D,
                TextureViewType.Texture2DArray => ImageViewType.Type2DArray,
                TextureViewType.Cube => ImageViewType.TypeCube,
                _ => throw new ArgumentOutOfRangeException(nameof(description)),
            },
            Format = VulkanHelpers.ToVulkanFormat(Format),
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = AspectMaskFor(Format),
                BaseMipLevel = normalized.BaseMipLevel,
                LevelCount = normalized.MipLevelCount,
                BaseArrayLayer = normalized.BaseArrayLayer,
                LayerCount = normalized.ArrayLayerCount,
            },
        };
        VulkanHelpers.Throw(_vk.CreateImageView(_device.Handle, &viewInfo, null, out ImageView view), "vkCreateImageView (texture view)");
        return new VulkanTextureView(_vk, _device, this, normalized, view);
    }

    internal static ImageAspectFlags AspectMaskFor(TextureFormat format) => format switch
    {
        TextureFormat.D16Unorm or TextureFormat.D32Float => ImageAspectFlags.DepthBit,
        TextureFormat.D24UnormS8Uint or TextureFormat.D32FloatS8Uint => ImageAspectFlags.DepthBit | ImageAspectFlags.StencilBit,
        _ => ImageAspectFlags.ColorBit,
    };

    public void Dispose()
    {
        lock (_lifetimeLock)
        {
            if (_disposeRequested)
                return;
            _disposeRequested = true;
            if (_retainedCount == 0)
                DestroyNative();
        }
    }

    private void DestroyNative()
    {
        if (_disposed)
            return;
        _disposed = true;
        _vk.DestroyImageView(_device.Handle, View, null);
        _vk.DestroyImage(_device.Handle, Handle, null);
        _memory.Free(_allocation);
    }
}

internal unsafe sealed class VulkanTextureView : ITextureView, IVulkanTexture
{
    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private readonly object _lifetimeLock = new();
    private int _retainedCount;
    private bool _disposeRequested;
    private bool _disposed;

    public VulkanTextureView(Vk vk, VulkanDevice device, VulkanTexture texture, in TextureViewDescription description, ImageView view)
    {
        _vk = vk;
        _device = device;
        Texture = texture;
        Type = description.Type;
        BaseMipLevel = description.BaseMipLevel;
        MipLevelCount = description.MipLevelCount;
        BaseArrayLayer = description.BaseArrayLayer;
        ArrayLayerCount = description.ArrayLayerCount;
        View = view;
        Handle = texture.Handle;
        Width = Math.Max(1u, texture.Width >> checked((int)BaseMipLevel));
        Height = Math.Max(1u, texture.Height >> checked((int)BaseMipLevel));
        Depth = Math.Max(1u, texture.Depth >> checked((int)BaseMipLevel));
        Format = texture.Format;
    }

    public ITexture Texture { get; }

    public TextureViewType Type { get; }

    public uint BaseMipLevel { get; }

    public uint MipLevelCount { get; }

    public uint BaseArrayLayer { get; }

    public uint ArrayLayerCount { get; }

    public Image Handle { get; }

    public ImageView View { get; }

    public uint Width { get; }

    public uint Height { get; }

    public uint Depth { get; }

    public TextureFormat Format { get; }

    public uint MipLevels => MipLevelCount;

    public uint ArrayLayers => ArrayLayerCount;

    public TextureDimension Dimension => Texture.Dimension;

    public ImageView GetView(TextureViewType type) => type == Type
        ? View
        : throw new ArgumentException($"This view is '{Type}', not '{type}'.", nameof(type));

    public bool BelongsTo(VulkanDevice device) => ReferenceEquals(_device, device);

    public IDisposable Retain()
    {
        lock (_lifetimeLock)
        {
            ObjectDisposedException.ThrowIf(_disposeRequested, this);
            IDisposable textureLease = ((IVulkanTexture)Texture).Retain();
            _retainedCount++;
            return new VulkanResourceLease(() =>
            {
                lock (_lifetimeLock)
                {
                    if (_retainedCount <= 0)
                        throw new InvalidOperationException("Texture-view retention count underflow.");
                    _retainedCount--;
                    textureLease.Dispose();
                    if (_disposeRequested && _retainedCount == 0)
                        DestroyNative();
                }
            });
        }
    }

    public void Dispose()
    {
        lock (_lifetimeLock)
        {
            if (_disposeRequested)
                return;
            _disposeRequested = true;
            if (_retainedCount == 0)
                DestroyNative();
        }
    }

    private void DestroyNative()
    {
        if (_disposed)
            return;
        _disposed = true;
        _vk.DestroyImageView(_device.Handle, View, null);
    }
}

internal unsafe sealed class VulkanSwapchainImage : ITexture, IVulkanTexture
{
    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private readonly object _lifetimeLock = new();
    private int _retainedCount;
    private bool _disposeRequested;
    private bool _disposed;

    public Image Handle { get; }

    public ImageView View { get; }

    public TextureFormat Format { get; }

    public uint Width { get; }

    public uint Height { get; }

    public uint Depth => 1u;

    public uint MipLevels => 1;

    public uint ArrayLayers => 1;

    public TextureDimension Dimension => TextureDimension.Texture2D;

    public ImageView GetView(TextureViewType type) => type == TextureViewType.Texture2D
        ? View
        : throw new ArgumentException("Swapchain images do not support cube views.", nameof(type));

    public bool BelongsTo(VulkanDevice device) => ReferenceEquals(_device, device);

    public IDisposable Retain()
    {
        lock (_lifetimeLock)
        {
            ObjectDisposedException.ThrowIf(_disposeRequested, this);
            _retainedCount++;
            return new VulkanResourceLease(ReleaseRetained);
        }
    }

    private void ReleaseRetained()
    {
        lock (_lifetimeLock)
        {
            if (_retainedCount <= 0)
                throw new InvalidOperationException("Swapchain image retention count underflow.");
            _retainedCount--;
            if (_disposeRequested && _retainedCount == 0)
                DestroyNative();
        }
    }

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
        lock (_lifetimeLock)
        {
            if (_disposeRequested)
                return;
            _disposeRequested = true;
            if (_retainedCount == 0)
                DestroyNative();
        }
    }

    private void DestroyNative()
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
    private VulkanRayTracingPipeline? _boundRayTracingPipeline;
    private readonly Dictionary<uint, VulkanDescriptorSet> _boundDescriptorSets = [];
    private bool _recording;
    private bool _renderPassActive;
    private readonly QueryPool _timestampQueryPool;
    private readonly uint _timestampValidBits;
    private uint _nextTimestampIndex;
    private bool _timestampOverflow;
    private readonly List<string> _timestampNames = [];
    private readonly Stack<(bool Recorded, string Name)> _timestampStack = new();
    private readonly Action<CommandBuffer>? _release;
    private readonly object? _owner;
    private readonly object _lifetimeLock = new();
    private readonly VulkanCommandBufferSubmission _submission = new();
    private CommandBufferLifetime _lifetime;

    public VulkanCommandBuffer(
        Vk vk,
        VulkanDevice device,
        CommandBuffer handle,
        QueryPool timestampQueryPool = default,
        uint timestampValidBits = 0,
        Action<CommandBuffer>? release = null,
        object? owner = null)
    {
        _vk = vk;
        _device = device;
        _handle = handle;
        _timestampQueryPool = timestampQueryPool;
        _timestampValidBits = timestampValidBits;
        _release = release;
        _owner = owner;
    }

    internal CommandBuffer Handle => _handle;

    internal bool HasRecorded { get; private set; }

    internal bool IsRecording => _recording;

    internal bool HasOpenTimestamp => _timestampStack.Count > 0;

    internal uint TimestampCount => _nextTimestampIndex;

    internal IReadOnlyList<string> TimestampNames => _timestampNames;

    internal bool TimestampOverflow => _timestampOverflow;

    internal VulkanCommandSubmissionState SubmissionState => _submission.State;

    internal bool HasRetainedResources => _submission.HasRetainedResources;

    internal void MarkSubmitted(object owner)
    {
        lock (_lifetimeLock)
        {
            if (_lifetime == CommandBufferLifetime.Released)
                throw new ObjectDisposedException(nameof(VulkanCommandBuffer));
            if (_lifetime == CommandBufferLifetime.Submitted)
                throw new InvalidOperationException("Command buffer has already been submitted.");
            if (_release is null)
                throw new ArgumentException("Command buffer is not a one-shot device command buffer.", nameof(owner));
            if (_owner is not null && !ReferenceEquals(_owner, owner))
                throw new ArgumentException("Command buffer was created by a different Vulkan device adapter.", nameof(owner));
            _submission.MarkSubmitted();
            _lifetime = CommandBufferLifetime.Submitted;
        }
    }

    internal void MarkFrameSubmitted()
    {
        lock (_lifetimeLock)
        {
            if (_release is not null)
                throw new InvalidOperationException("Only a frame command buffer can use frame submission tracking.");
            if (_lifetime != CommandBufferLifetime.Allocated)
                throw new InvalidOperationException($"Frame command buffer cannot be submitted in state {_lifetime}.");
            _submission.MarkSubmitted();
            _lifetime = CommandBufferLifetime.Submitted;
        }
    }

    internal void MarkQueueAccepted()
    {
        lock (_lifetimeLock)
            _submission.MarkQueueAccepted();
    }

    internal void ReleaseAfterSubmit()
    {
        Action<CommandBuffer>? release = null;
        Exception? failure = null;
        lock (_lifetimeLock)
        {
            if (_lifetime != CommandBufferLifetime.Submitted)
                return;
            try
            {
                _submission.CompleteSubmission();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                _lifetime = CommandBufferLifetime.Released;
                release = _release;
            }
        }
        try
        {
            release?.Invoke(_handle);
        }
        catch (Exception exception)
        {
            failure ??= exception;
        }
        if (failure is not null)
            throw failure;
    }

    internal void AbandonAfterSubmit()
    {
        lock (_lifetimeLock)
        {
            if (_lifetime == CommandBufferLifetime.Submitted)
                _submission.AbandonSubmission();
        }
    }

    internal void AbortUnsubmittedSubmit()
    {
        Action<CommandBuffer>? release;
        lock (_lifetimeLock)
        {
            if (_lifetime != CommandBufferLifetime.Submitted)
                return;
            _submission.RollbackSubmit();
            _submission.CancelSubmission();
            _lifetime = CommandBufferLifetime.Released;
            release = _release;
        }
        release?.Invoke(_handle);
    }

    internal void ReleaseResourcesAfterFence(bool wasSubmitted)
    {
        lock (_lifetimeLock)
        {
            if (wasSubmitted)
            {
                if (_submission.State is VulkanCommandSubmissionState.Submitted or VulkanCommandSubmissionState.Abandoned
                    && !_submission.SubmissionAccepted)
                    throw new InvalidOperationException("A command buffer cannot retire as submitted before queue acceptance is confirmed.");
                _submission.CompleteSubmission();
                if (_lifetime == CommandBufferLifetime.Submitted)
                    _lifetime = CommandBufferLifetime.Allocated;
            }
            else if (_submission.State is VulkanCommandSubmissionState.Recording or VulkanCommandSubmissionState.Executable)
                _submission.CancelSubmission();
            else if (_submission.State == VulkanCommandSubmissionState.Submitted && !_submission.SubmissionAccepted)
            {
                _submission.RollbackSubmit();
                _submission.CancelSubmission();
                _lifetime = CommandBufferLifetime.Allocated;
            }
        }
    }

    internal void ResetState()
    {
        lock (_lifetimeLock)
        {
            if (_lifetime == CommandBufferLifetime.Submitted)
                throw new InvalidOperationException("Cannot reset a command buffer before its submitted work has retired.");
            if (_submission.State is VulkanCommandSubmissionState.Recording or VulkanCommandSubmissionState.Executable)
                _submission.CancelSubmission();
            _lifetime = CommandBufferLifetime.Allocated;
        }
        _recording = false;
        _renderPassActive = false;
        _boundLayout = default;
        _boundBindPoint = PipelineBindPoint.Graphics;
        _boundRayTracingPipeline = null;
        _boundDescriptorSets.Clear();
        _nextTimestampIndex = 0;
        _timestampOverflow = false;
        _timestampNames.Clear();
        _timestampStack.Clear();
        HasRecorded = false;
    }

    internal void MarkRecorded() => HasRecorded = true;

    public void BeginTimestamp(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Timestamp name must not be empty.", nameof(name));
        if (!_recording)
            throw new InvalidOperationException("Command buffer is not recording.");
        bool canRecord = _timestampQueryPool.Handle != 0 && _timestampValidBits > 0 && !_timestampOverflow
            && _nextTimestampIndex + 2 <= VulkanRHI.TimestampQueryCapacity;
        if (canRecord)
        {
            _vk.CmdWriteTimestamp(_handle, PipelineStageFlags.TopOfPipeBit, _timestampQueryPool, _nextTimestampIndex);
            _timestampNames.Add(name);
        }
        else if (_timestampQueryPool.Handle != 0 && _timestampValidBits > 0)
            _timestampOverflow = true;
        _timestampStack.Push((canRecord, name));
    }

    public void EndTimestamp()
    {
        if (!_recording || _timestampStack.Count == 0)
            throw new InvalidOperationException("No timestamp scope is active.");
        (bool recorded, _) = _timestampStack.Pop();
        if (recorded)
        {
            _vk.CmdWriteTimestamp(_handle, PipelineStageFlags.BottomOfPipeBit, _timestampQueryPool, _nextTimestampIndex + 1);
            _nextTimestampIndex += 2;
        }
    }

    public void Begin()
    {
        CommandBufferBeginInfo beginInfo = new()
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };
        lock (_lifetimeLock)
        {
            if (_lifetime is CommandBufferLifetime.Submitted or CommandBufferLifetime.Released)
                throw new InvalidOperationException($"Command buffer cannot begin recording in state {_lifetime}.");
            _submission.ResetForRecording();
        }
        VulkanHelpers.Throw(_vk.BeginCommandBuffer(_handle, &beginInfo), "vkBeginCommandBuffer");
        _recording = true;
        if (_timestampQueryPool.Handle != 0 && _timestampValidBits > 0)
            _vk.CmdResetQueryPool(_handle, _timestampQueryPool, 0, VulkanRHI.TimestampQueryCapacity);
    }

    public void End()
    {
        if (!_recording)
            throw new InvalidOperationException("Command buffer is not recording.");
        if (_renderPassActive)
            throw new InvalidOperationException("Render pass is still active.");
        if (_timestampStack.Count > 0)
            throw new InvalidOperationException("Timestamp scopes are still active.");
        VulkanHelpers.Throw(_vk.EndCommandBuffer(_handle), "vkEndCommandBuffer");
        _recording = false;
        lock (_lifetimeLock)
            _submission.MarkExecutable();
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
                ImageLayout = VulkanHelpers.ToVulkanLayout(depthInfo.Layout),
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
        EnsurePipelineBindable();
        if (pipeline is not VulkanGraphicsPipeline vkPipeline)
            throw new ArgumentException("Pipeline does not belong to the Vulkan backend.", nameof(pipeline));
        _vk.CmdBindPipeline(_handle, PipelineBindPoint.Graphics, vkPipeline.Handle);
        _boundLayout = vkPipeline.Program.Layout;
        _boundBindPoint = PipelineBindPoint.Graphics;
        _boundRayTracingPipeline = null;
        _boundDescriptorSets.Clear();
    }

    public void BindComputePipeline(IComputePipeline pipeline)
    {
        EnsurePipelineBindable();
        if (pipeline is not VulkanComputePipeline vkPipeline)
            throw new ArgumentException("Pipeline does not belong to the Vulkan backend.", nameof(pipeline));
        _vk.CmdBindPipeline(_handle, PipelineBindPoint.Compute, vkPipeline.Handle);
        _boundLayout = vkPipeline.Program.Layout;
        _boundBindPoint = PipelineBindPoint.Compute;
        _boundRayTracingPipeline = null;
        _boundDescriptorSets.Clear();
    }

    public void BindRayTracingPipeline(IRayTracingPipeline pipeline)
    {
        EnsurePipelineBindable();
        if (!_device.SupportsRayTracing)
            throw new NotSupportedException(_device.RayTracingUnavailableReason ?? "Vulkan ray-tracing pipelines are unavailable.");
        if (pipeline is not VulkanRayTracingPipeline vkPipeline || !vkPipeline.BelongsTo(_device))
            throw new ArgumentException("Ray-tracing pipeline does not belong to this Vulkan device.", nameof(pipeline));
        RetainResource(vkPipeline);
        _vk.CmdBindPipeline(_handle, PipelineBindPoint.RayTracingKhr, vkPipeline.Handle);
        _boundLayout = vkPipeline.Program.Layout;
        _boundBindPoint = PipelineBindPoint.RayTracingKhr;
        _boundRayTracingPipeline = vkPipeline;
        _boundDescriptorSets.Clear();
        HasRecorded = true;
    }

    public void TraceRays(ShaderBindingTable table, uint width, uint height = 1, uint depth = 1)
    {
        if (!_recording)
            throw new InvalidOperationException("Command buffer is not recording.");
        if (_renderPassActive)
            throw new InvalidOperationException("Ray tracing dispatch cannot run inside a render pass.");
        if (!_device.SupportsRayTracing)
            throw new NotSupportedException(_device.RayTracingUnavailableReason ?? "Vulkan ray tracing is unavailable.");
        if (_boundBindPoint != PipelineBindPoint.RayTracingKhr || _boundRayTracingPipeline is null)
            throw new InvalidOperationException("A ray-tracing pipeline must be bound before dispatch.");
        ArgumentNullException.ThrowIfNull(table);
        if (!ReferenceEquals(table.Pipeline, _boundRayTracingPipeline))
            throw new ArgumentException("Shader binding table was created for a different ray-tracing pipeline.", nameof(table));
        if (width == 0 || height == 0 || depth == 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Ray dispatch dimensions must be non-zero.");

        RayTracingLimits limits = _device.Physical.RayTracing.Limits;
        ulong invocationCount = checked((ulong)width * height * depth);
        if (limits.MaxRayDispatchInvocationCount == 0 || invocationCount > limits.MaxRayDispatchInvocationCount)
            throw new ArgumentOutOfRangeException(nameof(width),
                $"Ray dispatch invocation count {invocationCount} exceeds the device limit {limits.MaxRayDispatchInvocationCount}.");
        if (table.Buffer is not VulkanBuffer sbtBuffer || !sbtBuffer.BelongsTo(_device)
            || !sbtBuffer.Usage.HasFlag(BufferUsage.ShaderBindingTable | BufferUsage.DeviceAddress))
            throw new ArgumentException("Shader binding table buffer does not belong to this Vulkan device or lacks required usage.", nameof(table));
        ValidateSbtRegion(table.RayGeneration, limits, sbtBuffer, nameof(table.RayGeneration), required: true);
        ValidateSbtRegion(table.Miss, limits, sbtBuffer, nameof(table.Miss), required: false);
        ValidateSbtRegion(table.Hit, limits, sbtBuffer, nameof(table.Hit), required: false);
        ValidateSbtRegion(table.Callable, limits, sbtBuffer, nameof(table.Callable), required: false);
        foreach (uint requiredSet in _boundRayTracingPipeline.Program.DeclaredSetIndices)
        {
            if (!_boundDescriptorSets.ContainsKey(requiredSet))
                throw new InvalidOperationException($"Ray-tracing pipeline descriptor set {requiredSet} has not been bound.");
        }
        foreach (VulkanDescriptorSet set in _boundDescriptorSets.Values)
        {
            if (set.ProgramLayout.Handle != _boundLayout.Handle)
                throw new InvalidOperationException("A bound descriptor set is incompatible with the ray-tracing pipeline.");
            RetainResource(set);
            foreach (VulkanAccelerationStructure structure in set.AccelerationStructures)
            {
                structure.ValidateTrace(_submission);
                RetainResource(structure);
            }
        }

        RetainResource(sbtBuffer);
        StridedDeviceAddressRegionKHR rayGeneration = ToVulkanRegion(table.RayGeneration);
        StridedDeviceAddressRegionKHR miss = ToVulkanRegion(table.Miss);
        StridedDeviceAddressRegionKHR hit = ToVulkanRegion(table.Hit);
        StridedDeviceAddressRegionKHR callable = ToVulkanRegion(table.Callable);
        _device.RayTracingPipelineExtension.CmdTraceRays(
            _handle,
            in rayGeneration,
            in miss,
            in hit,
            in callable,
            width,
            height,
            depth);
        HasRecorded = true;
    }

    private static void ValidateSbtRegion(ShaderBindingRegion region, RayTracingLimits limits, VulkanBuffer buffer, string name, bool required)
    {
        if (region.Size == 0)
        {
            if (required || region.DeviceAddress != 0 || region.Stride != 0)
                throw new ArgumentException($"Shader binding table region '{name}' is empty or malformed.", name);
            return;
        }
        if (limits.ShaderGroupBaseAlignment == 0 || limits.ShaderGroupHandleAlignment == 0
            || region.DeviceAddress == 0 || region.Stride == 0 || region.Stride > limits.MaxShaderGroupStride
            || region.Size % region.Stride != 0 || region.DeviceAddress % limits.ShaderGroupBaseAlignment != 0
            || region.Stride % limits.ShaderGroupHandleAlignment != 0
            || name == nameof(ShaderBindingTable.RayGeneration) && region.Size != region.Stride)
            throw new ArgumentException($"Shader binding table region '{name}' violates Vulkan alignment or size requirements.", name);

        ulong bufferStart = buffer.DeviceAddress;
        ulong regionEnd;
        ulong bufferEnd;
        try
        {
            regionEnd = checked(region.DeviceAddress + region.Size);
            bufferEnd = checked(bufferStart + buffer.Size);
        }
        catch (OverflowException exception)
        {
            throw new ArgumentException($"Shader binding table region '{name}' address range overflows.", name, exception);
        }
        if (region.DeviceAddress < bufferStart || regionEnd > bufferEnd)
            throw new ArgumentException($"Shader binding table region '{name}' falls outside its buffer device-address range.", name);
    }

    private static StridedDeviceAddressRegionKHR ToVulkanRegion(ShaderBindingRegion region) => new()
    {
        DeviceAddress = region.DeviceAddress,
        Stride = region.Stride,
        Size = region.Size,
    };

    private void EnsurePipelineBindable()
    {
        if (!_recording)
            throw new InvalidOperationException("Command buffer is not recording.");
    }

    public void BindDescriptorSet(IDescriptorSet set, uint setIndex, ReadOnlySpan<uint> dynamicOffsets = default)
    {
        if (!_recording)
            throw new InvalidOperationException("Command buffer is not recording.");
        if (_boundLayout.Handle == 0)
            throw new InvalidOperationException("A pipeline must be bound before binding descriptor sets.");
        if (set is not VulkanDescriptorSet vkSet || !vkSet.BelongsTo(_device))
            throw new ArgumentException("Descriptor set does not belong to this Vulkan device.", nameof(set));
        if (vkSet.ProgramLayout.Handle != _boundLayout.Handle)
            throw new ArgumentException("Descriptor set layout does not match the currently bound pipeline layout.", nameof(set));
        if (vkSet.SetIndex != setIndex)
            throw new ArgumentException($"Descriptor set {vkSet.SetIndex} cannot be bound at index {setIndex}.", nameof(set));
        RetainResource(vkSet);
        foreach (VulkanAccelerationStructure structure in vkSet.AccelerationStructures)
            RetainResource(structure);
        _boundDescriptorSets[setIndex] = vkSet;
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
            _boundLayout,
            setIndex,
            1,
            &descriptorSet,
            (uint)dynamicOffsets.Length,
            offsets);
        _boundDescriptorSets[setIndex] = vkSet;
        HasRecorded = true;
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

    public void BuildAccelerationStructure(IAccelerationStructure destination,
        AccelerationStructureBuildDescription description, IBuffer scratch, ulong scratchOffset = 0, bool update = false)
    {
        if (!_recording)
            throw new InvalidOperationException("Command buffer is not recording.");
        if (_renderPassActive)
            throw new InvalidOperationException("Acceleration structure builds cannot run inside a render pass.");
        if (destination is not VulkanAccelerationStructure vkDestination || !vkDestination.BelongsTo(_device))
            throw new ArgumentException("Acceleration structure does not belong to this Vulkan device.", nameof(destination));
        if (scratch is not VulkanBuffer vkScratch || !vkScratch.BelongsTo(_device))
            throw new ArgumentException("Scratch buffer does not belong to this Vulkan device.", nameof(scratch));
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(description.Geometries);
        description = description with
        {
            Geometries = description.Geometries.Select(geometry => geometry is InstanceGeometry instances
                ? instances with { BottomLevels = instances.BottomLevels.ToArray() }
                : geometry).ToArray(),
        };
        if (description.Type != destination.Type)
            throw new ArgumentException("Build description type does not match the destination acceleration structure.", nameof(description));
        if (!_device.SupportsAccelerationStructures)
            throw new NotSupportedException("Vulkan acceleration structures are unavailable on this device.");

        RayTracingLimits limits = _device.Physical.RayTracing.Limits;
        RayTracingValidation.ValidateBuild(description, limits);

        if (!vkScratch.Usage.HasFlag(BufferUsage.Storage | BufferUsage.DeviceAddress) || vkScratch.DeviceAddress == 0)
            throw new ArgumentException("AS scratch requires a storage buffer with a device address.", nameof(scratch));
        ulong scratchAddress = checked(vkScratch.DeviceAddress + scratchOffset);
        uint scratchAlignment = limits.MinScratchAlignment;
        if (scratchAlignment == 0 || scratchAddress % scratchAlignment != 0)
            throw new ArgumentException($"AS scratch address must be aligned to {scratchAlignment} bytes.", nameof(scratchOffset));

        AccelerationStructureBuildSizes sizes = VulkanRayTracingOperations.QueryBuildSizes(_vk, _device, description);
        ulong requiredScratchSize = update ? sizes.UpdateScratchSize : sizes.BuildScratchSize;
        if (requiredScratchSize == 0 || scratchOffset > vkScratch.Size || requiredScratchSize > vkScratch.Size - scratchOffset)
            throw new ArgumentException("AS scratch buffer range is too small for this build.", nameof(scratch));
        if (vkDestination.Size < sizes.StorageSize)
            throw new ArgumentException("Destination acceleration structure storage is too small for this build.", nameof(destination));

        AccelerationStructureGeometryKHR[] geometries = VulkanRayTracingOperations.CreateGeometries(description);
        foreach (AccelerationStructureGeometry geometry in description.Geometries)
        {
            switch (geometry)
            {
                case TriangleGeometry triangles:
                    RetainBuffer(triangles.VertexBuffer);
                    if (triangles.IndexBuffer is not null)
                        RetainBuffer(triangles.IndexBuffer);
                    break;
                case AabbGeometry aabbs:
                    RetainBuffer(aabbs.Buffer);
                    break;
                case InstanceGeometry instances:
                    RetainBuffer(instances.Buffer);
                    foreach (IAccelerationStructure bottomLevel in instances.BottomLevels)
                    {
                        if (bottomLevel is not VulkanAccelerationStructure vkBottomLevel || !vkBottomLevel.BelongsTo(_device))
                            throw new ArgumentException("TLAS references an acceleration structure from a different Vulkan device.", nameof(description));
                    }
                    break;
            }
        }
        RetainResource(vkDestination);
        RetainResource(vkScratch);

        AccelerationStructureBuildRangeInfoKHR[] ranges = description.Geometries.Select(geometry => new AccelerationStructureBuildRangeInfoKHR
        {
            PrimitiveCount = geometry.PrimitiveCount,
        }).ToArray();
        AccelerationStructureBuildGeometryInfoKHR buildInfo = new()
        {
            SType = StructureType.AccelerationStructureBuildGeometryInfoKhr,
            Type = VulkanRayTracingOperations.ToVulkanType(description.Type),
            Flags = VulkanRayTracingOperations.ToVulkanBuildFlags(description.Flags),
            Mode = update ? BuildAccelerationStructureModeKHR.UpdateKhr : BuildAccelerationStructureModeKHR.BuildKhr,
            SrcAccelerationStructure = update ? vkDestination.Handle : default,
            DstAccelerationStructure = vkDestination.Handle,
            GeometryCount = (uint)geometries.Length,
        };
        QueuePendingBuild(vkDestination, description, update);
        fixed (AccelerationStructureGeometryKHR* geometryPointer = geometries)
        fixed (AccelerationStructureBuildRangeInfoKHR* rangePointer = ranges)
        {
            buildInfo.PGeometries = geometryPointer;
            buildInfo.ScratchData = new DeviceOrHostAddressKHR { DeviceAddress = scratchAddress };
            AccelerationStructureBuildRangeInfoKHR* rangeList = rangePointer;
            _device.AccelerationStructureExtension.CmdBuildAccelerationStructures(_handle, 1, &buildInfo, &rangeList);
        }

        HasRecorded = true;
        MemoryBarrier(
            PipelineStages.AccelerationStructureBuild,
            ResourceAccess.AccelerationStructureWrite,
            PipelineStages.AccelerationStructureBuild | PipelineStages.RayTracingShader,
            ResourceAccess.AccelerationStructureRead | ResourceAccess.AccelerationStructureWrite);
    }

    private void RetainBuffer(IBuffer buffer)
    {
        if (buffer is not VulkanBuffer vulkanBuffer || !vulkanBuffer.BelongsTo(_device))
            throw new ArgumentException("Acceleration structure input buffer does not belong to this Vulkan device.", nameof(buffer));
        RetainResource(vulkanBuffer);
    }

    private void RetainResource(VulkanBuffer buffer) => _submission.Retain(buffer, buffer.Retain());

    private void RetainResource(VulkanAccelerationStructure structure) => _submission.Retain(structure, structure.Retain());

    private void RetainResource(VulkanDescriptorSet descriptorSet) => _submission.Retain(descriptorSet, descriptorSet.Retain());

    private void RetainResource(VulkanRayTracingPipeline pipeline) => _submission.Retain(pipeline, pipeline.Retain());

    private void QueuePendingBuild(VulkanAccelerationStructure structure, AccelerationStructureBuildDescription description, bool update)
    {
        VulkanAccelerationStructure.PendingBuild pending = structure.BeginBuild(_submission, description, update);
        try
        {
            _submission.Retain(pending, pending);
            _submission.AddPendingCommit(pending.Commit);
        }
        catch
        {
            pending.Dispose();
            throw;
        }
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

    public void MemoryBarrier(PipelineStages sourceStages, ResourceAccess sourceAccess,
        PipelineStages destinationStages, ResourceAccess destinationAccess)
    {
        MemoryBarrier2 barrier = new()
        {
            SType = StructureType.MemoryBarrier2,
            SrcStageMask = VulkanHelpers.ToVulkanPipelineStages2(sourceStages),
            SrcAccessMask = VulkanHelpers.ToVulkanAccess2(sourceAccess),
            DstStageMask = VulkanHelpers.ToVulkanPipelineStages2(destinationStages),
            DstAccessMask = VulkanHelpers.ToVulkanAccess2(destinationAccess),
        };
        DependencyInfo dependency = new()
        {
            SType = StructureType.DependencyInfo,
            MemoryBarrierCount = 1,
            PMemoryBarriers = &barrier,
        };
        _vk.CmdPipelineBarrier2(_handle, &dependency);
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
        BufferMemoryBarrier2 barrier = new()
        {
            SType = StructureType.BufferMemoryBarrier2,
            SrcStageMask = VulkanHelpers.ToVulkanPipelineStages2(sourceStages),
            SrcAccessMask = VulkanHelpers.ToVulkanAccess2(sourceAccess),
            DstStageMask = VulkanHelpers.ToVulkanPipelineStages2(destinationStages),
            DstAccessMask = VulkanHelpers.ToVulkanAccess2(destinationAccess),
            SrcQueueFamilyIndex = uint.MaxValue,
            DstQueueFamilyIndex = uint.MaxValue,
            Buffer = ResolveBuffer(buffer),
            Offset = offset,
            Size = size,
        };
        DependencyInfo dependency = new()
        {
            SType = StructureType.DependencyInfo,
            BufferMemoryBarrierCount = 1,
            PBufferMemoryBarriers = &barrier,
        };
        _vk.CmdPipelineBarrier2(_handle, &dependency);
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

    internal void ReleaseAfterDeviceIdle()
    {
        Action<CommandBuffer>? release = null;
        Exception? failure = null;
        lock (_lifetimeLock)
        {
            try
            {
                if (_submission.State is VulkanCommandSubmissionState.Submitted or VulkanCommandSubmissionState.Abandoned)
                    _submission.CompleteSubmission();
                else if (_submission.State is VulkanCommandSubmissionState.Recording or VulkanCommandSubmissionState.Executable)
                    _submission.CancelSubmission();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                if (_release is not null)
                {
                    _lifetime = CommandBufferLifetime.Released;
                    release = _release;
                }
                else if (_lifetime != CommandBufferLifetime.Released)
                    _lifetime = CommandBufferLifetime.Allocated;
            }
        }
        try
        {
            release?.Invoke(_handle);
        }
        catch (Exception exception)
        {
            failure ??= exception;
        }
        if (failure is not null)
            throw failure;
    }

    public void Dispose()
    {
        Action<CommandBuffer>? release;
        lock (_lifetimeLock)
        {
            if (_lifetime == CommandBufferLifetime.Released || _lifetime == CommandBufferLifetime.Submitted)
                return;
            if (_submission.State is VulkanCommandSubmissionState.Recording or VulkanCommandSubmissionState.Executable)
                _submission.CancelSubmission();
            _lifetime = CommandBufferLifetime.Released;
            release = _release;
        }
        release?.Invoke(_handle);
    }

    private enum CommandBufferLifetime
    {
        Allocated,
        Submitted,
        Released,
    }
}
