namespace Engine.RHI;

public interface IBuffer : IDisposable
{
    ulong Size { get; }

    BufferUsage Usage { get; }

    MemoryUsage Memory { get; }

    ulong DeviceAddress => throw new NotSupportedException("Buffer device addresses are unavailable.");

    IntPtr Map();

    void Unmap();
}

public interface ITexture : IDisposable
{
    TextureFormat Format { get; }

    uint Width { get; }

    uint Height { get; }

    uint Depth { get; }

    uint MipLevels { get; }

    uint ArrayLayers { get; }

    TextureDimension Dimension { get; }
}

public interface ITextureView : IDisposable
{
    ITexture Texture { get; }

    TextureViewType Type { get; }

    uint BaseMipLevel { get; }

    uint MipLevelCount { get; }

    uint BaseArrayLayer { get; }

    uint ArrayLayerCount { get; }
}

public interface ISampler : IDisposable;

public interface IPipeline : IDisposable;

public interface IShaderProgram : IDisposable
{
    string Name { get; }

    PipelineReflection Reflection { get; }

    IDescriptorSet CreateDescriptorSet(uint setIndex);
}

public interface IGraphicsPipeline : IPipeline
{
    IShaderProgram ShaderProgram { get; }
}

public interface IComputePipeline : IPipeline
{
    IShaderProgram ShaderProgram { get; }
}

public interface IDescriptorSet : IDisposable
{
    IShaderProgram Program { get; }

    uint SetIndex { get; }

    void WriteUniformBuffer(uint binding, IBuffer buffer, ulong offset = 0, ulong range = 0);

    void WriteStorageBuffer(uint binding, IBuffer buffer, ulong offset = 0, ulong range = 0);

    void WriteTexture(uint binding, ITexture texture, ISampler? sampler = null);

    void WriteTexture(uint binding, ITextureView view, ISampler? sampler = null);

    void WriteStorageImage(uint binding, ITexture texture);

    void WriteStorageImage(uint binding, ITextureView view);

    void WriteSampler(uint binding, ISampler sampler);

    void WriteAccelerationStructure(uint binding, IAccelerationStructure accelerationStructure) =>
        throw new NotSupportedException("Acceleration-structure descriptors are unavailable.");

    void WriteTextureArray(uint binding, IReadOnlyList<ITexture> textures) =>
        throw new NotSupportedException("Texture array descriptors are unavailable.");
}

public interface IRenderPass : IDisposable;

public interface IFramebuffer : IDisposable;

public interface IFence : IDisposable
{
    void Reset();

    bool Wait(ulong timeoutNanoseconds);

    bool IsSignaled { get; }
}

public interface ISemaphore : IDisposable;

public interface IMemoryAllocator : IDisposable
{
    ulong UsedMemory { get; }

    ulong AllocatedMemory { get; }
}

public interface IAccelerationStructure : IDisposable
{
    AccelerationStructureType Type { get; }
    ulong Size { get; }
    ulong DeviceAddress { get; }
    bool IsBuilt { get; }
}

public interface IRayTracingPipeline : IPipeline
{
    IShaderProgram ShaderProgram { get; }
    RayTracingPipelineDescription Description { get; }
    byte[] GetShaderGroupHandles();
}
