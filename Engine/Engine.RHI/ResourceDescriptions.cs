using Engine.Mathematics;

namespace Engine.RHI;

public readonly record struct BufferDescription
{
    public required ulong Size { get; init; }
    public required BufferUsage Usage { get; init; }
    public MemoryUsage Memory { get; init; }
}

public readonly record struct TextureDescription
{
    public required Size2D Extent { get; init; }
    public required TextureFormat Format { get; init; }
    public TextureUsage Usage { get; init; }
    public uint MipLevels { get; init; }
    public uint ArrayLayers { get; init; }
    public TextureDimension Dimension { get; init; }
    public uint Depth { get; init; }
}

public readonly record struct TextureViewDescription
{
    public required TextureViewType Type { get; init; }
    public uint BaseMipLevel { get; init; }
    public uint MipLevelCount { get; init; }
    public uint BaseArrayLayer { get; init; }
    public uint ArrayLayerCount { get; init; }
}

public readonly record struct TextureSubresourceData
{
    public required uint MipLevel { get; init; }
    public required uint ArrayLayer { get; init; }
    public required ReadOnlyMemory<byte> Data { get; init; }
}

public readonly record struct RenderPassBeginInfo
{
    public required ITexture RenderTarget { get; init; }
    public required Vector4 ClearColor { get; init; }
}
