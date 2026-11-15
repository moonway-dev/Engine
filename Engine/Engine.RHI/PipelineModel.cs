namespace Engine.RHI;

public enum PrimitiveTopology
{
    TriangleList,
    TriangleStrip,
    LineList,
    PointList,
}

public enum VertexFormat
{
    Float1,
    Float2,
    Float3,
    Float4,
    UByte4Norm,
}

public enum BlendMode
{
    None,
    Alpha,
    Premultiplied,
    Additive,
}

public sealed record VertexAttributeDescription
{
    public required uint Offset { get; init; }
    public required VertexFormat Format { get; init; }
}

public sealed record VertexLayoutDescription
{
    public required IReadOnlyList<VertexAttributeDescription> Attributes { get; init; }
    public uint Stride { get; init; }

    public static VertexLayoutDescription FromStruct<T>(params VertexAttributeDescription[] attributes)
        where T : unmanaged
    {
        return new VertexLayoutDescription
        {
            Attributes = attributes,
            Stride = sizeof_for_struct<T>(),
        };
    }

    private static unsafe uint sizeof_for_struct<T>() where T : unmanaged => (uint)sizeof(T);
}

public sealed record GraphicsPipelineDescription
{
    public required string Name { get; init; }
    public required IShaderProgram ShaderProgram { get; init; }
    public required IReadOnlyList<VertexLayoutDescription> VertexLayouts { get; init; }
    public PrimitiveTopology Topology { get; init; } = PrimitiveTopology.TriangleList;
    public PolygonMode PolygonMode { get; init; } = PolygonMode.Fill;
    public CullMode CullMode { get; init; } = CullMode.Back;
    public FrontFace FrontFace { get; init; } = FrontFace.CounterClockwise;
    public bool DepthTest { get; init; } = true;
    public bool DepthWrite { get; init; } = true;
    public bool DepthBiasEnabled { get; init; }
    public float DepthBiasConstant { get; init; }
    public float DepthBiasSlope { get; init; }
    public CompareOp DepthCompare { get; init; } = CompareOp.Less;
    public BlendMode BlendMode { get; init; } = BlendMode.None;
    public IReadOnlyList<TextureFormat> ColorAttachmentFormats { get; init; } = [];
    public TextureFormat? DepthAttachmentFormat { get; init; }
}

public sealed record ComputePipelineDescription
{
    public required string Name { get; init; }
    public required IShaderProgram ShaderProgram { get; init; }
}

public enum ImageLayout
{
    Undefined,
    ColorAttachmentOptimal,
    DepthAttachmentOptimal,
    DepthStencilReadOnlyOptimal,
    ShaderReadOnlyOptimal,
    TransferSrcOptimal,
    TransferDstOptimal,
    PresentSrcKhr,
    General,
    DepthReadOnly,
}

public enum AttachmentLoadOp
{
    Load,
    Clear,
    DontCare,
}

public enum AttachmentStoreOp
{
    Store,
    DontCare,
}

public readonly record struct Viewport(float X, float Y, float Width, float Height, float MinDepth, float MaxDepth);

public enum FilterMode
{
    Nearest,
    Linear,
}

public enum AddressMode
{
    Repeat,
    MirroredRepeat,
    Clamp,
}

public sealed record SamplerDescription
{
    public FilterMode MagFilter { get; init; } = FilterMode.Linear;
    public FilterMode MinFilter { get; init; } = FilterMode.Linear;
    public AddressMode AddressMode { get; init; } = AddressMode.Repeat;
    public bool CompareEnabled { get; init; }
    public CompareOp CompareOp { get; init; } = CompareOp.Less;
    public uint MaxAnisotropy { get; init; } = 1;
}
