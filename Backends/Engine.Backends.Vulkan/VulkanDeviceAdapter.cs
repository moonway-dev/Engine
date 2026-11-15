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
    private CommandPool? _generalCommandPool;
    private Fence? _submitFence;

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
        if (description.ShaderProgram is not VulkanShaderProgram program)
            throw new ArgumentException("Shader program does not belong to the Vulkan backend.", nameof(description));
        return new VulkanComputePipeline(_vk, _device, program, _pipelineCache.Handle, description);
    }

    public ICommandBuffer CreateCommandBuffer()
    {
        if (_generalCommandPool is null)
        {
            CommandPoolCreateInfo poolInfo = new()
            {
                SType = StructureType.CommandPoolCreateInfo,
                Flags = CommandPoolCreateFlags.ResetCommandBufferBit | CommandPoolCreateFlags.TransientBit,
                QueueFamilyIndex = _physical.GraphicsFamilyIndex,
            };
            VulkanHelpers.Throw(
                _vk.CreateCommandPool(_device.Handle, &poolInfo, null, out CommandPool pool),
                "vkCreateCommandPool");
            _generalCommandPool = pool;
            FenceCreateInfo fenceInfo = new() { SType = StructureType.FenceCreateInfo };
            VulkanHelpers.Throw(
                _vk.CreateFence(_device.Handle, &fenceInfo, null, out Fence fence),
                "vkCreateFence (device submit)");
            _submitFence = fence;
        }

        CommandBufferAllocateInfo allocateInfo = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _generalCommandPool.Value,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1,
        };
        VulkanHelpers.Throw(
            _vk.AllocateCommandBuffers(_device.Handle, &allocateInfo, out CommandBuffer commandBuffer),
            "vkAllocateCommandBuffers");
        return new VulkanCommandBuffer(_vk, _device, commandBuffer);
    }

    public void UploadBufferData(IBuffer buffer, ReadOnlySpan<byte> data, ulong bufferOffset = 0)
    {
        _uploader.UploadBuffer(buffer, data, bufferOffset);
        _uploader.Execute();
    }

    public void UploadTextureData(ITexture texture, ReadOnlySpan<byte> data)
    {
        _uploader.UploadTexture(texture, data);
        _uploader.Execute();
    }

    public void FlushUploads() => _uploader.Execute();

    public void Submit(ICommandBuffer commandBuffer)
    {
        if (commandBuffer is not VulkanCommandBuffer vkCommandBuffer)
            throw new ArgumentException("Command buffer does not belong to the Vulkan backend.", nameof(commandBuffer));
        CommandBuffer handle = vkCommandBuffer.Handle;
        SubmitInfo submitInfo = new()
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &handle,
        };
        VulkanHelpers.Throw(
            _vk.QueueSubmit(_device.GraphicsQueue, 1, &submitInfo, _submitFence!.Value),
            "vkQueueSubmit (device)");
        Fence submitFence = _submitFence!.Value;
        Fence* fencePointer = &submitFence;
        _vk.WaitForFences(_device.Handle, 1, fencePointer, new Bool32(true), ulong.MaxValue);
        _vk.ResetFences(_device.Handle, 1, fencePointer);
    }

    public void WaitIdle() => _device.WaitIdle();

    public void Dispose()
    {
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
    }
}
