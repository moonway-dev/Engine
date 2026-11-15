using System.Runtime.InteropServices;
using Engine.Backends.Null;
using Engine.Core;
using Engine.Mathematics;
using Engine.Rendering;
using Engine.RHI;
using Xunit;

namespace Engine.Tests.Rendering;

public class MemoryBlockAllocatorTests
{
    [Fact]
    public void Allocate_And_Free_RoundTrip()
    {
        MemoryBlockAllocator allocator = new(0, 1024);
        Assert.True(allocator.TryAllocate(256, 256, out ulong first));
        Assert.Equal(0UL, first);
        Assert.True(allocator.TryAllocate(256, 256, out ulong second));
        Assert.Equal(256UL, second);
        allocator.Free(first, 256);
        Assert.True(allocator.TryAllocate(128, 256, out ulong third));
        Assert.Equal(0UL, third);
        Assert.Equal(384UL, allocator.Used);
    }

    [Fact]
    public void Allocate_RespectsAlignment()
    {
        MemoryBlockAllocator allocator = new(0, 1024);
        Assert.True(allocator.TryAllocate(10, 1, out ulong unaligned));
        Assert.Equal(0UL, unaligned);
        Assert.True(allocator.TryAllocate(100, 256, out ulong aligned));
        Assert.Equal(256UL, aligned);
    }

    [Fact]
    public void Free_MergesAdjacentRanges()
    {
        MemoryBlockAllocator allocator = new(0, 4096);
        allocator.TryAllocate(1024, 256, out ulong a);
        allocator.TryAllocate(1024, 256, out ulong b);
        allocator.TryAllocate(1024, 256, out ulong c);
        Assert.Equal(3072UL, allocator.Used);
        allocator.Free(a, 1024);
        allocator.Free(b, 1024);
        Assert.Equal(2, allocator.FreeRangeCount);
        allocator.Free(c, 1024);
        Assert.Equal(1, allocator.FreeRangeCount);
        Assert.Equal(4096UL, allocator.LargestFreeRange());
        Assert.Equal(0UL, allocator.Used);
    }

    [Fact]
    public void Allocate_TooLarge_Fails()
    {
        MemoryBlockAllocator allocator = new(0, 1024);
        Assert.False(allocator.TryAllocate(2048, 256, out _));
    }

    [Fact]
    public void FragmentedAllocator_StillServesSmallRequests()
    {
        MemoryBlockAllocator allocator = new(0, 4096);
        List<(ulong Offset, ulong Size)> allocations = [];
        for (int i = 0; i < 8; i++)
        {
            allocator.TryAllocate(512, 256, out ulong offset);
            allocations.Add((offset, 512));
        }
        Assert.Equal(4096UL, allocator.Used);
        foreach ((ulong offset, ulong size) in allocations.Take(4))
            allocator.Free(offset, size);
        Assert.True(allocator.TryAllocate(2048, 256, out ulong large));
        Assert.Equal(0UL, large);
    }
}

public class ScenePickingTests
{
    [Fact]
    public void CreateRay_FromCenter_PointsForward()
    {
        Camera camera = new() { Position = new Vector3(0f, 0f, 5f), AspectRatio = 1f };
        camera.LookAt(Vector3.Zero);
        PickingRay ray = ScenePicking.CreateRay(camera, new Vector2(0.5f, 0.5f));
        Assert.True(Vector3.Dot(ray.Direction, camera.Forward) > 0.999f);
    }

    [Fact]
    public void IntersectAabb_ReturnsNearestDistance()
    {
        bool hit = ScenePicking.IntersectAabb(
            new Vector3(0f, 0f, 5f),
            Vector3.Forward,
            new Vector3(-1f, -1f, -1f),
            new Vector3(1f, 1f, 1f),
            out float distance);
        Assert.True(hit);
        Assert.Equal(4f, distance, 4);
    }

    [Fact]
    public void IntersectAabb_ParallelOutside_Misses()
    {
        bool hit = ScenePicking.IntersectAabb(
            new Vector3(2f, 0f, 5f),
            Vector3.Forward,
            new Vector3(-1f, -1f, -1f),
            new Vector3(1f, 1f, 1f),
            out _);
        Assert.False(hit);
    }
}

public class FrameUniformRingTests
{
    [Fact]
    public void FrameSlots_UseIndependentBufferRanges()
    {
        NullDevice device = new();
        using FrameUniformRing ring = new(device, 2, 1024);

        ring.BeginFrame(0);
        FrameAllocation first = ring.Allocate(16);
        ring.Write(first, [1, 2, 3, 4]);
        ring.FlushFrame();

        ring.BeginFrame(1);
        FrameAllocation second = ring.Allocate(16);
        ring.Write(second, [5, 6, 7, 8]);
        ring.FlushFrame();

        Assert.Equal(0UL, first.Offset);
        Assert.Equal(1024UL, second.Offset);
        Assert.Equal(0UL, first.LocalOffset);
        Assert.Equal(0UL, second.LocalOffset);
        Assert.Equal([0UL, 1024UL], device.BufferUploads.Select(upload => upload.Offset));
        Assert.Equal([1, 2, 3, 4], device.BufferUploads[0].Data[..4]);
        Assert.Equal([5, 6, 7, 8], device.BufferUploads[1].Data[..4]);
    }

    [Fact]
    public void Write_RejectsStaleAndForeignAllocations()
    {
        NullDevice device = new();
        using FrameUniformRing ring = new(device, 2, 512);
        using FrameUniformRing other = new(device, 2, 512);
        ring.BeginFrame(0);
        FrameAllocation stale = ring.Allocate(16);
        ring.BeginFrame(1);
        Assert.Throws<ArgumentException>(() => ring.Write(stale, new byte[4]));
        other.BeginFrame(1);
        FrameAllocation foreign = other.Allocate(16);
        Assert.Throws<ArgumentException>(() => ring.Write(foreign, new byte[4]));
        FrameAllocation valid = ring.Allocate(16);
        Assert.Throws<ArgumentOutOfRangeException>(() => ring.Write(valid, new byte[17]));
    }

    [Fact]
    public void BeginFrame_ResetsOnlySelectedSlotCursor()
    {
        NullDevice device = new();
        using FrameUniformRing ring = new(device, 2, 512);
        ring.BeginFrame(0);
        FrameAllocation first = ring.Allocate(12);
        FrameAllocation second = ring.Allocate(12);
        ring.BeginFrame(0);
        FrameAllocation reused = ring.Allocate(12);

        Assert.Equal(0UL, first.Offset);
        Assert.Equal(256UL, second.Offset);
        Assert.Equal(first.Offset, reused.Offset);
    }
}

public class ShaderUniformLayoutTests
{
    [Fact]
    public void Vertex_MatchesPbrInputAbi()
    {
        Assert.Equal(48, Marshal.SizeOf<Vertex>());
        Assert.Equal(0, Marshal.OffsetOf<Vertex>(nameof(Vertex.Position)).ToInt32());
        Assert.Equal(12, Marshal.OffsetOf<Vertex>(nameof(Vertex.Normal)).ToInt32());
        Assert.Equal(24, Marshal.OffsetOf<Vertex>(nameof(Vertex.UV)).ToInt32());
        Assert.Equal(32, Marshal.OffsetOf<Vertex>(nameof(Vertex.Tangent)).ToInt32());
        Assert.Equal(48u, Vertex.Layout.Stride);
        Assert.Equal(VertexFormat.Float4, Vertex.Layout.Attributes[3].Format);
    }

    [Fact]
    public void MaterialData_MatchesSlangLayout()
    {
        Assert.Equal(80, Marshal.SizeOf<MaterialData>());
        Assert.Equal(0, Marshal.OffsetOf<MaterialData>(nameof(MaterialData.AlbedoColor)).ToInt32());
        Assert.Equal(12, Marshal.OffsetOf<MaterialData>(nameof(MaterialData.Metallic)).ToInt32());
        Assert.Equal(16, Marshal.OffsetOf<MaterialData>(nameof(MaterialData.Roughness)).ToInt32());
        Assert.Equal(20, Marshal.OffsetOf<MaterialData>(nameof(MaterialData.EmissiveIntensity)).ToInt32());
        Assert.Equal(24, Marshal.OffsetOf<MaterialData>(nameof(MaterialData.UvScale)).ToInt32());
        Assert.Equal(32, Marshal.OffsetOf<MaterialData>(nameof(MaterialData.NormalScale)).ToInt32());
        Assert.Equal(36, Marshal.OffsetOf<MaterialData>(nameof(MaterialData.AoStrength)).ToInt32());
        Assert.Equal(40, Marshal.OffsetOf<MaterialData>(nameof(MaterialData.UvOffset)).ToInt32());
        Assert.Equal(48, Marshal.OffsetOf<MaterialData>(nameof(MaterialData.EmissionColor)).ToInt32());
        Assert.Equal(60, Marshal.OffsetOf<MaterialData>(nameof(MaterialData.LegacyEmission)).ToInt32());
        Assert.Equal(64, Marshal.OffsetOf<MaterialData>(nameof(MaterialData.MetallicChannel)).ToInt32());
        Assert.Equal(68, Marshal.OffsetOf<MaterialData>(nameof(MaterialData.RoughnessChannel)).ToInt32());
        Assert.Equal(72, Marshal.OffsetOf<MaterialData>(nameof(MaterialData.AmbientOcclusionChannel)).ToInt32());
    }

    [Fact]
    public void ModelConstants_MatchesSlangLayoutAndBuildsInverseTranspose()
    {
        Assert.Equal(128, Marshal.SizeOf<ModelConstants>());
        Assert.Equal(0, Marshal.OffsetOf<ModelConstants>(nameof(ModelConstants.Model)).ToInt32());
        Assert.Equal(64, Marshal.OffsetOf<ModelConstants>(nameof(ModelConstants.NormalMatrix)).ToInt32());

        Matrix4x4 model = Matrix4x4.CreateRotationY(0.47f) * Matrix4x4.CreateScale(new Vector3(2f, 3f, 4f));
        ModelConstants constants = ModelConstants.FromModel(model);
        Vector3 tangent = Vector3.TransformDirection(Vector3.UnitX, model);
        Vector3 normal = Vector3.TransformDirection(Vector3.UnitY, constants.NormalMatrix);
        Assert.Equal(0f, Vector3.Dot(tangent, normal), 5);
        Assert.Equal(model.Invert().Transpose(), constants.NormalMatrix);
    }

    [Fact]
    public void LightData_MatchesSlangArrayStride()
    {
        Assert.Equal(64, Marshal.SizeOf<LightData>());
        Assert.Equal(0, Marshal.OffsetOf<LightData>(nameof(LightData.DirectionOrPosition)).ToInt32());
        Assert.Equal(12, Marshal.OffsetOf<LightData>(nameof(LightData.Type)).ToInt32());
        Assert.Equal(16, Marshal.OffsetOf<LightData>(nameof(LightData.Color)).ToInt32());
        Assert.Equal(28, Marshal.OffsetOf<LightData>(nameof(LightData.Intensity)).ToInt32());
        Assert.Equal(32, Marshal.OffsetOf<LightData>(nameof(LightData.Range)).ToInt32());
        Assert.Equal(528, Marshal.SizeOf<LightsData>());
        Assert.Equal(512, Marshal.OffsetOf<LightsData>(nameof(LightsData.Count)).ToInt32());
    }
}

public class CameraTests
{
    [Fact]
    public void Camera_LookAt_ProducesForwardView()
    {
        Camera camera = new() { Position = new Vector3(0f, 0f, 5f) };
        camera.LookAt(Vector3.Zero);
        MathAssert.Equal(new Vector3(0f, 0f, -5f), Vector3.TransformPoint(Vector3.Zero, camera.ViewMatrix));
    }

    [Fact]
    public void Camera_Projection_MapsNearPlaneCorrectly()
    {
        Camera camera = new() { FieldOfViewY = MathUtil.DegToRad * 60f, AspectRatio = 1f, NearPlane = 0.5f, FarPlane = 100f };
        Matrix4x4 projection = camera.ProjectionMatrix;
        Vector4 nearPoint = Vector4.Transform(new Vector4(0f, 0f, -0.5f, 1f), projection);
        Assert.Equal(0f, nearPoint.Z / nearPoint.W, 4);
        Vector4 farPoint = Vector4.Transform(new Vector4(0f, 0f, -100f, 1f), projection);
        Assert.Equal(1f, farPoint.Z / farPoint.W, 4);
    }

    [Fact]
    public void Camera_Projection_MapsPositiveYUpWithVulkanViewport()
    {
        Camera camera = new() { FieldOfViewY = MathUtil.HalfPi, AspectRatio = 1f, NearPlane = 0.1f, FarPlane = 100f };
        Vector4 clip = Vector4.Transform(new Vector4(0f, 1f, -2f, 1f), camera.ProjectionMatrix);
        Assert.True(clip.Y / clip.W < 0f);
    }

    [Fact]
    public void Camera_ViewProjection_TransformsScenePoint()
    {
        Camera camera = new() { Position = new Vector3(0f, 0f, 10f) };
        camera.LookAt(Vector3.Zero);
        Matrix4x4 viewProjection = camera.ViewProjection;
        Vector4 clip = Vector4.Transform(new Vector4(0f, 0f, 0f, 1f), viewProjection);
        float ndcZ = clip.Z / clip.W;
        Assert.InRange(ndcZ, 0f, 1f);
        Assert.Equal(0f, clip.X / clip.W, 5);
        Assert.Equal(0f, clip.Y / clip.W, 5);
    }

    [Fact]
    public void Camera_Forward_MatchesRotation()
    {
        Camera camera = new();
        camera.LookAt(new Vector3(10f, 0f, -10f));
        MathAssert.Equal(new Vector3(0.5547f, 0f, -0.8321f), camera.Forward);
    }
}
