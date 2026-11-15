using Engine.Mathematics;

namespace Engine.RHI;

public interface ICommandBuffer : IDisposable
{
    void Begin();

    void End();

    void BeginRendering(in RenderPassInfo info);

    void EndRendering();

    void SetViewport(in Viewport viewport);

    void SetScissor(in Rect scissor);

    void BindGraphicsPipeline(IGraphicsPipeline pipeline);

    void BindComputePipeline(IComputePipeline pipeline);

    void BindDescriptorSet(IDescriptorSet set, uint setIndex, ReadOnlySpan<uint> dynamicOffsets = default);

    void BindVertexBuffer(uint slot, IBuffer buffer, ulong offset = 0);

    void BindIndexBuffer(IBuffer buffer, IndexType indexType);

    void PushConstants(ReadOnlySpan<byte> data, ShaderStages stages, uint offset = 0);

    void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0);

    void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int vertexOffset = 0, uint firstInstance = 0);

    void Dispatch(uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1);

    void CopyBufferToBuffer(IBuffer source, IBuffer destination, ulong size, ulong sourceOffset = 0, ulong destinationOffset = 0);

    void CopyBufferToImage(IBuffer source, ITexture destination, ulong sourceOffset = 0);

    void ImageBarrier(ITexture texture, ImageLayout oldLayout, ImageLayout newLayout);

    void ImageBarrier(
        ITexture texture,
        ImageLayout oldLayout,
        ImageLayout newLayout,
        PipelineStages sourceStages,
        ResourceAccess sourceAccess,
        PipelineStages destinationStages,
        ResourceAccess destinationAccess,
        TextureSubresourceRange subresources = default);

    void CopyImage(ITexture source, ImageLayout sourceLayout, ITexture destination, ImageLayout destinationLayout);

    void BufferBarrier(IBuffer buffer, ulong size, ulong offset = 0);

    void BufferBarrier(
        IBuffer buffer,
        ulong size,
        PipelineStages sourceStages,
        ResourceAccess sourceAccess,
        PipelineStages destinationStages,
        ResourceAccess destinationAccess,
        ulong offset = 0);
}

public readonly record struct ColorAttachmentInfo
{
    public required ITexture Texture { get; init; }
    public AttachmentLoadOp LoadOp { get; init; }
    public AttachmentStoreOp StoreOp { get; init; }
    public Vector4 ClearColor { get; init; }
}

public readonly record struct DepthAttachmentInfo
{
    public required ITexture Texture { get; init; }
    public AttachmentLoadOp LoadOp { get; init; }
    public AttachmentStoreOp StoreOp { get; init; }
    public float ClearDepth { get; init; }
}

public readonly record struct RenderPassInfo
{
    public required ColorAttachmentInfo[] ColorAttachments { get; init; }
    public DepthAttachmentInfo? DepthAttachment { get; init; }
    public required Rect RenderArea { get; init; }
}
