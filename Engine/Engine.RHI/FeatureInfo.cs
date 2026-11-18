namespace Engine.RHI;

public sealed record FeatureInfo
{
    public required string DeviceName { get; init; }
    public required string VendorName { get; init; }
    public required string ApiVersion { get; init; }
    public required ulong DedicatedVideoMemory { get; init; }
    public required bool SupportsCompute { get; init; }
    public required bool SupportsRayTracing { get; init; }
    public bool SupportsBufferDeviceAddress { get; init; }
    public bool SupportsAccelerationStructures { get; init; }
    public bool SupportsRayQuery { get; init; }
    public bool SupportsNonUniformSampledImageIndexing { get; init; }
    public uint MaxPerStageSampledImages { get; init; }
    public uint MaxDescriptorSetSampledImages { get; init; }
    public RayTracingLimits RayTracing { get; init; } = new();
    public string? RayTracingUnavailableReason { get; init; }
    public required bool SupportsMeshShaders { get; init; }
    public bool SupportsGpuTimestamps { get; init; }
    public float TimestampPeriodNanoseconds { get; init; }
    public IReadOnlyList<string> Extensions { get; init; } = [];
}
