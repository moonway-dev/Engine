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
}

public readonly record struct RenderPassBeginInfo
{
    public required ITexture RenderTarget { get; init; }
    public required Vector4 ClearColor { get; init; }
}
