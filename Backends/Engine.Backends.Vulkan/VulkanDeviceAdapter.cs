using Engine.Core;
using Engine.RHI;
using Silk.NET.Core;
using Silk.NET.Vulkan;

namespace Engine.Backends.Vulkan;

internal unsafe sealed class VulkanDeviceAdapter : IDevice
{
    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private readonly VulkanPhysicalDevice _physical;
    private readonly VulkanMemoryManager _memory;
    private readonly VulkanStagingUploader _uploader;
    private readonly VulkanDescriptorAllocator _descriptorAllocator;
    private readonly VulkanPipelineCache _pipelineCache;
    private readonly object _submissionLock = new();
    private readonly HashSet<VulkanCommandBuffer> _ownedCommandBuffers = [];
    private readonly HashSet<VulkanCommandBuffer> _abandonedCommandBuffers = [];
    private CommandPool? _generalCommandPool;
    private Fence? _submitFence;
    private bool _unusable;
    private bool _disposed;

    public VulkanDeviceAdapter(
        Vk vk,
        VulkanDevice device,
        VulkanPhysicalDevice physical,
        VulkanMemoryManager memory,
        VulkanStagingUploader uploader,
        VulkanDescriptorAllocator descriptorAllocator,
        FeatureInfo features)
    {
        _vk = vk;
        _device = device;
        _physical = physical;
        _memory = memory;
        _uploader = uploader;
        _descriptorAllocator = descriptorAllocator;
        Features = features;
        _pipelineCache = new VulkanPipelineCache(vk, device, physical);
    }

    public string Name => _physical.DeviceName;

    public FeatureInfo Features { get; }

    public IMemoryAllocator MemoryAllocator => _memory;

    public IBuffer CreateBuffer(in BufferDescription description) =>
        new VulkanBuffer(_vk, _device, _memory, description, $"Buffer {description.Size}B");

    public ITexture CreateTexture(in TextureDescription description) =>
        new VulkanTexture(_vk, _device, _memory, description, $"Texture {description.Extent.Width}x{description.Extent.Height}");

    public ITextureView CreateTextureView(ITexture texture, TextureViewType type) =>
        CreateTextureView(texture, new TextureViewDescription { Type = type });

    public ITextureView CreateTextureView(ITexture texture, in TextureViewDescription description) => texture is VulkanTexture vkTexture
        ? vkTexture.CreateView(description)
        : throw new ArgumentException("Texture does not belong to the Vulkan backend.", nameof(texture));

    public ISampler CreateSampler(in SamplerDescription description) =>
        new VulkanSampler(_vk, _device, description);

    public IShaderProgram CreateShaderProgram(in ShaderProgramDescription description) =>
        new VulkanShaderProgram(_vk, _device, _descriptorAllocator, description);

    public IGraphicsPipeline CreateGraphicsPipeline(in GraphicsPipelineDescription description)
    {
        if (description.ShaderProgram is not VulkanShaderProgram program)
            throw new ArgumentException("Shader program does not belong to the Vulkan backend.", nameof(description));
        return new VulkanGraphicsPipeline(_vk, _device, program, _pipelineCache.Handle, description);
    }

    public IComputePipeline CreateComputePipeline(in ComputePipelineDescription description)
    {
        if (description.ShaderProgram is not VulkanShaderProgram program || !program.BelongsTo(_device))
            throw new ArgumentException("Shader program does not belong to this Vulkan device.", nameof(description));
        return new VulkanComputePipeline(_vk, _device, program, _pipelineCache.Handle, description);
    }

    public IRayTracingPipeline CreateRayTracingPipeline(RayTracingPipelineDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        if (!_device.SupportsRayTracing)
            throw new NotSupportedException(_device.RayTracingUnavailableReason ?? "Vulkan ray-tracing pipelines are unavailable.");
        if (description.ShaderProgram is not VulkanShaderProgram program || !program.BelongsTo(_device))
            throw new ArgumentException("Shader program does not belong to this Vulkan device.", nameof(description));
        ObjectDisposedException.ThrowIf(program.IsDisposed, program);
        return new VulkanRayTracingPipeline(_vk, _device, program, description);
    }

    public AccelerationStructureBuildSizes GetAccelerationStructureBuildSizes(AccelerationStructureBuildDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        EnsureAccelerationStructuresEnabled();
        RayTracingValidation.ValidateBuild(description, _physical.RayTracing.Limits);
        return VulkanRayTracingOperations.QueryBuildSizes(_vk, _device, description);
    }

    public IAccelerationStructure CreateAccelerationStructure(AccelerationStructureType type, ulong size)
    {
        EnsureAccelerationStructuresEnabled();
        if (!Enum.IsDefined(type))
            throw new ArgumentOutOfRangeException(nameof(type));
        return new VulkanAccelerationStructure(_vk, _device, _memory, type, size);
    }

    private void EnsureAccelerationStructuresEnabled()
    {
        if (!_device.SupportsAccelerationStructures)
            throw new NotSupportedException(_device.Physical.RayTracing.AccelerationStructuresUnavailableReason
                ?? "Vulkan acceleration structures are unavailable.");
    }

    public ICommandBuffer CreateCommandBuffer()
    {
        lock (_submissionLock)
        {
            EnsureUsable();
            EnsureCommandPool();
            CommandBufferAllocateInfo allocateInfo = new()
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = _generalCommandPool!.Value,
                Level = CommandBufferLevel.Primary,
                CommandBufferCount = 1,
            };
            VulkanHelpers.Throw(
                _vk.AllocateCommandBuffers(_device.Handle, &allocateInfo, out CommandBuffer commandBuffer),
                "vkAllocateCommandBuffers");
            VulkanCommandBuffer? wrapper = null;
            wrapper = new VulkanCommandBuffer(
                _vk,
                _device,
                commandBuffer,
                release: handle => FreeGeneralCommandBuffer(wrapper!, handle),
                owner: this);
            _ownedCommandBuffers.Add(wrapper);
            return wrapper;
        }
    }

    private void EnsureCommandPool()
    {
        if (_generalCommandPool is not null)
            return;
        CommandPoolCreateInfo poolInfo = new()
        {
            SType = StructureType.CommandPoolCreateInfo,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit | CommandPoolCreateFlags.TransientBit,
            QueueFamilyIndex = _physical.GraphicsFamilyIndex,
        };
        VulkanHelpers.Throw(_vk.CreateCommandPool(_device.Handle, &poolInfo, null, out CommandPool pool), "vkCreateCommandPool");
        _generalCommandPool = pool;
        try
        {
            CreateSubmitFence();
        }
        catch
        {
            _vk.DestroyCommandPool(_device.Handle, pool, null);
            _generalCommandPool = null;
            throw;
        }
    }

    private void CreateSubmitFence()
    {
        FenceCreateInfo fenceInfo = new() { SType = StructureType.FenceCreateInfo };
        VulkanHelpers.Throw(_vk.CreateFence(_device.Handle, &fenceInfo, null, out Fence fence), "vkCreateFence (device submit)");
        _submitFence = fence;
    }

    private void FreeGeneralCommandBuffer(VulkanCommandBuffer wrapper, CommandBuffer commandBuffer)
    {
        lock (_submissionLock)
        {
            _ownedCommandBuffers.Remove(wrapper);
            _abandonedCommandBuffers.Remove(wrapper);
            if (_generalCommandPool is null || _disposed)
                return;
            _vk.FreeCommandBuffers(_device.Handle, _generalCommandPool.Value, 1, &commandBuffer);
        }
    }

    private void EnsureUsable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_unusable)
            throw new InvalidOperationException("Vulkan device adapter is unusable after an unconfirmed queue submission failure.");
    }

    public void UploadBufferData(IBuffer buffer, ReadOnlySpan<byte> data, ulong bufferOffset = 0)
    {
        _uploader.UploadBuffer(buffer, data, bufferOffset);
        _uploader.Execute();
    }

    public void UploadTextureData(ITexture texture, ReadOnlySpan<byte> data)
    {
        TextureSubresourceData subresource = new()
        {
            MipLevel = 0,
            ArrayLayer = 0,
            Data = data.ToArray(),
        };
        UploadTextureSubresources(texture, [subresource]);
    }

    public void UploadTextureSubresources(ITexture texture, ReadOnlySpan<TextureSubresourceData> subresources)
    {
        TextureUploadValidation.ValidateSubresources(texture, subresources);
        foreach (TextureSubresourceData subresource in subresources)
            _uploader.UploadTextureSubresource(texture, subresource);
        _uploader.Execute();
    }

    public void FlushUploads() => _uploader.Execute();

    public void Submit(ICommandBuffer commandBuffer)
    {
        if (commandBuffer is not VulkanCommandBuffer vkCommandBuffer)
            throw new ArgumentException("Command buffer does not belong to the Vulkan backend.", nameof(commandBuffer));
        lock (_submissionLock)
        {
            EnsureUsable();
            if (!_ownedCommandBuffers.Contains(vkCommandBuffer))
                throw new ArgumentException("Command buffer was not created by this Vulkan device adapter.", nameof(commandBuffer));
            EnsureCommandPool();
            vkCommandBuffer.MarkSubmitted(this);
            CommandBuffer handle = vkCommandBuffer.Handle;
            SubmitInfo submitInfo = new()
            {
                SType = StructureType.SubmitInfo,
                CommandBufferCount = 1,
                PCommandBuffers = &handle,
            };
            Result submitResult;
            lock (_device.QueueLock)
                submitResult = _vk.QueueSubmit(_device.GraphicsQueue, 1, &submitInfo, _submitFence!.Value);
            if (submitResult != Result.Success)
            {
                VulkanException submitFailure = new(submitResult, "vkQueueSubmit (device)");
                Result submitIdleResult = _device.WaitIdle();
                if (submitIdleResult == Result.Success)
                {
                    Exception? cleanupFailure = null;
                    try
                    {
                        vkCommandBuffer.AbortUnsubmittedSubmit();
                    }
                    catch (Exception exception)
                    {
                        cleanupFailure = exception;
                    }
                    try
                    {
                        RecoverSubmitFence();
                    }
                    catch (Exception exception)
                    {
                        cleanupFailure ??= exception;
                    }
                    if (cleanupFailure is not null)
                        throw new AggregateException(submitFailure, cleanupFailure);
                    throw submitFailure;
                }

                vkCommandBuffer.AbandonAfterSubmit();
                _abandonedCommandBuffers.Add(vkCommandBuffer);
                _unusable = true;
                throw new AggregateException(
                    submitFailure,
                    new VulkanException(submitIdleResult, "vkDeviceWaitIdle after device submit failure"));
            }

            vkCommandBuffer.MarkQueueAccepted();
            Fence submitFence = _submitFence!.Value;
            Fence* fencePointer = &submitFence;
            Result waitResult = _vk.WaitForFences(_device.Handle, 1, fencePointer, new Bool32(true), ulong.MaxValue);
            if (waitResult == Result.Success)
            {
                Exception? completionFailure = null;
                try
                {
                    vkCommandBuffer.ReleaseAfterSubmit();
                }
                catch (Exception exception)
                {
                    completionFailure = exception;
                }
                Result resetResult = _vk.ResetFences(_device.Handle, 1, fencePointer);
                if (resetResult != Result.Success)
                {
                    try
                    {
                        RecoverSubmitFence();
                    }
                    catch (Exception exception)
                    {
                        completionFailure ??= exception;
                    }
                    completionFailure ??= new VulkanException(resetResult, "vkResetFences (device submit)");
                }
                if (completionFailure is not null)
                    throw completionFailure;
                return;
            }

            VulkanException waitFailure = new(waitResult, "vkWaitForFences (device submit)");
            Result idleResult = _device.WaitIdle();
            if (idleResult == Result.Success)
            {
                Exception? cleanupFailure = null;
                try
                {
                    vkCommandBuffer.ReleaseAfterSubmit();
                }
                catch (Exception exception)
                {
                    cleanupFailure = exception;
                }
                try
                {
                    RecoverSubmitFence();
                }
                catch (Exception exception)
                {
                    cleanupFailure ??= exception;
                }
                if (cleanupFailure is not null)
                    throw new AggregateException(waitFailure, cleanupFailure);
            }
            else
            {
                vkCommandBuffer.AbandonAfterSubmit();
                _abandonedCommandBuffers.Add(vkCommandBuffer);
                _unusable = true;
                throw new AggregateException(
                    waitFailure,
                    new VulkanException(idleResult, "vkDeviceWaitIdle after device fence wait failure"));
            }
            throw waitFailure;
        }
    }

    private void RecoverSubmitFence()
    {
        if (_submitFence is not null)
        {
            _vk.DestroyFence(_device.Handle, _submitFence.Value, null);
            _submitFence = null;
        }
        try
        {
            CreateSubmitFence();
        }
        catch
        {
            _unusable = true;
            throw;
        }
    }

    internal void EnsureCanBeginFrame()
    {
        lock (_submissionLock)
            EnsureUsable();
    }

    internal void RetireAfterDeviceIdle()
    {
        lock (_submissionLock)
        {
            foreach (VulkanCommandBuffer commandBuffer in _ownedCommandBuffers.ToArray())
                commandBuffer.ReleaseAfterDeviceIdle();
            _abandonedCommandBuffers.Clear();
        }
    }

    public void WaitIdle()
    {
        lock (_submissionLock)
        {
            EnsureUsable();
            VulkanHelpers.Throw(_device.WaitIdle(), "vkDeviceWaitIdle");
            foreach (VulkanCommandBuffer commandBuffer in _abandonedCommandBuffers.ToArray())
                commandBuffer.ReleaseAfterDeviceIdle();
            _abandonedCommandBuffers.Clear();
        }
    }

    public void Dispose()
    {
        lock (_submissionLock)
        {
            if (_disposed)
                return;
            Result idleResult = _device.WaitIdle();
            if (idleResult != Result.Success)
            {
                _unusable = true;
                throw new VulkanException(idleResult, "vkDeviceWaitIdle (device adapter dispose)");
            }
            foreach (VulkanCommandBuffer commandBuffer in _ownedCommandBuffers.ToArray())
                commandBuffer.ReleaseAfterDeviceIdle();
            _abandonedCommandBuffers.Clear();
            _ownedCommandBuffers.Clear();
            if (_generalCommandPool is not null)
            {
                _vk.DestroyCommandPool(_device.Handle, _generalCommandPool.Value, null);
                _generalCommandPool = null;
            }
            if (_submitFence is not null)
            {
                _vk.DestroyFence(_device.Handle, _submitFence.Value, null);
                _submitFence = null;
            }
            _pipelineCache.Dispose();
            _disposed = true;
        }
    }
}
