using System.Runtime.InteropServices;
using Engine.Backends.Null;
using Engine.Mathematics;
using Engine.RHI;
using Xunit;

namespace Engine.Tests.RHI;

public class RayTracingValidationTests
{
    private const BufferUsage BuildInputUsage = BufferUsage.AccelerationStructureBuildInput | BufferUsage.DeviceAddress;

    private static readonly RayTracingLimits Limits = new()
    {
        MaxGeometryCount = 4,
        MaxPrimitiveCount = 100,
        MaxInstanceCount = 8,
    };

    [Fact]
    public void RayTracingInstance_HasExpected64ByteAbi()
    {
        Assert.Equal(64, Marshal.SizeOf<RayTracingInstance>());
        Assert.Equal(48, Marshal.OffsetOf<RayTracingInstance>(nameof(RayTracingInstance.CustomIndexAndMask)).ToInt32());
        Assert.Equal(52, Marshal.OffsetOf<RayTracingInstance>(nameof(RayTracingInstance.ShaderBindingOffsetAndFlags)).ToInt32());
        Assert.Equal(56, Marshal.OffsetOf<RayTracingInstance>(nameof(RayTracingInstance.AccelerationStructureReference)).ToInt32());
    }

    [Fact]
    public void RayTracingInstance_CreatePacks24BitIndicesMaskAndFlags()
    {
        RayTracingInstance instance = RayTracingInstance.Create(
            Matrix4x4.Identity,
            customIndex: 0x00ff_ffff,
            mask: 0xa5,
            shaderBindingOffset: 0x00ff_ffff,
            flags: RayTracingInstanceFlags.TriangleFacingCullDisable
                | RayTracingInstanceFlags.TriangleFrontCounterClockwise
                | RayTracingInstanceFlags.ForceNonOpaque,
            bottomLevelAddress: 0x1234_5678);

        Assert.Equal(0xa5ff_ffffu, instance.CustomIndexAndMask);
        Assert.Equal(0x0bff_ffffu, instance.ShaderBindingOffsetAndFlags);
        Assert.Equal(0x1234_5678ul, instance.AccelerationStructureReference);
        Assert.Equal(1f, instance.M11);
        Assert.Equal(1f, instance.M22);
        Assert.Equal(1f, instance.M33);
        Assert.Equal(0f, instance.M14);
    }

    [Fact]
    public void RayTracingInstance_CreateRejectsValuesOutside24BitFieldsAndZeroAddress()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RayTracingInstance.Create(
            Matrix4x4.Identity, 0x0100_0000, 0, 0, RayTracingInstanceFlags.None, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => RayTracingInstance.Create(
            Matrix4x4.Identity, 0, 0, 0x0100_0000, RayTracingInstanceFlags.None, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => RayTracingInstance.Create(
            Matrix4x4.Identity, 0, 0, 0, RayTracingInstanceFlags.None, 0));
    }

    [Fact]
    public void RayTracingInstance_CreateRejectsNonAffineNonFiniteAndSingularTransforms()
    {
        Matrix4x4 nonAffine = Matrix4x4.Identity;
        nonAffine.M41 = 1f;
        Assert.Throws<ArgumentException>(() => CreateInstance(nonAffine));

        Matrix4x4 nonFinite = Matrix4x4.Identity;
        nonFinite.M23 = float.NaN;
        Assert.Throws<ArgumentException>(() => CreateInstance(nonFinite));

        Assert.Throws<ArgumentException>(() => CreateInstance(Matrix4x4.CreateScale(0f)));
    }

    [Fact]
    public void RayTracingInstance_CreateRejectsConflictingOpacityFlags()
    {
        RayTracingInstanceFlags flags = RayTracingInstanceFlags.ForceOpaque | RayTracingInstanceFlags.ForceNonOpaque;
        Assert.Throws<ArgumentException>(() => RayTracingInstance.Create(Matrix4x4.Identity, 0, 0, 0, flags, 1));
    }

    [Fact]
    public void ValidateBuild_AcceptsTriangleVertexAndIndexRangesAtBufferEnd()
    {
        TriangleGeometry triangle = new()
        {
            VertexBuffer = CreateInputBuffer(size: 48),
            VertexOffset = 4,
            VertexStride = 16,
            VertexCount = 3,
            IndexBuffer = CreateInputBuffer(size: 8),
            IndexOffset = 2,
            IndexType = IndexType.UInt16,
            IndexCount = 3,
        };

        RayTracingValidation.ValidateBuild(BottomLevel(triangle), Limits);
    }

    [Fact]
    public void ValidateBuild_RejectsTriangleVertexInputWithInvalidUsageOrRange()
    {
        TriangleGeometry missingDeviceAddressUsage = new()
        {
            VertexBuffer = CreateBuffer(36, BufferUsage.AccelerationStructureBuildInput),
            VertexCount = 3,
        };
        TriangleGeometry vertexRangeOverrun = new()
        {
            VertexBuffer = CreateInputBuffer(35),
            VertexCount = 3,
        };
        TriangleGeometry misalignedVertexOffset = new()
        {
            VertexBuffer = CreateInputBuffer(40),
            VertexOffset = 2,
            VertexCount = 3,
        };

        Assert.Throws<ArgumentException>(() => RayTracingValidation.ValidateBuild(BottomLevel(missingDeviceAddressUsage), Limits));
        Assert.Throws<ArgumentException>(() => RayTracingValidation.ValidateBuild(BottomLevel(vertexRangeOverrun), Limits));
        Assert.Throws<ArgumentException>(() => RayTracingValidation.ValidateBuild(BottomLevel(misalignedVertexOffset), Limits));
    }

    [Fact]
    public void ValidateBuild_RejectsInvalidTriangleIndexRangeAndAlignment()
    {
        TriangleGeometry indexRangeOverrun = new()
        {
            VertexBuffer = CreateInputBuffer(36),
            VertexCount = 3,
            IndexBuffer = CreateInputBuffer(5),
            IndexType = IndexType.UInt16,
            IndexCount = 3,
        };
        TriangleGeometry misalignedIndexOffset = new()
        {
            VertexBuffer = CreateInputBuffer(36),
            VertexCount = 3,
            IndexBuffer = CreateInputBuffer(8),
            IndexOffset = 2,
            IndexType = IndexType.UInt32,
            IndexCount = 3,
        };
        TriangleGeometry incompleteIndexTriplet = new()
        {
            VertexBuffer = CreateInputBuffer(36),
            VertexCount = 3,
            IndexBuffer = CreateInputBuffer(8),
            IndexCount = 4,
        };

        Assert.Throws<ArgumentException>(() => RayTracingValidation.ValidateBuild(BottomLevel(indexRangeOverrun), Limits));
        Assert.Throws<ArgumentException>(() => RayTracingValidation.ValidateBuild(BottomLevel(misalignedIndexOffset), Limits));
        Assert.Throws<ArgumentException>(() => RayTracingValidation.ValidateBuild(BottomLevel(incompleteIndexTriplet), Limits));
    }

    [Fact]
    public void ValidateBuild_AcceptsAabbRangeAndRejectsMisalignedOrOutOfRangeInput()
    {
        AabbGeometry valid = new()
        {
            Buffer = CreateInputBuffer(size: 64),
            Offset = 8,
            Stride = 32,
            Count = 2,
        };
        RayTracingValidation.ValidateBuild(BottomLevel(valid), Limits);

        AabbGeometry rangeOverrun = valid with { Buffer = CreateInputBuffer(size: 63) };
        AabbGeometry misalignedOffset = valid with { Offset = 4 };
        AabbGeometry invalidStride = valid with { Stride = 28 };
        Assert.Throws<ArgumentException>(() => RayTracingValidation.ValidateBuild(BottomLevel(rangeOverrun), Limits));
        Assert.Throws<ArgumentException>(() => RayTracingValidation.ValidateBuild(BottomLevel(misalignedOffset), Limits));
        Assert.Throws<ArgumentException>(() => RayTracingValidation.ValidateBuild(BottomLevel(invalidStride), Limits));
    }

    [Fact]
    public void ValidateBuild_AcceptsTlasInstanceRangeAndRejectsInvalidRangeOrBlasReference()
    {
        TestAccelerationStructure blas = new(AccelerationStructureType.BottomLevel, 0x4000);
        InstanceGeometry valid = new()
        {
            Buffer = CreateInputBuffer(size: 144),
            Offset = 16,
            Count = 2,
            BottomLevels = [blas],
        };
        RayTracingValidation.ValidateBuild(TopLevel(valid), Limits);

        InstanceGeometry rangeOverrun = valid with { Buffer = CreateInputBuffer(size: 143) };
        InstanceGeometry misalignedOffset = valid with { Offset = 8 };
        InstanceGeometry wrongAsType = valid with
        {
            BottomLevels = [new TestAccelerationStructure(AccelerationStructureType.TopLevel, 0x8000)],
        };
        InstanceGeometry zeroAddress = valid with
        {
            BottomLevels = [new TestAccelerationStructure(AccelerationStructureType.BottomLevel, 0)],
        };

        Assert.Throws<ArgumentException>(() => RayTracingValidation.ValidateBuild(TopLevel(rangeOverrun), Limits));
        Assert.Throws<ArgumentException>(() => RayTracingValidation.ValidateBuild(TopLevel(misalignedOffset), Limits));
        Assert.Throws<ArgumentException>(() => RayTracingValidation.ValidateBuild(TopLevel(wrongAsType), Limits));
        Assert.Throws<ArgumentException>(() => RayTracingValidation.ValidateBuild(TopLevel(zeroAddress), Limits));
        Assert.Throws<ArgumentException>(() => RayTracingValidation.ValidateBuild(TopLevel(valid with { Count = 9 }), Limits));
    }

    [Fact]
    public void ValidateBuild_EnforcesGeometryAndPrimitiveLimitsAndBuildFlags()
    {
        TriangleGeometry triangle = new() { VertexBuffer = CreateInputBuffer(36), VertexCount = 3 };
        AccelerationStructureBuildDescription description = BottomLevel(triangle);

        Assert.Throws<ArgumentException>(() => RayTracingValidation.ValidateBuild(description, Limits with { MaxGeometryCount = 0 }));
        Assert.Throws<ArgumentException>(() => RayTracingValidation.ValidateBuild(description, Limits with { MaxPrimitiveCount = 0 }));
        Assert.Throws<ArgumentException>(() => RayTracingValidation.ValidateBuild(description with
        {
            Flags = AccelerationStructureBuildFlags.PreferFastBuild | AccelerationStructureBuildFlags.PreferFastTrace,
        }, Limits));
        Assert.Throws<ArgumentException>(() => RayTracingValidation.ValidateBuild(description with { Type = (AccelerationStructureType)99 }, Limits));
    }

    [Fact]
    public void ValidateUpdate_AllowsTopologyStableUpdateAndRejectsIllegalChanges()
    {
        AccelerationStructureBuildDescription previous = BottomLevel(
            new TriangleGeometry { VertexBuffer = CreateInputBuffer(36), VertexCount = 3 },
            AccelerationStructureBuildFlags.AllowUpdate | AccelerationStructureBuildFlags.PreferFastTrace);
        AccelerationStructureBuildDescription legal = BottomLevel(
            new TriangleGeometry { VertexBuffer = CreateInputBuffer(36), VertexCount = 3 },
            AccelerationStructureBuildFlags.AllowUpdate | AccelerationStructureBuildFlags.PreferFastTrace);

        RayTracingValidation.ValidateUpdate(previous, legal);

        Assert.Throws<ArgumentException>(() => RayTracingValidation.ValidateUpdate(
            BottomLevel((TriangleGeometry)previous.Geometries[0], AccelerationStructureBuildFlags.PreferFastTrace), legal));
        Assert.Throws<ArgumentException>(() => RayTracingValidation.ValidateUpdate(previous, legal with
        {
            Flags = AccelerationStructureBuildFlags.AllowUpdate | AccelerationStructureBuildFlags.PreferFastBuild,
        }));
        Assert.Throws<ArgumentException>(() => RayTracingValidation.ValidateUpdate(previous, legal with
        {
            Geometries = [legal.Geometries[0], legal.Geometries[0]],
        }));
        Assert.Throws<ArgumentException>(() => RayTracingValidation.ValidateUpdate(previous, BottomLevel(
            new TriangleGeometry { VertexBuffer = CreateInputBuffer(72), VertexCount = 6 },
            AccelerationStructureBuildFlags.AllowUpdate | AccelerationStructureBuildFlags.PreferFastTrace)));
        Assert.Throws<ArgumentException>(() => RayTracingValidation.ValidateUpdate(previous, BottomLevel(
            new TriangleGeometry { VertexBuffer = CreateInputBuffer(36), VertexCount = 3, VertexFormat = VertexFormat.Float2 },
            AccelerationStructureBuildFlags.AllowUpdate | AccelerationStructureBuildFlags.PreferFastTrace)));
    }

    [Fact]
    public void NullBackend_ReportsRayTracingUnavailableAndThrowsForRayTracingOperations()
    {
        using IDevice device = new NullDevice();
        Assert.False(device.Features.SupportsRayTracing);
        Assert.Throws<NotSupportedException>(() => device.GetAccelerationStructureBuildSizes(null!));
        Assert.Throws<NotSupportedException>(() => device.CreateAccelerationStructure(AccelerationStructureType.BottomLevel, 1024));
        Assert.Throws<NotSupportedException>(() => device.CreateRayTracingPipeline(null!));

        using ICommandBuffer commandBuffer = device.CreateCommandBuffer();
        Assert.Throws<NotSupportedException>(() => commandBuffer.BindRayTracingPipeline(null!));
        Assert.Throws<NotSupportedException>(() => commandBuffer.BuildAccelerationStructure(null!, null!, null!));
        Assert.Throws<NotSupportedException>(() => commandBuffer.TraceRays(null!, 1));
    }

    private static RayTracingInstance CreateInstance(Matrix4x4 transform) =>
        RayTracingInstance.Create(transform, 0, 0, 0, RayTracingInstanceFlags.None, 1);

    private static TestBuffer CreateInputBuffer(ulong size) => CreateBuffer(size, BuildInputUsage);

    private static TestBuffer CreateBuffer(ulong size, BufferUsage usage) => new(size, usage, 0x1000);

    private static AccelerationStructureBuildDescription BottomLevel(
        AccelerationStructureGeometry geometry,
        AccelerationStructureBuildFlags flags = AccelerationStructureBuildFlags.PreferFastTrace) => new()
    {
        Type = AccelerationStructureType.BottomLevel,
        Geometries = [geometry],
        Flags = flags,
    };

    private static AccelerationStructureBuildDescription TopLevel(InstanceGeometry geometry) => new()
    {
        Type = AccelerationStructureType.TopLevel,
        Geometries = [geometry],
    };

    private sealed class TestBuffer(ulong size, BufferUsage usage, ulong deviceAddress) : IBuffer
    {
        public ulong Size { get; } = size;
        public BufferUsage Usage { get; } = usage;
        public MemoryUsage Memory => MemoryUsage.CpuToGpu;
        public ulong DeviceAddress { get; } = deviceAddress;
        public IntPtr Map() => IntPtr.Zero;
        public void Unmap() { }
        public void Dispose() { }
    }

    private sealed class TestAccelerationStructure(AccelerationStructureType type, ulong deviceAddress) : IAccelerationStructure
    {
        public AccelerationStructureType Type { get; } = type;
        public ulong Size => 1024;
        public ulong DeviceAddress { get; } = deviceAddress;
        public bool IsBuilt => true;
        public void Dispose() { }
    }
}
