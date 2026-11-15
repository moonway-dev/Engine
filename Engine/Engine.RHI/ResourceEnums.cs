namespace Engine.RHI;

[Flags]
public enum BufferUsage
{
    None = 0,
    Vertex = 1,
    Index = 2,
    Uniform = 4,
    Storage = 8,
    Indirect = 16,
    TransferSource = 32,
    TransferDestination = 64,
}

public enum MemoryUsage
{
    Unknown,
    GpuOnly,
    CpuToGpu,
    GpuToCpu,
    CpuOnly,
}

[Flags]
public enum TextureUsage
{
    None = 0,
    Sampled = 1,
    Storage = 2,
    ColorAttachment = 4,
    DepthAttachment = 8,
    TransferSource = 16,
    TransferDestination = 32,
}

[Flags]
public enum PipelineStages
{
    None = 0,
    Top = 1,
    DrawIndirect = 2,
    VertexInput = 4,
    VertexShader = 8,
    FragmentShader = 16,
    EarlyDepth = 32,
    LateDepth = 64,
    ColorOutput = 128,
    ComputeShader = 256,
    Transfer = 512,
    Bottom = 1024,
    Host = 2048,
    AllGraphics = 4096,
    AllCommands = 8192,
}

[Flags]
public enum ResourceAccess
{
    None = 0,
    IndirectRead = 1,
    IndexRead = 2,
    VertexRead = 4,
    UniformRead = 8,
    ShaderRead = 16,
    ShaderWrite = 32,
    ColorRead = 64,
    ColorWrite = 128,
    DepthRead = 256,
    DepthWrite = 512,
    TransferRead = 1024,
    TransferWrite = 2048,
    HostRead = 4096,
    HostWrite = 8192,
    MemoryRead = 16384,
    MemoryWrite = 32768,
}

public readonly record struct TextureSubresourceRange(
    uint BaseMipLevel = 0,
    uint MipLevelCount = uint.MaxValue,
    uint BaseArrayLayer = 0,
    uint ArrayLayerCount = uint.MaxValue);

public enum TextureFormat
{
    Unknown = 0,
    R8Unorm,
    R8G8B8A8UNorm,
    R8G8B8A8Srgb,
    B8G8R8A8UNorm,
    B8G8R8A8Srgb,
    R16G16B16A16Float,
    R32G32B32A32Float,
    R11G11B10Float,
    D16Unorm,
    D32Float,
    D24UnormS8Uint,
    D32FloatS8Uint,
}

public static class TextureFormatExtensions
{
    public static bool IsDepth(this TextureFormat format) => format switch
    {
        TextureFormat.D16Unorm or TextureFormat.D32Float or TextureFormat.D24UnormS8Uint or TextureFormat.D32FloatS8Uint => true,
        _ => false,
    };
}

public enum PresentMode
{
    Fifo,
    FifoRelaxed,
    Mailbox,
    Immediate,
}

public enum IndexType
{
    UInt16,
    UInt32,
}

public enum ShaderStage
{
    Vertex,
    TessellationControl,
    TessellationEvaluation,
    Geometry,
    Fragment,
    Compute,
    Mesh,
    Task,
    RayGeneration,
    Miss,
    ClosestHit,
    AnyHit,
    Intersection,
    Callable,
}

public enum PolygonMode
{
    Fill,
    Line,
    Point,
}

public enum CullMode
{
    None,
    Front,
    Back,
    FrontAndBack,
}

public enum FrontFace
{
    CounterClockwise,
    Clockwise,
}

public enum CompareOp
{
    Never,
    Less,
    Equal,
    LessOrEqual,
    Greater,
    NotEqual,
    GreaterOrEqual,
    Always,
}
