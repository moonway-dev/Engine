using Engine.Mathematics;
using Engine.RHI;

namespace Engine.Backends.Null;

public sealed class NullDevice : IDevice
{
    private readonly List<NullBufferUpload> _bufferUploads = [];

    public string Name => "Null Device";

    public IReadOnlyList<NullBufferUpload> BufferUploads => _bufferUploads;

    public FeatureInfo Features { get; } = new()
    {
        DeviceName = "Null Device",
        VendorName = "Engine",
        ApiVersion = "Null 1.0",
        DedicatedVideoMemory = 0,
        SupportsCompute = false,
        SupportsRayTracing = false,
        SupportsMeshShaders = false,
    };

    public IMemoryAllocator MemoryAllocator { get; } = new NullMemoryAllocator();

    public IBuffer CreateBuffer(in BufferDescription description) => new NullBuffer(description);

    public ITexture CreateTexture(in TextureDescription description) => new NullTexture(description);

    public ITextureView CreateTextureView(ITexture texture, TextureViewType type) => CreateTextureView(texture, new TextureViewDescription { Type = type });

    public ITextureView CreateTextureView(ITexture texture, in TextureViewDescription description) => new NullTextureView(texture, description);

    public ISampler CreateSampler(in SamplerDescription description) => new NullSampler();

    public IShaderProgram CreateShaderProgram(in ShaderProgramDescription description) => new NullShaderProgram(description);

    public IGraphicsPipeline CreateGraphicsPipeline(in GraphicsPipelineDescription description) => new NullGraphicsPipeline(description);

    public IComputePipeline CreateComputePipeline(in ComputePipelineDescription description) => new NullComputePipeline(description);

    public ICommandBuffer CreateCommandBuffer() => new NullCommandBuffer();

    public void UploadBufferData(IBuffer buffer, ReadOnlySpan<byte> data, ulong bufferOffset = 0)
    {
        _bufferUploads.Add(new NullBufferUpload(buffer, bufferOffset, data.ToArray()));
    }

    public void UploadTextureData(ITexture texture, ReadOnlySpan<byte> data)
    {
        ArgumentNullException.ThrowIfNull(texture);
        TextureSubresourceData subresource = new()
        {
            MipLevel = 0,
            ArrayLayer = 0,
            Data = data.ToArray(),
        };
        UploadTextureSubresources(texture, [subresource]);
    }

    public void UploadTextureSubresources(ITexture texture, ReadOnlySpan<TextureSubresourceData> subresources) =>
        TextureUploadValidation.ValidateSubresources(texture, subresources);

    public void FlushUploads()
    {
    }

    public void Submit(ICommandBuffer commandBuffer)
    {
    }

    public void WaitIdle()
    {
    }

    public void Dispose()
    {
    }
}

public readonly record struct NullBufferUpload(IBuffer Buffer, ulong Offset, byte[] Data);

public sealed class NullMemoryAllocator : IMemoryAllocator
{
    public ulong UsedMemory => 0;

    public ulong AllocatedMemory => 0;

    public void Dispose()
    {
    }
}

public sealed class NullBuffer : IBuffer
{
    public NullBuffer(in BufferDescription description)
    {
        Size = description.Size;
        Usage = description.Usage;
        Memory = description.Memory;
    }

    public ulong Size { get; }

    public BufferUsage Usage { get; }

    public MemoryUsage Memory { get; }

    public IntPtr Map() => IntPtr.Zero;

    public void Unmap()
    {
    }

    public void Dispose()
    {
    }
}

public sealed class NullTexture : ITexture
{
    public NullTexture(in TextureDescription description)
    {
        Format = description.Format;
        Width = (uint)description.Extent.Width;
        Height = (uint)description.Extent.Height;
        Depth = Math.Max(1u, description.Depth);
        MipLevels = description.MipLevels == 0 ? 1 : description.MipLevels;
        ArrayLayers = description.ArrayLayers == 0 ? (description.Dimension == TextureDimension.Cube ? 6u : 1u) : description.ArrayLayers;
        Dimension = description.Dimension;
        if (Dimension == TextureDimension.Cube && (Width != Height || ArrayLayers != 6))
            throw new ArgumentException("Cube textures must be square and have exactly six array layers.", nameof(description));
        if (Dimension == TextureDimension.Texture2D && ArrayLayers != 1)
            throw new ArgumentException("2D textures must have exactly one array layer.", nameof(description));
        if (Dimension == TextureDimension.Texture3D && ArrayLayers != 1)
            throw new ArgumentException("3D textures must have exactly one array layer.", nameof(description));
        if (Dimension is < TextureDimension.Texture2D or > TextureDimension.Texture3D)
            throw new ArgumentOutOfRangeException(nameof(description), "Texture dimension is not supported.");
    }

    public TextureFormat Format { get; }

    public uint Width { get; }

    public uint Height { get; }

    public uint Depth { get; }

    public uint MipLevels { get; }

    public uint ArrayLayers { get; }

    public TextureDimension Dimension { get; }

    public void Dispose()
    {
    }
}

public sealed class NullTextureView : ITextureView
{
    public NullTextureView(ITexture texture, TextureViewType type)
        : this(texture, new TextureViewDescription { Type = type })
    {
    }

    public NullTextureView(ITexture texture, in TextureViewDescription description)
    {
        Texture = texture ?? throw new ArgumentNullException(nameof(texture));
        TextureViewDescription normalized = TextureViewValidation.ValidateAndNormalize(texture, description);
        Type = normalized.Type;
        BaseMipLevel = normalized.BaseMipLevel;
        MipLevelCount = normalized.MipLevelCount;
        BaseArrayLayer = normalized.BaseArrayLayer;
        ArrayLayerCount = normalized.ArrayLayerCount;
    }

    public ITexture Texture { get; }

    public TextureViewType Type { get; }

    public uint BaseMipLevel { get; }

    public uint MipLevelCount { get; }

    public uint BaseArrayLayer { get; }

    public uint ArrayLayerCount { get; }

    public void Dispose()
    {
    }
}

public sealed class NullSampler : ISampler
{
    public void Dispose()
    {
    }
}

public sealed class NullShaderProgram : IShaderProgram
{
    public NullShaderProgram(in ShaderProgramDescription description)
    {
        Name = description.Name;
        Reflection = description.Reflection;
    }

    public string Name { get; }

    public PipelineReflection Reflection { get; }

    public IDescriptorSet CreateDescriptorSet(uint setIndex) => new NullDescriptorSet(this, setIndex);

    public void Dispose()
    {
    }
}

public sealed class NullGraphicsPipeline : IGraphicsPipeline
{
    public NullGraphicsPipeline(in GraphicsPipelineDescription description)
    {
        ShaderProgram = description.ShaderProgram;
    }

    public IShaderProgram ShaderProgram { get; }

    public void Dispose()
    {
    }
}

public sealed class NullComputePipeline : IComputePipeline
{
    public NullComputePipeline(in ComputePipelineDescription description)
    {
        ShaderProgram = description.ShaderProgram;
    }

    public IShaderProgram ShaderProgram { get; }

    public void Dispose()
    {
    }
}

public sealed class NullDescriptorSet : IDescriptorSet
{
    public NullDescriptorSet(NullShaderProgram program, uint setIndex)
    {
        Program = program;
        SetIndex = setIndex;
    }

    public IShaderProgram Program { get; }

    public uint SetIndex { get; }

    public void WriteUniformBuffer(uint binding, IBuffer buffer, ulong offset = 0, ulong range = 0)
    {
    }

    public void WriteStorageBuffer(uint binding, IBuffer buffer, ulong offset = 0, ulong range = 0)
    {
    }

    public void WriteTexture(uint binding, ITexture texture, ISampler? sampler = null)
    {
    }

    public void WriteTexture(uint binding, ITextureView view, ISampler? sampler = null)
    {
    }

    public void WriteStorageImage(uint binding, ITexture texture)
    {
    }

    public void WriteStorageImage(uint binding, ITextureView view)
    {
    }

    public void WriteSampler(uint binding, ISampler sampler)
    {
    }

    public void Dispose()
    {
    }
}

public sealed class NullCommandBuffer : ICommandBuffer
{
    private bool _recording;
    private bool _renderPassActive;
    private readonly Stack<string> _timestampScopes = new();
    private Vector4? _lastClearColor;
    private uint _drawCalls;
    private uint _drawIndexedCalls;
    private uint _dispatchCalls;

    public Vector4? LastClearColor => _lastClearColor;
    public uint DrawCalls => _drawCalls;
    public uint DrawIndexedCalls => _drawIndexedCalls;
    public uint DispatchCalls => _dispatchCalls;
    public bool HasRecorded { get; private set; }

    public int TimestampScopeCount => _timestampScopes.Count;

    public int CompletedTimestampScopeCount { get; private set; }

    public void Begin() => _recording = true;

    public void End()
    {
        if (!_recording)
            throw new InvalidOperationException("Command buffer is not recording.");
        if (_renderPassActive)
            throw new InvalidOperationException("Render pass is still active.");
        if (_timestampScopes.Count > 0)
            throw new InvalidOperationException("Timestamp scopes are still active.");
        _recording = false;
    }

    public void BeginRendering(in RenderPassInfo info)
    {
        if (!_recording)
            throw new InvalidOperationException("Command buffer is not recording.");
        if (_renderPassActive)
            throw new InvalidOperationException("Render pass is already active.");
        _renderPassActive = true;
        HasRecorded = true;
        _lastClearColor = info.ColorAttachments.Length > 0 ? info.ColorAttachments[0].ClearColor : null;
    }

    public void EndRendering()
    {
        if (!_renderPassActive)
            throw new InvalidOperationException("No active render pass.");
        _renderPassActive = false;
    }

    public void BeginTimestamp(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Timestamp name must not be empty.", nameof(name));
        if (!_recording)
            throw new InvalidOperationException("Command buffer is not recording.");
        _timestampScopes.Push(name);
    }

    public void EndTimestamp()
    {
        if (!_recording || _timestampScopes.Count == 0)
            throw new InvalidOperationException("No timestamp scope is active.");
        _timestampScopes.Pop();
        CompletedTimestampScopeCount++;
    }

    public void SetViewport(in Viewport viewport)
    {
    }

    public void SetScissor(in Rect scissor)
    {
    }

    public void BindGraphicsPipeline(IGraphicsPipeline pipeline)
    {
    }

    public void BindComputePipeline(IComputePipeline pipeline)
    {
    }

    public void BindDescriptorSet(IDescriptorSet set, uint setIndex, ReadOnlySpan<uint> dynamicOffsets = default)
    {
    }

    public void BindVertexBuffer(uint slot, IBuffer buffer, ulong offset = 0)
    {
    }

    public void BindIndexBuffer(IBuffer buffer, IndexType indexType)
    {
    }

    public void PushConstants(ReadOnlySpan<byte> data, ShaderStages stages, uint offset = 0)
    {
    }

    public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0) => _drawCalls++;

    public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int vertexOffset = 0, uint firstInstance = 0) => _drawIndexedCalls++;

    public void Dispatch(uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1) => _dispatchCalls++;

    public void CopyBufferToBuffer(IBuffer source, IBuffer destination, ulong size, ulong sourceOffset = 0, ulong destinationOffset = 0)
    {
    }

    public void CopyBufferToImage(IBuffer source, ITexture destination, ulong sourceOffset = 0)
    {
    }

    public void ImageBarrier(ITexture texture, ImageLayout oldLayout, ImageLayout newLayout)
    {
    }

    public void ImageBarrier(
        ITexture texture,
        ImageLayout oldLayout,
        ImageLayout newLayout,
        PipelineStages sourceStages,
        ResourceAccess sourceAccess,
        PipelineStages destinationStages,
        ResourceAccess destinationAccess,
        TextureSubresourceRange subresources = default)
    {
    }

    public void CopyImage(ITexture source, ImageLayout sourceLayout, ITexture destination, ImageLayout destinationLayout)
    {
    }

    public void BlitImage(ITexture source, ImageLayout sourceLayout, ITexture destination, ImageLayout destinationLayout)
    {
    }

    public void BufferBarrier(IBuffer buffer, ulong size, ulong offset = 0)
    {
    }

    public void BufferBarrier(
        IBuffer buffer,
        ulong size,
        PipelineStages sourceStages,
        ResourceAccess sourceAccess,
        PipelineStages destinationStages,
        ResourceAccess destinationAccess,
        ulong offset = 0)
    {
    }

    public void Dispose()
    {
    }
}
