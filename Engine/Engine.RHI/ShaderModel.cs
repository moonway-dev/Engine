namespace Engine.RHI;

public enum DescriptorType
{
    UniformBuffer,
    StorageBuffer,
    SampledImage,
    StorageImage,
    Sampler,
    CombinedImageSampler,
    InputAttachment,
    AccelerationStructure,
}

public sealed record PipelineReflection
{
    public IReadOnlyList<PushConstantRange> PushConstants { get; init; } = [];
    public IReadOnlyList<DescriptorSetLayout> DescriptorSets { get; init; } = [];
    public IReadOnlyList<uint> VertexInputs { get; init; } = [];

    public static readonly PipelineReflection Empty = new();
}

public sealed record PushConstantRange
{
    public required uint Offset { get; init; }
    public required uint Size { get; init; }
    public required ShaderStages Stages { get; init; }
}

public sealed record DescriptorSetLayout
{
    public required uint SetIndex { get; init; }
    public required IReadOnlyList<DescriptorBinding> Bindings { get; init; }
}

public sealed record DescriptorBinding
{
    public required uint Binding { get; init; }
    public required DescriptorType Type { get; init; }
    public required ShaderStages Stages { get; init; }
    public uint ArrayCount { get; init; } = 1;
}

[Flags]
public enum ShaderStages
{
    None = 0,
    Vertex = 1,
    Fragment = 2,
    Compute = 4,
    Geometry = 8,
    TessellationControl = 16,
    TessellationEvaluation = 32,
    RayGeneration = 64,
    Miss = 128,
    ClosestHit = 256,
    AnyHit = 512,
    Intersection = 1024,
    Callable = 2048,
}

public sealed record ShaderStageCode
{
    public required ShaderStage Stage { get; init; }
    public required byte[] SpirV { get; init; }
    public required string EntryPoint { get; init; }
}

public sealed record ShaderProgramDescription
{
    public required string Name { get; init; }
    public required IReadOnlyList<ShaderStageCode> Stages { get; init; }
    public PipelineReflection Reflection { get; init; } = PipelineReflection.Empty;
}
