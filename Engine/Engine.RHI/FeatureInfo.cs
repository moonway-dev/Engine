namespace Engine.RHI;

public sealed record FeatureInfo
{
    public required string DeviceName { get; init; }
    public required string VendorName { get; init; }
    public required string ApiVersion { get; init; }
    public required ulong DedicatedVideoMemory { get; init; }
    public required bool SupportsCompute { get; init; }
    public required bool SupportsRayTracing { get; init; }
    public required bool SupportsMeshShaders { get; init; }
    public IReadOnlyList<string> Extensions { get; init; } = [];
}
