using Engine.Core;
using Engine.Mathematics;
using Engine.Platform;
using Engine.RHI;
using Silk.NET.Core;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace Engine.Backends.Vulkan;

using VkSemaphore = Silk.NET.Vulkan.Semaphore;
using VkImageLayout = Silk.NET.Vulkan.ImageLayout;

internal unsafe sealed class VulkanRHI : IRHI
{
    private readonly ILogger _log;
    private readonly Vk _vk = Vk.GetApi();
    private VulkanInstance? _instance;
    private VulkanPhysicalDevice? _physical;
    private VulkanDevice? _device;
    private VulkanSwapchain? _swapchain;
    private VulkanMemoryManager? _memory;
    private VulkanStagingUploader? _uploader;
    private VulkanReadback? _readback;
    private VulkanDescriptorAllocator? _descriptorAllocator;
    private KhrSurface? _khrSurface;
    private SurfaceKHR _surface;
    private List<Frame> _frames = [];
    private readonly List<VkSemaphore> _renderFinishedSemaphores = [];
    private uint _frameIndex;
    private Frame? _currentFrame;
    private VulkanCommandBuffer? _frameCommandBuffer;
    private uint _currentImageIndex;
    private ulong _swapchainGeneration;
    private bool _initialized;
    private VulkanDeviceAdapter? _deviceAdapter;

    public string Name => "Vulkan Renderer";

    public GraphicsBackend Backend => GraphicsBackend.Vulkan;

    public IDevice Device => _deviceAdapter ?? throw new InvalidOperationException("Vulkan RHI is not initialized.");

    public ISwapchain Swapchain => _swapchain ?? throw new InvalidOperationException("Vulkan RHI is not initialized.");

    public uint CurrentImageIndex => _currentImageIndex;

    public uint CurrentFrameSlot => _frameIndex;

    public uint FramesInFlight => (uint)_frames.Count;

    public VulkanRHI(ILogger log)
    {
        _log = log;
    }

    public void Initialize(RHIConfiguration configuration, SurfaceSource? surfaceSource)
    {
        if (surfaceSource is null)
            throw new VulkanException(Result.ErrorInitializationFailed, "Vulkan backend requires a window surface; use the Null backend for headless mode");

        SurfaceSource source = surfaceSource.Value;
        _instance = VulkanInstance.Create(_vk, configuration, source.RequiredInstanceExtensions, _log);

        ulong surfaceHandle = source.CreateSurface(_instance.Handle.Handle);
        _surface = new SurfaceKHR(surfaceHandle);

        if (!_vk.TryGetInstanceExtension(_instance.Handle, out _khrSurface))
            throw new VulkanException(Result.ErrorExtensionNotPresent, "Loading VK_KHR_surface");

        _physical = VulkanPhysicalDevice.Select(_vk, _instance.Handle, _khrSurface, _surface, configuration.PreferredDeviceIndex, _log);
        _device = new VulkanDevice(_vk, _instance.Handle, _physical, _log);
        _memory = new VulkanMemoryManager(_vk, _device, _physical, _log);
        _descriptorAllocator = new VulkanDescriptorAllocator(_vk, _device, _log);
        _swapchain = new VulkanSwapchain(_vk, _device, _khrSurface!, _surface, configuration.VSync, configuration.HeadlessExtent, _log);
        _readback = new VulkanReadback(_vk, _device, _memory);
        _uploader = new VulkanStagingUploader(_vk, _device, _memory, _log);

        uint framesInFlight = Math.Max(1u, configuration.FramesInFlight);
        for (uint i = 0; i < framesInFlight; i++)
            _frames.Add(CreateFrame());
        EnsureRenderFinishedSemaphores();

        FeatureInfo features = BuildFeatures();
        _deviceAdapter = new VulkanDeviceAdapter(_vk, _device, _physical, _memory, _uploader, _descriptorAllocator, features);
        _initialized = true;
    }

    private Frame CreateFrame()
    {
        CommandPoolCreateInfo poolInfo = new()
        {
            SType = StructureType.CommandPoolCreateInfo,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
            QueueFamilyIndex = _device!.Physical.GraphicsFamilyIndex,
        };
        VulkanHelpers.Throw(_vk.CreateCommandPool(_device.Handle, &poolInfo, null, out CommandPool pool), "vkCreateCommandPool");

        CommandBufferAllocateInfo allocateInfo = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = pool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1,
        };
        VulkanHelpers.Throw(_vk.AllocateCommandBuffers(_device.Handle, &allocateInfo, out CommandBuffer commandBuffer), "vkAllocateCommandBuffers");

        SemaphoreCreateInfo semaphoreInfo = new() { SType = StructureType.SemaphoreCreateInfo };
        VulkanHelpers.Throw(_vk.CreateSemaphore(_device.Handle, &semaphoreInfo, null, out VkSemaphore imageAvailable), "vkCreateSemaphore (imageAvailable)");

        FenceCreateInfo fenceInfo = new() { SType = StructureType.FenceCreateInfo, Flags = FenceCreateFlags.SignaledBit };
        VulkanHelpers.Throw(_vk.CreateFence(_device.Handle, &fenceInfo, null, out Fence fence), "vkCreateFence");

        return new Frame(pool, commandBuffer, imageAvailable, fence);
    }

    private void EnsureRenderFinishedSemaphores()
    {
        if (_renderFinishedSemaphores.Count == _swapchain!.ImageCount && _swapchainGeneration == _swapchain.Generation)
            return;
        _device!.WaitIdle();
        foreach (VkSemaphore semaphore in _renderFinishedSemaphores)
            _vk.DestroySemaphore(_device.Handle, semaphore, null);
        _renderFinishedSemaphores.Clear();
        SemaphoreCreateInfo semaphoreInfo = new() { SType = StructureType.SemaphoreCreateInfo };
        for (uint i = 0; i < _swapchain.ImageCount; i++)
        {
            VulkanHelpers.Throw(_vk.CreateSemaphore(_device.Handle, &semaphoreInfo, null, out VkSemaphore renderFinished), "vkCreateSemaphore (renderFinished)");
            _renderFinishedSemaphores.Add(renderFinished);
        }
        _swapchainGeneration = _swapchain.Generation;
    }

    private FeatureInfo BuildFeatures()
    {
        IReadOnlyList<string> extensions = _physical!.GetDeviceExtensions();
        bool hasExtension(string name) => extensions.Contains(name);
        return new FeatureInfo
        {
            DeviceName = _physical.DeviceName,
            VendorName = VulkanHelpers.GetVendorName(_physical.Properties.VendorID),
            ApiVersion = VulkanHelpers.DecodeVersion(_physical.Properties.ApiVersion),
            DedicatedVideoMemory = _physical.GetDedicatedMemory(),
            SupportsCompute = true,
            SupportsRayTracing = hasExtension(VulkanHelpers.ExtensionAccelerationStructure)
                && hasExtension(VulkanHelpers.ExtensionRayTracingPipeline)
                && hasExtension(VulkanHelpers.ExtensionRayQuery),
            SupportsMeshShaders = hasExtension(VulkanHelpers.ExtensionMeshShader),
            Extensions = extensions,
        };
    }

    public FrameStatus BeginFrame()
    {
        EnsureInitialized();
        if (_swapchain!.IsSuspended || _swapchain.Extent.Width == 0 || _swapchain.Extent.Height == 0)
            return FrameStatus.Skipped;
        Frame frame = _frames[(int)_frameIndex];
        fixed (Fence* fencePointer = &frame.InFlight)
        {
            VulkanHelpers.Throw(
                _vk.WaitForFences(_device!.Handle, 1, fencePointer, new Bool32(true), ulong.MaxValue),
                "vkWaitForFences (frame)");
        }

        VulkanSwapchain.AcquireResult acquire = _swapchain!.AcquireImage(frame.ImageAvailable);
        if (acquire.OutOfDate)
        {
            _swapchain.EnsureValid();
            EnsureRenderFinishedSemaphores();
            acquire = _swapchain.AcquireImage(frame.ImageAvailable);
        }
        EnsureRenderFinishedSemaphores();
        _currentImageIndex = acquire.ImageIndex;
        _vk.ResetCommandPool(_device.Handle, frame.CommandPool, CommandPoolResetFlags.None);
        frame.Recorded = false;
        _currentFrame = frame;
        _frameCommandBuffer = frame.EnsureCommandBuffer(_vk, _device);
        _frameCommandBuffer.ResetState();
        return FrameStatus.Ready;
    }

    public void ClearFramebuffer(ICommandBuffer commandBuffer, in Vector4 color)
    {
        EnsureInitialized();
        if (_currentFrame is null)
            throw new InvalidOperationException("BeginFrame must be called before ClearFramebuffer.");
        if (commandBuffer is not VulkanCommandBuffer vkCommandBuffer)
            throw new ArgumentException("Command buffer does not belong to the Vulkan backend.", nameof(commandBuffer));
        ITexture renderTarget = _swapchain!.GetRenderTarget(_currentImageIndex);
        ImageMemoryBarrier toGeneral = new()
        {
            SType = StructureType.ImageMemoryBarrier,
            SrcAccessMask = 0,
            DstAccessMask = AccessFlags.TransferWriteBit,
            OldLayout = VkImageLayout.Undefined,
            NewLayout = VkImageLayout.General,
            SrcQueueFamilyIndex = uint.MaxValue,
            DstQueueFamilyIndex = uint.MaxValue,
            Image = ((IVulkanTexture)renderTarget).Handle,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
        };
        _vk.CmdPipelineBarrier(
            vkCommandBuffer.Handle,
            PipelineStageFlags.TopOfPipeBit,
            PipelineStageFlags.TransferBit,
            DependencyFlags.None,
            0, null, 0, null, 1, &toGeneral);

        ClearColorValue clearValue = new();
        clearValue.Float32_0 = color.X;
        clearValue.Float32_1 = color.Y;
        clearValue.Float32_2 = color.Z;
        clearValue.Float32_3 = color.W;
        ImageSubresourceRange clearRange = new()
        {
            AspectMask = ImageAspectFlags.ColorBit,
            BaseMipLevel = 0,
            LevelCount = 1,
            BaseArrayLayer = 0,
            LayerCount = 1,
        };
        _vk.CmdClearColorImage(vkCommandBuffer.Handle, ((IVulkanTexture)renderTarget).Handle, VkImageLayout.General, &clearValue, 1, &clearRange);
        vkCommandBuffer.MarkRecorded();

        ImageMemoryBarrier barrier = new()
        {
            SType = StructureType.ImageMemoryBarrier,
            SrcAccessMask = AccessFlags.TransferWriteBit,
            DstAccessMask = 0,
            OldLayout = VkImageLayout.General,
            NewLayout = VkImageLayout.ColorAttachmentOptimal,
            SrcQueueFamilyIndex = uint.MaxValue,
            DstQueueFamilyIndex = uint.MaxValue,
            Image = ((IVulkanTexture)renderTarget).Handle,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
        };
        _vk.CmdPipelineBarrier(
            vkCommandBuffer.Handle,
            PipelineStageFlags.TransferBit,
            PipelineStageFlags.BottomOfPipeBit,
            DependencyFlags.None,
            0, null, 0, null, 1, &barrier);
    }

    public void EndFrame(ICommandBuffer? commandBuffer)
    {
        EnsureInitialized();
        Frame frame = _currentFrame ?? throw new InvalidOperationException("BeginFrame must be called before EndFrame.");

        if (commandBuffer is not null && !ReferenceEquals(commandBuffer, _frameCommandBuffer))
            throw new ArgumentException("Command buffer does not belong to the current frame.", nameof(commandBuffer));
        VulkanCommandBuffer vkCommandBuffer = _frameCommandBuffer!;
        if (commandBuffer is null || !vkCommandBuffer.HasRecorded)
        {
            if (!vkCommandBuffer.IsRecording)
                vkCommandBuffer.Begin();
            ITexture renderTarget = _swapchain!.GetRenderTarget(_currentImageIndex);
            ImageMemoryBarrier toPresent = new()
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = VkImageLayout.Undefined,
                NewLayout = VkImageLayout.PresentSrcKhr,
                SrcQueueFamilyIndex = uint.MaxValue,
                DstQueueFamilyIndex = uint.MaxValue,
                Image = ((IVulkanTexture)renderTarget).Handle,
                SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
            };
            _vk.CmdPipelineBarrier(vkCommandBuffer.Handle,
                PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.BottomOfPipeBit,
                DependencyFlags.None, 0, null, 0, null, 1, &toPresent);
            vkCommandBuffer.MarkRecorded();
        }

        {
            if (vkCommandBuffer.IsRecording)
                VulkanHelpers.Throw(_vk.EndCommandBuffer(vkCommandBuffer.Handle), "vkEndCommandBuffer (frame)");
            VkSemaphore waitSemaphore = frame.ImageAvailable;
            VkSemaphore renderFinished = _renderFinishedSemaphores[(int)_currentImageIndex];
            VkSemaphore signalSemaphore = renderFinished;
            PipelineStageFlags waitStage = PipelineStageFlags.AllCommandsBit;
            CommandBuffer commandBufferHandle = vkCommandBuffer.Handle;
            SubmitInfo submitInfo = new()
            {
                SType = StructureType.SubmitInfo,
                WaitSemaphoreCount = 1,
                PWaitSemaphores = &waitSemaphore,
                PWaitDstStageMask = &waitStage,
                CommandBufferCount = 1,
                PCommandBuffers = &commandBufferHandle,
                SignalSemaphoreCount = 1,
                PSignalSemaphores = &signalSemaphore,
            };
            fixed (Fence* fencePointer = &frame.InFlight)
                VulkanHelpers.Throw(_vk.ResetFences(_device!.Handle, 1, fencePointer), "vkResetFences (frame)");
            VulkanHelpers.Throw(
                _vk.QueueSubmit(_device!.GraphicsQueue, 1, &submitInfo, frame.InFlight),
                "vkQueueSubmit");
            _swapchain!.Present(_device!.PresentQueue, renderFinished, _currentImageIndex);
        }

        _frameIndex = (_frameIndex + 1) % (uint)_frames.Count;
        _currentFrame = null;
        _frameCommandBuffer = null;
    }

    public ICommandBuffer GetFrameCommandBuffer() => _frameCommandBuffer!;

    public bool VSync
    {
        get => _swapchain!.VSync;
        set
        {
            if (_swapchain!.VSync == value)
                return;
            _swapchain.VSync = value;
            _swapchain.RequestRecreation();
        }
    }

    public byte[]? ReadSwapchainPixels() =>
        _swapchain?.ReadPixels(_currentImageIndex, _readback!);

    public byte[]? ReadAllSwapchainPixels() =>
        _swapchain?.ReadAllPixels(_readback!);

    public uint SwapchainImageCount => _swapchain?.ImageCount ?? 0;

    public byte[]? ReadTexturePixels(ITexture texture, Engine.RHI.ImageLayout currentLayout) =>
        texture is IVulkanTexture vulkanTexture && _readback is not null
            ? _readback.ReadTexturePixels(
                vulkanTexture,
                VulkanHelpers.ToVulkanLayout(currentLayout),
                texture.Width * texture.Height * (ulong)VulkanHelpers.FormatBytesPerPixel(texture.Format))
            : null;

    public void WaitIdle() => _device?.WaitIdle();

    private void EnsureInitialized()
    {
        if (!_initialized)
            throw new InvalidOperationException("Vulkan RHI is not initialized.");
    }

    public void Dispose()
    {
        if (!_initialized)
            return;
        _initialized = false;
        _vk.DeviceWaitIdle(_device!.Handle);

        foreach (Frame frame in _frames)
            frame.Dispose(_vk, _device);
        _frames.Clear();
        foreach (VkSemaphore semaphore in _renderFinishedSemaphores)
            _vk.DestroySemaphore(_device.Handle, semaphore, null);
        _renderFinishedSemaphores.Clear();

        _deviceAdapter?.Dispose();
        _deviceAdapter = null;
        _readback?.Dispose();
        _uploader?.Dispose();
        _descriptorAllocator?.Dispose();
        _swapchain?.Dispose();
        _swapchain = null;
        _memory?.Dispose();
        _device.Dispose();
        _device = null;
        _khrSurface!.DestroySurface(_instance!.Handle, _surface, null);
        _instance.Dispose();
        _instance = null;
        _physical = null;
    }

    private sealed class Frame(
        CommandPool commandPool,
        CommandBuffer commandBuffer,
        VkSemaphore imageAvailable,
        Fence fence)
    {
        public CommandPool CommandPool = commandPool;
        public VkSemaphore ImageAvailable = imageAvailable;
        public Fence InFlight = fence;
        public bool Recorded;
        private CommandBuffer _commandBuffer = commandBuffer;
        private VulkanCommandBuffer? _wrapper;

        public VulkanCommandBuffer EnsureCommandBuffer(Vk vk, VulkanDevice device) =>
            _wrapper ??= new VulkanCommandBuffer(vk, device, _commandBuffer);

        public void Dispose(Vk vk, VulkanDevice device)
        {
            vk.DestroyCommandPool(device.Handle, CommandPool, null);
            vk.DestroySemaphore(device.Handle, ImageAvailable, null);
            vk.DestroyFence(device.Handle, InFlight, null);
        }
    }
}
