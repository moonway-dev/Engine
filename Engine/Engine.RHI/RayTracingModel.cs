using System.Runtime.InteropServices;
using Engine.Mathematics;

namespace Engine.RHI;

public enum RayTracingMode
{
    Off,
    Auto,
    Required,
}

public sealed record RayTracingLimits
{
    public uint ShaderGroupHandleSize { get; init; }
    public uint ShaderGroupHandleAlignment { get; init; }
    public uint ShaderGroupBaseAlignment { get; init; }
    public uint MaxShaderGroupStride { get; init; }
    public uint MaxRayRecursionDepth { get; init; }
    public uint MaxRayDispatchInvocationCount { get; init; }
    public uint MinScratchAlignment { get; init; }
    public ulong MaxGeometryCount { get; init; }
    public ulong MaxPrimitiveCount { get; init; }
    public ulong MaxInstanceCount { get; init; }
}

public enum AccelerationStructureType
{
    BottomLevel,
    TopLevel,
}

[Flags]
public enum AccelerationStructureBuildFlags
{
    None = 0,
    AllowUpdate = 1,
    PreferFastTrace = 2,
    PreferFastBuild = 4,
}

[Flags]
public enum AccelerationStructureGeometryFlags
{
    None = 0,
    Opaque = 1,
    NoDuplicateAnyHit = 2,
}

[Flags]
public enum RayTracingInstanceFlags : byte
{
    None = 0,
    TriangleFacingCullDisable = 1,
    TriangleFrontCounterClockwise = 2,
    ForceOpaque = 4,
    ForceNonOpaque = 8,
}

public abstract record AccelerationStructureGeometry
{
    public AccelerationStructureGeometryFlags Flags { get; init; } = AccelerationStructureGeometryFlags.Opaque;
    public abstract uint PrimitiveCount { get; }
}

public sealed record TriangleGeometry : AccelerationStructureGeometry
{
    public required IBuffer VertexBuffer { get; init; }
    public ulong VertexOffset { get; init; }
    public uint VertexStride { get; init; } = 12;
    public uint VertexCount { get; init; }
    public VertexFormat VertexFormat { get; init; } = VertexFormat.Float3;
    public IBuffer? IndexBuffer { get; init; }
    public ulong IndexOffset { get; init; }
    public IndexType IndexType { get; init; } = IndexType.UInt32;
    public uint IndexCount { get; init; }
    public override uint PrimitiveCount => (IndexBuffer is null ? VertexCount : IndexCount) / 3;
}

public sealed record AabbGeometry : AccelerationStructureGeometry
{
    public required IBuffer Buffer { get; init; }
    public ulong Offset { get; init; }
    public uint Stride { get; init; } = 24;
    public uint Count { get; init; }
    public override uint PrimitiveCount => Count;
}

public sealed record InstanceGeometry : AccelerationStructureGeometry
{
    public required IBuffer Buffer { get; init; }
    public ulong Offset { get; init; }
    public uint Count { get; init; }
    public IReadOnlyList<IAccelerationStructure> BottomLevels { get; init; } = [];
    public override uint PrimitiveCount => Count;
}

public sealed record AccelerationStructureBuildDescription
{
    public required AccelerationStructureType Type { get; init; }
    public required IReadOnlyList<AccelerationStructureGeometry> Geometries { get; init; }
    public AccelerationStructureBuildFlags Flags { get; init; } = AccelerationStructureBuildFlags.PreferFastTrace;
}

public readonly record struct AccelerationStructureBuildSizes(ulong StorageSize, ulong BuildScratchSize, ulong UpdateScratchSize);

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct RayTracingInstance
{
    public float M11;
    public float M12;
    public float M13;
    public float M14;
    public float M21;
    public float M22;
    public float M23;
    public float M24;
    public float M31;
    public float M32;
    public float M33;
    public float M34;
    public uint CustomIndexAndMask;
    public uint ShaderBindingOffsetAndFlags;
    public ulong AccelerationStructureReference;

    public static RayTracingInstance Create(Matrix4x4 transform, uint customIndex, byte mask, uint shaderBindingOffset,
        RayTracingInstanceFlags flags, ulong bottomLevelAddress)
    {
        if (customIndex > 0xffffff || shaderBindingOffset > 0xffffff)
            throw new ArgumentOutOfRangeException(nameof(customIndex));
        if (bottomLevelAddress == 0)
            throw new ArgumentOutOfRangeException(nameof(bottomLevelAddress));
        if (transform.M41 != 0f || transform.M42 != 0f || transform.M43 != 0f || transform.M44 != 1f)
            throw new ArgumentException("Instance transform must be affine.", nameof(transform));
        float[] elements = [transform.M11, transform.M12, transform.M13, transform.M14, transform.M21, transform.M22,
            transform.M23, transform.M24, transform.M31, transform.M32, transform.M33, transform.M34];
        if (elements.Any(value => !float.IsFinite(value)))
            throw new ArgumentException("Instance transform must be finite.", nameof(transform));
        float determinant = transform.M11 * (transform.M22 * transform.M33 - transform.M23 * transform.M32)
            - transform.M12 * (transform.M21 * transform.M33 - transform.M23 * transform.M31)
            + transform.M13 * (transform.M21 * transform.M32 - transform.M22 * transform.M31);
        if (!float.IsFinite(determinant) || MathF.Abs(determinant) < 1e-12f)
            throw new ArgumentException("Instance transform must be invertible.", nameof(transform));
        if (flags.HasFlag(RayTracingInstanceFlags.ForceOpaque) && flags.HasFlag(RayTracingInstanceFlags.ForceNonOpaque))
            throw new ArgumentException("Conflicting instance opacity flags.", nameof(flags));
        return new RayTracingInstance
        {
            M11 = transform.M11, M12 = transform.M12, M13 = transform.M13, M14 = transform.M14,
            M21 = transform.M21, M22 = transform.M22, M23 = transform.M23, M24 = transform.M24,
            M31 = transform.M31, M32 = transform.M32, M33 = transform.M33, M34 = transform.M34,
            CustomIndexAndMask = customIndex | ((uint)mask << 24),
            ShaderBindingOffsetAndFlags = shaderBindingOffset | ((uint)flags << 24),
            AccelerationStructureReference = bottomLevelAddress,
        };
    }
}

public enum RayTracingShaderGroupType
{
    General,
    TrianglesHit,
    ProceduralHit,
}

public sealed record RayTracingShaderGroup
{
    public required RayTracingShaderGroupType Type { get; init; }
    public int GeneralShader { get; init; } = -1;
    public int ClosestHitShader { get; init; } = -1;
    public int AnyHitShader { get; init; } = -1;
    public int IntersectionShader { get; init; } = -1;
}

public sealed record RayTracingPipelineDescription
{
    public required string Name { get; init; }
    public required IShaderProgram ShaderProgram { get; init; }
    public required IReadOnlyList<RayTracingShaderGroup> Groups { get; init; }
    public uint MaxRecursionDepth { get; init; } = 1;
}

public readonly record struct ShaderBindingRegion(ulong DeviceAddress, ulong Stride, ulong Size);

public sealed class ShaderBindingTable : IDisposable
{
    private readonly IRayTracingPipeline _pipeline;

    public IRayTracingPipeline Pipeline => _pipeline;

    public IBuffer Buffer { get; }
    public ShaderBindingRegion RayGeneration { get; }
    public ShaderBindingRegion Miss { get; }
    public ShaderBindingRegion Hit { get; }
    public ShaderBindingRegion Callable { get; }

    public ShaderBindingTable(IDevice device, IRayTracingPipeline pipeline, uint rayGenerationGroup,
        IReadOnlyList<uint> missGroups, IReadOnlyList<uint> hitGroups, IReadOnlyList<uint>? callableGroups = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(missGroups);
        ArgumentNullException.ThrowIfNull(hitGroups);
        _pipeline = pipeline;
        RayTracingLimits limits = device.Features.RayTracing;
        if (!device.Features.SupportsRayTracing || limits.ShaderGroupHandleSize == 0)
            throw new NotSupportedException("Ray tracing pipelines are unavailable.");
        if (limits.ShaderGroupHandleAlignment == 0 || limits.ShaderGroupBaseAlignment == 0)
            throw new ArgumentException("Ray-tracing shader group alignments must be non-zero.", nameof(device));
        uint stride = checked((uint)AlignUp(limits.ShaderGroupHandleSize, limits.ShaderGroupHandleAlignment));
        if (stride > limits.MaxShaderGroupStride)
            throw new ArgumentOutOfRangeException(nameof(limits.MaxShaderGroupStride));
        callableGroups ??= [];
        IReadOnlyList<uint>[] groups = [[rayGenerationGroup], missGroups, hitGroups, callableGroups];
        foreach (uint group in groups.SelectMany(region => region))
        {
            if (group >= pipeline.Description.Groups.Count)
                throw new ArgumentOutOfRangeException(nameof(rayGenerationGroup), $"Shader group index {group} is outside the pipeline group range.");
        }
        if (pipeline.Description.Groups[checked((int)rayGenerationGroup)].Type != RayTracingShaderGroupType.General)
            throw new ArgumentException("Ray-generation SBT record must reference a general shader group.", nameof(rayGenerationGroup));
        if (missGroups.Any(group => pipeline.Description.Groups[checked((int)group)].Type != RayTracingShaderGroupType.General))
            throw new ArgumentException("Miss SBT records must reference general shader groups.", nameof(missGroups));
        if (callableGroups.Any(group => pipeline.Description.Groups[checked((int)group)].Type != RayTracingShaderGroupType.General))
            throw new ArgumentException("Callable SBT records must reference general shader groups.", nameof(callableGroups));
        if (hitGroups.Any(group => pipeline.Description.Groups[checked((int)group)].Type == RayTracingShaderGroupType.General))
            throw new ArgumentException("Hit SBT records must reference hit shader groups.", nameof(hitGroups));
        ulong[] offsets = new ulong[4];
        ulong size = 0;
        for (int region = 0; region < 4; region++)
        {
            offsets[region] = size;
            size = checked(size + AlignUp(checked((ulong)groups[region].Count * stride), limits.ShaderGroupBaseAlignment));
        }
        byte[] handles = pipeline.GetShaderGroupHandles();
        if (handles.Length != checked(pipeline.Description.Groups.Count * (int)limits.ShaderGroupHandleSize))
            throw new InvalidOperationException("Unexpected shader group handle byte count.");
        Buffer = device.CreateBuffer(new BufferDescription
        {
            Size = checked(size + limits.ShaderGroupBaseAlignment),
            Usage = BufferUsage.ShaderBindingTable | BufferUsage.DeviceAddress | BufferUsage.TransferDestination,
            Memory = MemoryUsage.GpuOnly,
        });
        try
        {
            ulong baseAddress = AlignUp(Buffer.DeviceAddress, limits.ShaderGroupBaseAlignment);
            ulong baseOffset = baseAddress - Buffer.DeviceAddress;
            byte[] bytes = new byte[checked((int)size)];
            for (int region = 0; region < 4; region++)
            for (int record = 0; record < groups[region].Count; record++)
            {
                uint group = groups[region][record];
                if (group >= pipeline.Description.Groups.Count)
                    throw new ArgumentOutOfRangeException(nameof(groups));
                handles.AsSpan(checked((int)(group * limits.ShaderGroupHandleSize)), checked((int)limits.ShaderGroupHandleSize))
                    .CopyTo(bytes.AsSpan(checked((int)(offsets[region] + (ulong)record * stride))));
            }
            device.UploadBufferData(Buffer, bytes, baseOffset);
            ShaderBindingRegion Region(int index) => groups[index].Count == 0 ? default
                : new ShaderBindingRegion(baseAddress + offsets[index], stride, checked((ulong)groups[index].Count * stride));
            RayGeneration = Region(0);
            Miss = Region(1);
            Hit = Region(2);
            Callable = Region(3);
        }
        catch
        {
            Buffer.Dispose();
            throw;
        }
    }

    public static ulong AlignUp(ulong value, ulong alignment)
    {
        if (alignment == 0)
            throw new ArgumentOutOfRangeException(nameof(alignment));
        ulong remainder = value % alignment;
        return remainder == 0 ? value : checked(value + alignment - remainder);
    }

    public void Dispose() => Buffer.Dispose();
}
