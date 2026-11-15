namespace Engine.RHI;

public interface IDevice : IDisposable
{
    string Name { get; }

    FeatureInfo Features { get; }

    IMemoryAllocator MemoryAllocator { get; }

    IBuffer CreateBuffer(in BufferDescription description);

    ITexture CreateTexture(in TextureDescription description);

    ISampler CreateSampler(in SamplerDescription description);

    IShaderProgram CreateShaderProgram(in ShaderProgramDescription description);

    IGraphicsPipeline CreateGraphicsPipeline(in GraphicsPipelineDescription description);

    IComputePipeline CreateComputePipeline(in ComputePipelineDescription description);

    ICommandBuffer CreateCommandBuffer();

    void UploadBufferData(IBuffer buffer, ReadOnlySpan<byte> data, ulong bufferOffset = 0);

    void UploadTextureData(ITexture texture, ReadOnlySpan<byte> data);

    void FlushUploads();

    void Submit(ICommandBuffer commandBuffer);

    void WaitIdle();
}
