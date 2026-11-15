namespace Engine.RHI;

public interface IBuffer : IDisposable
{
    ulong Size { get; }

    BufferUsage Usage { get; }

    MemoryUsage Memory { get; }

    IntPtr Map();

    void Unmap();
}

public interface ITexture : IDisposable
{
    TextureFormat Format { get; }

    uint Width { get; }

    uint Height { get; }

    uint MipLevels { get; }

    uint ArrayLayers { get; }
}

public interface ITextureView : IDisposable
{
    ITexture Texture { get; }
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

    void WriteStorageImage(uint binding, ITexture texture);

    void WriteSampler(uint binding, ISampler sampler);
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

public interface IAccelerationStructure : IDisposable;
