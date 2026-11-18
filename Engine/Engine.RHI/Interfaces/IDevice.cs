namespace Engine.RHI;

public interface IDevice : IDisposable
{
    string Name { get; }

    FeatureInfo Features { get; }

    IMemoryAllocator MemoryAllocator { get; }

    IBuffer CreateBuffer(in BufferDescription description);

    ITexture CreateTexture(in TextureDescription description);

    ITextureView CreateTextureView(ITexture texture, TextureViewType type);

    ITextureView CreateTextureView(ITexture texture, in TextureViewDescription description);

    ISampler CreateSampler(in SamplerDescription description);

    IShaderProgram CreateShaderProgram(in ShaderProgramDescription description);

    IGraphicsPipeline CreateGraphicsPipeline(in GraphicsPipelineDescription description);

    IComputePipeline CreateComputePipeline(in ComputePipelineDescription description);

    AccelerationStructureBuildSizes GetAccelerationStructureBuildSizes(AccelerationStructureBuildDescription description) =>
        throw new NotSupportedException("Acceleration structures are unavailable.");

    IAccelerationStructure CreateAccelerationStructure(AccelerationStructureType type, ulong size) =>
        throw new NotSupportedException("Acceleration structures are unavailable.");

    IRayTracingPipeline CreateRayTracingPipeline(RayTracingPipelineDescription description) =>
        throw new NotSupportedException("Ray tracing pipelines are unavailable.");

    ICommandBuffer CreateCommandBuffer();

    void UploadBufferData(IBuffer buffer, ReadOnlySpan<byte> data, ulong bufferOffset = 0);

    void UploadTextureData(ITexture texture, ReadOnlySpan<byte> data);

    void UploadTextureSubresources(ITexture texture, ReadOnlySpan<TextureSubresourceData> subresources);

    void FlushUploads();

    void Submit(ICommandBuffer commandBuffer);

    void WaitIdle();
}
