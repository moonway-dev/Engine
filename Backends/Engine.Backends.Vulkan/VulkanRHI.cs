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
    private bool _unusable;
    private VulkanDeviceAdapter? _deviceAdapter;
    private GpuFrameTiming? _latestGpuFrameTiming;
    private long _submittedFrameNumber;

    internal const uint TimestampQueryCapacity = 512;

    public string Name => "Vulkan Renderer";

    public GraphicsBackend Backend => GraphicsBackend.Vulkan;

    public IDevice Device => _deviceAdapter ?? throw new InvalidOperationException("Vulkan RHI is not initialized.");

    public ISwapchain Swapchain => _swapchain ?? throw new InvalidOperationException("Vulkan RHI is not initialized.");

    public uint CurrentImageIndex => _currentImageIndex;

    public uint CurrentFrameSlot => _frameIndex;

    public uint FramesInFlight => (uint)_frames.Count;

    public bool SupportsGpuTimestamps => _physical is { GraphicsTimestampValidBits: > 0 };

    public GpuFrameTiming? LatestGpuFrameTiming => _latestGpuFrameTiming;

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

        _physical = VulkanPhysicalDevice.Select(
            _vk, _instance.Handle, _khrSurface, _surface, configuration.PreferredDeviceIndex, configuration.RayTracing, _log);
        _device = new VulkanDevice(_vk, _instance.Handle, _physical, configuration.RayTracing, _log);
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

        QueryPool queryPool = default;
        if (_physical!.GraphicsTimestampValidBits > 0)
        {
            QueryPoolCreateInfo queryPoolInfo = new()
            {
                SType = StructureType.QueryPoolCreateInfo,
                QueryType = QueryType.Timestamp,
                QueryCount = TimestampQueryCapacity,
            };
            VulkanHelpers.Throw(_vk.CreateQueryPool(_device.Handle, &queryPoolInfo, null, out queryPool), "vkCreateQueryPool (timestamps)");
        }

        SemaphoreCreateInfo semaphoreInfo = new() { SType = StructureType.SemaphoreCreateInfo };
        VulkanHelpers.Throw(_vk.CreateSemaphore(_device.Handle, &semaphoreInfo, null, out VkSemaphore imageAvailable), "vkCreateSemaphore (imageAvailable)");

        FenceCreateInfo fenceInfo = new() { SType = StructureType.FenceCreateInfo, Flags = FenceCreateFlags.SignaledBit };
        VulkanHelpers.Throw(_vk.CreateFence(_device.Handle, &fenceInfo, null, out Fence fence), "vkCreateFence");

        return new Frame(pool, commandBuffer, imageAvailable, fence, queryPool);
    }

    private void EnsureRenderFinishedSemaphores()
    {
        if (_renderFinishedSemaphores.Count == _swapchain!.ImageCount && _swapchainGeneration == _swapchain.Generation)
            return;
        VulkanHelpers.Throw(_device!.WaitIdle(), "vkDeviceWaitIdle (swapchain semaphore refresh)");
        RetireAfterConfirmedDeviceIdle("readback retirement after swapchain semaphore idle");
        _deviceAdapter?.RetireAfterDeviceIdle();
        foreach (Frame frame in _frames)
            frame.RetireAfterDeviceIdle();
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
        VulkanPhysicalDevice physical = _physical!;
        VulkanDevice device = _device!;
        IReadOnlyList<string> extensions = device.EnabledExtensions;
        bool supportsRayTracing = device.SupportsRayTracing;
        return new FeatureInfo
        {
            DeviceName = physical.DeviceName,
            VendorName = VulkanHelpers.GetVendorName(physical.Properties.VendorID),
            ApiVersion = VulkanHelpers.DecodeVersion(physical.Properties.ApiVersion),
            DedicatedVideoMemory = physical.GetDedicatedMemory(),
            SupportsCompute = device.SupportsCompute,
            SupportsRayTracing = supportsRayTracing,
            SupportsBufferDeviceAddress = device.SupportsBufferDeviceAddress,
            SupportsAccelerationStructures = device.SupportsAccelerationStructures,
            SupportsRayQuery = device.SupportsRayQuery,
            SupportsNonUniformSampledImageIndexing = supportsRayTracing && physical.RayTracing.NonUniformSampledImages,
            MaxPerStageSampledImages = physical.Properties.Limits.MaxPerStageDescriptorSampledImages,
            MaxDescriptorSetSampledImages = physical.Properties.Limits.MaxDescriptorSetSampledImages,
            RayTracing = supportsRayTracing ? physical.RayTracing.Limits : new RayTracingLimits(),
            RayTracingUnavailableReason = device.RayTracingUnavailableReason,
            SupportsMeshShaders = physical.GetDeviceExtensions().Contains(VulkanHelpers.ExtensionMeshShader),
            SupportsGpuTimestamps = physical.GraphicsTimestampValidBits > 0,
            TimestampPeriodNanoseconds = physical.Properties.Limits.TimestampPeriod,
            Extensions = extensions,
        };
    }

    public FrameStatus BeginFrame()
    {
        EnsureInitialized();
        if (_unusable)
            throw new InvalidOperationException("Vulkan RHI is unusable after an unconfirmed frame submission failure.");
        if (_currentFrame is not null)
            throw new InvalidOperationException("EndFrame must be called before beginning another frame.");
        _deviceAdapter!.EnsureCanBeginFrame();
        if (_swapchain!.IsSuspended || _swapchain.Extent.Width == 0 || _swapchain.Extent.Height == 0)
            return FrameStatus.Skipped;
        Frame frame = _frames[(int)_frameIndex];
        fixed (Fence* fencePointer = &frame.InFlight)
        {
            Result waitResult = _vk.WaitForFences(_device!.Handle, 1, fencePointer, new Bool32(true), ulong.MaxValue);
            if (waitResult != Result.Success)
            {
                _unusable = true;
                throw new VulkanException(waitResult, "vkWaitForFences (frame)");
            }
        }
        frame.EnsureCommandBuffer(_vk, _device!, frame.QueryPool, _physical!.GraphicsTimestampValidBits)
            .ReleaseResourcesAfterFence(frame.SubmissionAccepted);
        frame.SubmissionAccepted = false;

        VulkanSwapchain.AcquireResult acquire = _swapchain!.AcquireImage(frame.ImageAvailable);
        if (acquire.OutOfDate)
        {
            _swapchain.EnsureValid();
            EnsureRenderFinishedSemaphores();
            acquire = _swapchain.AcquireImage(frame.ImageAvailable);
            if (acquire.OutOfDate)
                return FrameStatus.Skipped;
        }
        EnsureRenderFinishedSemaphores();
        _currentImageIndex = acquire.ImageIndex;
        if (frame.SubmittedFrameNumber >= 0)
            HarvestGpuTimings(frame);
        VulkanHelpers.Throw(_vk.ResetCommandPool(_device.Handle, frame.CommandPool, CommandPoolResetFlags.None), "vkResetCommandPool (frame)");
        frame.Recorded = false;
        frame.TimestampNames.Clear();
        frame.TimestampCount = 0;
        frame.TimingOverflow = false;
        _currentFrame = frame;
        _frameCommandBuffer = frame.EnsureCommandBuffer(_vk, _device, frame.QueryPool, _physical!.GraphicsTimestampValidBits);
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
        if (vkCommandBuffer.HasOpenTimestamp)
            throw new InvalidOperationException("Cannot submit a frame with an active timestamp scope.");
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
                vkCommandBuffer.End();
            vkCommandBuffer.MarkFrameSubmitted();
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
            Result resetResult;
            fixed (Fence* fencePointer = &frame.InFlight)
                resetResult = _vk.ResetFences(_device!.Handle, 1, fencePointer);
            if (resetResult != Result.Success)
            {
                vkCommandBuffer.ReleaseResourcesAfterFence(wasSubmitted: false);
                _unusable = true;
                _currentFrame = null;
                _frameCommandBuffer = null;
                throw new VulkanException(resetResult, "vkResetFences (frame)");
            }
            Result submitResult;
            lock (_device!.QueueLock)
                submitResult = _vk.QueueSubmit(_device.GraphicsQueue, 1, &submitInfo, frame.InFlight);
            if (submitResult != Result.Success)
            {
                VulkanException submitFailure = new(submitResult, "vkQueueSubmit (frame)");
                Result idleResult = _device!.WaitIdle();
                if (idleResult == Result.Success)
                    vkCommandBuffer.ReleaseResourcesAfterFence(wasSubmitted: false);
                else
                {
                    vkCommandBuffer.AbandonAfterSubmit();
                    _unusable = true;
                    _currentFrame = null;
                    _frameCommandBuffer = null;
                    throw new AggregateException(submitFailure, new VulkanException(idleResult, "vkDeviceWaitIdle after frame submit failure"));
                }
                _unusable = true;
                _currentFrame = null;
                _frameCommandBuffer = null;
                throw submitFailure;
            }
            frame.SubmissionAccepted = true;
            vkCommandBuffer.MarkQueueAccepted();
            frame.SubmittedFrameNumber = _submittedFrameNumber++;
            frame.TimestampCount = vkCommandBuffer.TimestampCount;
            frame.TimestampNames.Clear();
            frame.TimestampNames.AddRange(vkCommandBuffer.TimestampNames);
            frame.TimingOverflow = vkCommandBuffer.TimestampOverflow;
            try
            {
                lock (_device.QueueLock)
                    _swapchain!.Present(_device.PresentQueue, renderFinished, _currentImageIndex);
            }
            catch
            {
                // The graphics submission remains tracked by the frame fence and will retire on reuse.
                _unusable = true;
                _currentFrame = null;
                _frameCommandBuffer = null;
                _frameIndex = (_frameIndex + 1) % (uint)_frames.Count;
                throw;
            }
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

    public void WaitIdle()
    {
        if (_device is null)
            return;
        VulkanHelpers.Throw(_device.WaitIdle(), "vkDeviceWaitIdle (RHI)");
        RetireAfterConfirmedDeviceIdle("readback retirement after RHI idle");
        _deviceAdapter?.RetireAfterDeviceIdle();
        foreach (Frame frame in _frames)
            frame.RetireAfterDeviceIdle();
    }

    private void RetireAfterConfirmedDeviceIdle(string operation)
    {
        VulkanReadback? readback = _readback;
        if (readback is null)
            return;
        try
        {
            VulkanHelpers.Throw(readback.RetireAfterConfirmedDeviceIdle(), operation);
        }
        catch
        {
            _unusable = true;
            throw;
        }
    }

    private void HarvestGpuTimings(Frame frame)
    {
        if (!SupportsGpuTimestamps)
            return;
        if (frame.TimingOverflow)
        {
            _latestGpuFrameTiming = new GpuFrameTiming(frame.SubmittedFrameNumber, [], Incomplete: true);
            return;
        }
        if (frame.TimestampCount == 0)
        {
            _latestGpuFrameTiming = new GpuFrameTiming(frame.SubmittedFrameNumber, []);
            return;
        }
        ulong[] timestamps = new ulong[frame.TimestampCount];
        Result result;
        fixed (ulong* timestampPointer = timestamps)
        {
            result = _vk.GetQueryPoolResults(
                _device!.Handle,
                frame.QueryPool,
                0,
                frame.TimestampCount,
                (nuint)(timestamps.Length * sizeof(ulong)),
                timestampPointer,
                sizeof(ulong),
                (QueryResultFlags)1);
        }
        if (result != Result.Success)
        {
            _log.Warning($"GPU timestamp results for frame {frame.SubmittedFrameNumber} were unavailable ({result}).");
            _latestGpuFrameTiming = new GpuFrameTiming(frame.SubmittedFrameNumber, [], Incomplete: true);
            return;
        }

        uint validBits = _physical!.GraphicsTimestampValidBits;
        ulong mask = validBits >= 64 ? ulong.MaxValue : (1UL << (int)validBits) - 1UL;
        double nanosecondsPerTick = _physical.Properties.Limits.TimestampPeriod;
        List<GpuPassTiming> passes = new(frame.TimestampNames.Count);
        for (int i = 0; i < frame.TimestampNames.Count; i++)
        {
            ulong start = timestamps[i * 2] & mask;
            ulong end = timestamps[i * 2 + 1] & mask;
            ulong elapsed = validBits >= 64 ? unchecked(end - start) : (end - start) & mask;
            passes.Add(new GpuPassTiming(frame.TimestampNames[i], elapsed * nanosecondsPerTick / 1_000_000.0));
        }
        _latestGpuFrameTiming = new GpuFrameTiming(frame.SubmittedFrameNumber, passes);
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
            throw new InvalidOperationException("Vulkan RHI is not initialized.");
        if (_unusable)
            throw new InvalidOperationException("Vulkan RHI is unusable after an unconfirmed queue submission failure.");
    }

    public void Dispose()
    {
        if (!_initialized)
            return;
        VulkanDevice device = _device!;
        VulkanHelpers.Throw(device.WaitIdle(), "vkDeviceWaitIdle (RHI shutdown)");
        RetireAfterConfirmedDeviceIdle("readback retirement after RHI shutdown idle");
        _deviceAdapter?.RetireAfterDeviceIdle();
        foreach (Frame frame in _frames)
            frame.RetireAfterDeviceIdle();

        _initialized = false;
        _currentFrame = null;
        _frameCommandBuffer = null;
        _deviceAdapter?.Dispose();
        _deviceAdapter = null;
        foreach (Frame frame in _frames)
            frame.Dispose(_vk, device);
        _frames.Clear();
        foreach (VkSemaphore semaphore in _renderFinishedSemaphores)
            _vk.DestroySemaphore(device.Handle, semaphore, null);
        _renderFinishedSemaphores.Clear();

        _readback?.Dispose();
        _uploader?.Dispose();
        _descriptorAllocator?.Dispose();
        _swapchain?.Dispose();
        _swapchain = null;
        _memory?.Dispose();
        device.Dispose();
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
        Fence fence,
        QueryPool queryPool)
    {
        public CommandPool CommandPool = commandPool;
        public VkSemaphore ImageAvailable = imageAvailable;
        public Fence InFlight = fence;
        public QueryPool QueryPool = queryPool;
        public bool Recorded;
        public bool SubmissionAccepted;
        public long SubmittedFrameNumber = -1;
        public uint TimestampCount;
        public bool TimingOverflow;
        public List<string> TimestampNames { get; } = [];
        private CommandBuffer _commandBuffer = commandBuffer;
        private VulkanCommandBuffer? _wrapper;

        public VulkanCommandBuffer EnsureCommandBuffer(Vk vk, VulkanDevice device, QueryPool queryPool, uint timestampValidBits) =>
            _wrapper ??= new VulkanCommandBuffer(vk, device, _commandBuffer, queryPool, timestampValidBits);

        public void ReleaseCommandBufferResources(bool wasSubmitted) =>
            _wrapper?.ReleaseResourcesAfterFence(wasSubmitted);

        public void RetireAfterDeviceIdle()
        {
            _wrapper?.ReleaseAfterDeviceIdle();
            SubmissionAccepted = false;
        }

        public void Dispose(Vk vk, VulkanDevice device)
        {
            if (QueryPool.Handle != 0)
                vk.DestroyQueryPool(device.Handle, QueryPool, null);
            vk.DestroyCommandPool(device.Handle, CommandPool, null);
            vk.DestroySemaphore(device.Handle, ImageAvailable, null);
            vk.DestroyFence(device.Handle, InFlight, null);
        }
    }
}
