using System.Runtime.InteropServices;
using Engine.Backends.Null;
using Engine.Core;
using Engine.Mathematics;
using Engine.Rendering;
using Engine.RHI;
using Engine.Scene;
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
    public void Free_RestoresAlignmentPadding()
    {
        MemoryBlockAllocator allocator = new(0, 512);
        Assert.True(allocator.TryAllocate(1, 1, out ulong prefix));
        Assert.Equal(0UL, prefix);
        Assert.True(allocator.TryAllocate(10, 256, out ulong aligned));
        Assert.Equal(256UL, aligned);

        Assert.Equal(266UL, allocator.Used);
        allocator.Free(aligned, 10);

        Assert.Equal(1UL, allocator.Used);
        Assert.Equal(511UL, allocator.LargestFreeRange());
        Assert.True(allocator.TryAllocate(511, 1, out ulong reusedPadding));
        Assert.Equal(1UL, reusedPadding);
        Assert.Equal(512UL, allocator.Used);
    }

    [Fact]
    public void Free_WrongSize_ThrowsAndKeepsAllocationLive()
    {
        MemoryBlockAllocator allocator = new(0, 1024);
        Assert.True(allocator.TryAllocate(1, 1, out _));
        Assert.True(allocator.TryAllocate(10, 256, out ulong offset));

        ArgumentException exception = Assert.Throws<ArgumentException>(() => allocator.Free(offset, 9));

        Assert.Contains("size", exception.Message);
        Assert.Equal(266UL, allocator.Used);
        allocator.Free(offset, 10);
        Assert.Equal(1UL, allocator.Used);
    }

    [Fact]
    public void Free_WrongOffset_ThrowsAndKeepsAllocationLive()
    {
        MemoryBlockAllocator allocator = new(0, 1024);
        Assert.True(allocator.TryAllocate(32, 1, out ulong offset));

        ArgumentException exception = Assert.Throws<ArgumentException>(() => allocator.Free(offset + 1, 32));

        Assert.Contains("live allocation", exception.Message);
        Assert.Equal(32UL, allocator.Used);
        allocator.Free(offset, 32);
        Assert.Equal(0UL, allocator.Used);
    }

    [Fact]
    public void Free_AlreadyFreedOffset_Throws()
    {
        MemoryBlockAllocator allocator = new(0, 1024);
        Assert.True(allocator.TryAllocate(32, 1, out ulong offset));
        allocator.Free(offset, 32);

        ArgumentException exception = Assert.Throws<ArgumentException>(() => allocator.Free(offset, 32));

        Assert.Contains("live allocation", exception.Message);
        Assert.Equal(0UL, allocator.Used);
    }

    [Fact]
    public void Allocate_RejectsZeroAlignment()
    {
        MemoryBlockAllocator allocator = new(0, 1024);

        Assert.Throws<ArgumentOutOfRangeException>(() => allocator.TryAllocate(1, 0, out _));
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
    public void ModelMatrix_UsesRenderPoseWithoutChangingSimulationTransform()
    {
        Scene.Scene scene = new("Render interpolation");
        GameObject gameObject = scene.CreateObject("Body");
        gameObject.Transform.Position = new Vector3(0f, 4f, 0f);
        gameObject.Transform.SetRenderPose(new Vector3(0f, 2f, 0f), Quaternion.Identity);
        RenderItem item = new()
        {
            Mesh = null!,
            Material = null!,
            BoundTransform = gameObject.Transform,
            RenderInterpolationAlpha = 0.25f,
        };

        Assert.Equal(2.5f, item.ModelMatrix.Translation.Y, 4);
        Assert.Equal(4f, gameObject.Transform.Position.Y);
        Assert.Equal(2f, gameObject.Transform.RenderWorldMatrix.Translation.Y);
    }

    [Fact]
    public void InterpolatedWorldMatrix_UsesInterpolatedParentPoseForChildCamera()
    {
        Scene.Scene scene = new("Interpolated camera pose");
        GameObject player = scene.CreateObject("Player");
        player.Transform.Position = new Vector3(0f, 4f, 0f);
        player.Transform.Rotation = Quaternion.FromYawPitchRoll(90f * MathUtil.DegToRad, 0f, 0f);
        player.Transform.SetRenderPose(new Vector3(0f, 2f, 0f), Quaternion.Identity);
        GameObject camera = scene.CreateObject("Camera", player);
        camera.Transform.LocalPosition = new Vector3(0f, 0.65f, 0f);

        Matrix4x4 cameraPose = camera.Transform.GetInterpolatedWorldMatrix(0.25f);

        Assert.Equal(3.15f, cameraPose.Translation.Y, 4);
        Assert.Equal(4.65f, camera.Transform.Position.Y, 4);
        cameraPose.Decompose(out _, out Quaternion cameraRotation, out _);
        Assert.Equal(22.5f * MathUtil.DegToRad, cameraRotation.ToYawPitchRoll().X, 4);
    }

    [Fact]
    public void ModelMatrix_UsesExtrapolatedRenderPoseWithoutLerpingItAgain()
    {
        Scene.Scene scene = new("Render extrapolation");
        GameObject gameObject = scene.CreateObject("Body");
        gameObject.Transform.Position = new Vector3(0f, 4f, 0f);
        gameObject.Transform.SetRenderPose(new Vector3(0f, 5f, 0f), Quaternion.Identity, overridesCurrent: true);
        RenderItem item = new()
        {
            Mesh = null!,
            Material = null!,
            BoundTransform = gameObject.Transform,
            RenderInterpolationAlpha = 0.25f,
        };

        Assert.Equal(5f, item.ModelMatrix.Translation.Y, 4);
        Assert.Equal(4f, gameObject.Transform.Position.Y);
    }

    [Fact]
    public void ModelMatrix_InterpolatesWorldPoseWithTransformedParent()
    {
        Scene.Scene scene = new("Parent interpolation");
        GameObject parent = scene.CreateObject("Parent");
        parent.Transform.Position = new Vector3(10f, 0f, 0f);
        parent.Transform.Rotation = Quaternion.FromAxisAngle(Vector3.UnitY, MathF.PI * 0.5f);
        GameObject gameObject = scene.CreateObject("Body");
        gameObject.SetParent(parent);
        gameObject.Transform.LocalPosition = new Vector3(0f, 0f, -1f);
        Vector3 currentPosition = gameObject.Transform.Position;
        gameObject.Transform.SetRenderPose(new Vector3(2f, 0f, 0f), Quaternion.Identity);
        RenderItem item = new()
        {
            Mesh = null!,
            Material = null!,
            BoundTransform = gameObject.Transform,
            RenderInterpolationAlpha = 0.5f,
        };

        Assert.Equal(Vector3.Lerp(new Vector3(2f, 0f, 0f), currentPosition, 0.5f).X, item.ModelMatrix.Translation.X, 4);
        Assert.Equal(currentPosition, gameObject.Transform.Position);
    }

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
    public void CameraJitter_LeavesBaseViewProjectionUnchangedAndOffsetsNdc()
    {
        Camera camera = new() { AspectRatio = 1f };
        Matrix4x4 unjittered = camera.ViewProjection;
        Vector2 jitterPixels = new(0.25f, -0.25f);
        Size2D extent = new(100, 50);

        Matrix4x4 jittered = camera.CreateJitteredViewProjection(jitterPixels, extent);

        Assert.Equal(unjittered, camera.ViewProjection);
        Assert.Equal(2f * jitterPixels.X / extent.Width, jittered.M13 - unjittered.M13, 6);
        Assert.Equal(2f * jitterPixels.Y / extent.Height, jittered.M23 - unjittered.M23, 6);
        Assert.Throws<ArgumentOutOfRangeException>(() => camera.CreateJitteredViewProjection(Vector2.Zero, new Size2D(0, 10)));
    }

    [Fact]
    public void TemporalConstants_MatchesSlangPushConstantSize()
    {
        Assert.Equal(28, Marshal.SizeOf<TemporalConstants>());
        Assert.Equal(0, Marshal.OffsetOf<TemporalConstants>(nameof(TemporalConstants.Width)).ToInt32());
        Assert.Equal(8, Marshal.OffsetOf<TemporalConstants>(nameof(TemporalConstants.SourceWidth)).ToInt32());
        Assert.Equal(16, Marshal.OffsetOf<TemporalConstants>(nameof(TemporalConstants.HistoryValid)).ToInt32());
        Assert.Equal(20, Marshal.OffsetOf<TemporalConstants>(nameof(TemporalConstants.HistoryWeight)).ToInt32());
        Assert.Equal(24, Marshal.OffsetOf<TemporalConstants>(nameof(TemporalConstants.DepthThreshold)).ToInt32());
    }

    [Fact]
    public void RtViewData_MatchesSlangUniformLayout()
    {
        Assert.Equal(320, Marshal.SizeOf<RtViewData>());
        Assert.Equal(0, Marshal.OffsetOf<RtViewData>(nameof(RtViewData.ViewProjection)).ToInt32());
        Assert.Equal(64, Marshal.OffsetOf<RtViewData>(nameof(RtViewData.InverseViewProjection)).ToInt32());
        Assert.Equal(128, Marshal.OffsetOf<RtViewData>(nameof(RtViewData.PreviousViewProjection)).ToInt32());
        Assert.Equal(192, Marshal.OffsetOf<RtViewData>(nameof(RtViewData.CameraPositionAndMaxDistance)).ToInt32());
        Assert.Equal(208, Marshal.OffsetOf<RtViewData>(nameof(RtViewData.ExtentAndFrame)).ToInt32());
        Assert.Equal(224, Marshal.OffsetOf<RtViewData>(nameof(RtViewData.LightDirectionAndAngularRadius)).ToInt32());
        Assert.Equal(240, Marshal.OffsetOf<RtViewData>(nameof(RtViewData.Settings)).ToInt32());
        Assert.Equal(256, Marshal.OffsetOf<RtViewData>(nameof(RtViewData.PointLightPositionAndRange)).ToInt32());
        Assert.Equal(272, Marshal.OffsetOf<RtViewData>(nameof(RtViewData.ShadowQuality)).ToInt32());
        Assert.Equal(288, Marshal.OffsetOf<RtViewData>(nameof(RtViewData.ReflectionQuality)).ToInt32());
        Assert.Equal(304, Marshal.OffsetOf<RtViewData>(nameof(RtViewData.GiQuality)).ToInt32());
    }

    [Fact]
    public void RtShadowControl_MatchesSlangUniformLayout()
    {
        Assert.Equal(32, Marshal.SizeOf<RtShadowControl>());
        Assert.Equal(0, Marshal.OffsetOf<RtShadowControl>(nameof(RtShadowControl.Enabled)).ToInt32());
        Assert.Equal(4, Marshal.OffsetOf<RtShadowControl>(nameof(RtShadowControl.ExtentWidth)).ToInt32());
        Assert.Equal(8, Marshal.OffsetOf<RtShadowControl>(nameof(RtShadowControl.ExtentHeight)).ToInt32());
        Assert.Equal(12, Marshal.OffsetOf<RtShadowControl>(nameof(RtShadowControl.PointLightEnabled)).ToInt32());
        Assert.Equal(16, Marshal.OffsetOf<RtShadowControl>(nameof(RtShadowControl.PointLightIndex)).ToInt32());
        Assert.Equal(20, Marshal.OffsetOf<RtShadowControl>(nameof(RtShadowControl.RasterDisabled)).ToInt32());
    }

    [Fact]
    public void RtFilterConstants_MatchSlangPushLayout()
    {
        Assert.Equal(56, Marshal.SizeOf<RtFilterConstants>());
        Assert.Equal(0, Marshal.OffsetOf<RtFilterConstants>(nameof(RtFilterConstants.ExtentWidth)).ToInt32());
        Assert.Equal(4, Marshal.OffsetOf<RtFilterConstants>(nameof(RtFilterConstants.ExtentHeight)).ToInt32());
        Assert.Equal(8, Marshal.OffsetOf<RtFilterConstants>(nameof(RtFilterConstants.SourceWidth)).ToInt32());
        Assert.Equal(12, Marshal.OffsetOf<RtFilterConstants>(nameof(RtFilterConstants.SourceHeight)).ToInt32());
        Assert.Equal(16, Marshal.OffsetOf<RtFilterConstants>(nameof(RtFilterConstants.HistoryValid)).ToInt32());
        Assert.Equal(20, Marshal.OffsetOf<RtFilterConstants>(nameof(RtFilterConstants.Mode)).ToInt32());
        Assert.Equal(24, Marshal.OffsetOf<RtFilterConstants>(nameof(RtFilterConstants.HistoryWeight)).ToInt32());
        Assert.Equal(28, Marshal.OffsetOf<RtFilterConstants>(nameof(RtFilterConstants.DepthThreshold)).ToInt32());
        Assert.Equal(32, Marshal.OffsetOf<RtFilterConstants>(nameof(RtFilterConstants.NormalThreshold)).ToInt32());
        Assert.Equal(36, Marshal.OffsetOf<RtFilterConstants>(nameof(RtFilterConstants.StepWidth)).ToInt32());
        Assert.Equal(40, Marshal.OffsetOf<RtFilterConstants>(nameof(RtFilterConstants.ValidityEnabled)).ToInt32());
        Assert.Equal(44, Marshal.OffsetOf<RtFilterConstants>(nameof(RtFilterConstants.UseSceneDepth)).ToInt32());
    }

    [Fact]
    public void RtCompositeConstants_MatchSlangPushLayout()
    {
        Assert.Equal(24, Marshal.SizeOf<RtCompositeConstants>());
        Assert.Equal(0, Marshal.OffsetOf<RtCompositeConstants>(nameof(RtCompositeConstants.ExtentWidth)).ToInt32());
        Assert.Equal(4, Marshal.OffsetOf<RtCompositeConstants>(nameof(RtCompositeConstants.ExtentHeight)).ToInt32());
        Assert.Equal(8, Marshal.OffsetOf<RtCompositeConstants>(nameof(RtCompositeConstants.SourceWidth)).ToInt32());
        Assert.Equal(12, Marshal.OffsetOf<RtCompositeConstants>(nameof(RtCompositeConstants.SourceHeight)).ToInt32());
        Assert.Equal(16, Marshal.OffsetOf<RtCompositeConstants>(nameof(RtCompositeConstants.ReflectionsEnabled)).ToInt32());
        Assert.Equal(20, Marshal.OffsetOf<RtCompositeConstants>(nameof(RtCompositeConstants.Strength)).ToInt32());
    }

    [Fact]
    public void SsgiData_MatchesSlangUniformLayout()
    {
        Assert.Equal(160, Marshal.SizeOf<SsgiData>());
        Assert.Equal(0, Marshal.OffsetOf<SsgiData>(nameof(SsgiData.ViewProjection)).ToInt32());
        Assert.Equal(64, Marshal.OffsetOf<SsgiData>(nameof(SsgiData.InverseViewProjection)).ToInt32());
        Assert.Equal(128, Marshal.OffsetOf<SsgiData>(nameof(SsgiData.CameraPositionAndRadius)).ToInt32());
        Assert.Equal(144, Marshal.OffsetOf<SsgiData>(nameof(SsgiData.ExtentAndFrame)).ToInt32());
    }

    [Fact]
    public void GiCompositeConstants_MatchesSlangPushConstantSize()
    {
        Assert.Equal(16, Marshal.SizeOf<GiCompositeConstants>());
        Assert.Equal(0, Marshal.OffsetOf<GiCompositeConstants>(nameof(GiCompositeConstants.Width)).ToInt32());
        Assert.Equal(4, Marshal.OffsetOf<GiCompositeConstants>(nameof(GiCompositeConstants.Height)).ToInt32());
        Assert.Equal(8, Marshal.OffsetOf<GiCompositeConstants>(nameof(GiCompositeConstants.Intensity)).ToInt32());
    }

    [Fact]
    public void ViewProjection_Invert_RoundTripsWorldPositionThroughDepthUv()
    {
        Camera camera = new() { AspectRatio = 1f, Position = new Vector3(0f, 0f, 5f) };
        Size2D extent = new(128, 128);
        Matrix4x4 viewProjection = camera.CreateJitteredViewProjection(new Vector2(0.125f, -0.25f), extent);
        Matrix4x4 inverseViewProjection = viewProjection.Invert();
        Vector3 worldPoint = new(0.75f, -0.5f, -2f);

        Vector4 clip = new(
            worldPoint.X * viewProjection.M11 + worldPoint.Y * viewProjection.M12 + worldPoint.Z * viewProjection.M13 + viewProjection.M14,
            worldPoint.X * viewProjection.M21 + worldPoint.Y * viewProjection.M22 + worldPoint.Z * viewProjection.M23 + viewProjection.M24,
            worldPoint.X * viewProjection.M31 + worldPoint.Y * viewProjection.M32 + worldPoint.Z * viewProjection.M33 + viewProjection.M34,
            worldPoint.X * viewProjection.M41 + worldPoint.Y * viewProjection.M42 + worldPoint.Z * viewProjection.M43 + viewProjection.M44);
        float ndcDepth = clip.Z / clip.W;
        Vector2 uv = new(clip.X / clip.W * 0.5f + 0.5f, clip.Y / clip.W * 0.5f + 0.5f);
        Vector4 ndc = new(uv.X * 2f - 1f, uv.Y * 2f - 1f, ndcDepth, 1f);
        Vector4 unprojected = new(
            ndc.X * inverseViewProjection.M11 + ndc.Y * inverseViewProjection.M12 + ndc.Z * inverseViewProjection.M13 + inverseViewProjection.M14,
            ndc.X * inverseViewProjection.M21 + ndc.Y * inverseViewProjection.M22 + ndc.Z * inverseViewProjection.M23 + inverseViewProjection.M24,
            ndc.X * inverseViewProjection.M31 + ndc.Y * inverseViewProjection.M32 + ndc.Z * inverseViewProjection.M33 + inverseViewProjection.M34,
            ndc.X * inverseViewProjection.M41 + ndc.Y * inverseViewProjection.M42 + ndc.Z * inverseViewProjection.M43 + inverseViewProjection.M44);
        Vector3 reconstructed = new(unprojected.X / unprojected.W, unprojected.Y / unprojected.W, unprojected.Z / unprojected.W);

        Assert.InRange(ndcDepth, 0f, 1f);
        Assert.InRange(uv.X, 0f, 1f);
        Assert.InRange(uv.Y, 0f, 1f);
        Assert.Equal(worldPoint.X, reconstructed.X, 4);
        Assert.Equal(worldPoint.Y, reconstructed.Y, 4);
        Assert.Equal(worldPoint.Z, reconstructed.Z, 4);
    }

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
    public void ModelConstants_MatchesSlangLayoutAndKeepsPreviousModel()
    {
        Assert.Equal(128, Marshal.SizeOf<ModelConstants>());
        Assert.Equal(0, Marshal.OffsetOf<ModelConstants>(nameof(ModelConstants.Model)).ToInt32());
        Assert.Equal(64, Marshal.OffsetOf<ModelConstants>(nameof(ModelConstants.PreviousModel)).ToInt32());

        Matrix4x4 model = Matrix4x4.CreateRotationY(0.47f) * Matrix4x4.CreateScale(new Vector3(2f, 3f, 4f));
        Matrix4x4 previous = Matrix4x4.CreateTranslation(new Vector3(1f, 2f, 3f));
        ModelConstants constants = ModelConstants.FromModel(model, previous);
        Assert.Equal(model, constants.Model);
        Assert.Equal(previous, constants.PreviousModel);
    }

    [Fact]
    public void ModelConstants_FirstFrameUsesCurrentAsPrevious()
    {
        Matrix4x4 model = Matrix4x4.CreateTRS(new Vector3(1f, 2f, 3f), Quaternion.Identity, new Vector3(1f, 0f, 2f));

        ModelConstants constants = ModelConstants.FromModel(model);

        Assert.Equal(model, constants.Model);
        Assert.Equal(model, constants.PreviousModel);
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

public class ImageBasedLightingResourcesTests
{
    [Fact]
    public void Create_NullDevice_UploadsNeutralFallbackAndOwnsSampler()
    {
        NullDevice innerDevice = new();
        using NoStorageTextureDevice device = new(innerDevice);
        using ImageBasedLightingResources resources = ImageBasedLightingResources.Create(device, null, string.Empty);

        Assert.False(resources.IsBaked);
        Assert.Equal(TextureDimension.Cube, resources.Environment.Dimension);
        Assert.Equal(TextureDimension.Cube, resources.Irradiance.Dimension);
        Assert.Equal(TextureDimension.Cube, resources.PrefilteredSpecular.Dimension);
        Assert.Equal(TextureFormat.R16G16B16A16Float, resources.BrdfLut.Format);
        Assert.NotNull(resources.Sampler);
        Assert.Equal(6u, resources.Environment.ArrayLayers);
        Assert.Equal(7u, resources.PrefilteredSpecular.MipLevels);
        Assert.Equal(4, device.TexturesWithoutStorage);
        Assert.Equal(4, device.SampledTransferTextures);
    }

    private sealed class NoStorageTextureDevice(IDevice innerDevice) : IDevice
    {
        public int TexturesWithoutStorage { get; private set; }
        public int SampledTransferTextures { get; private set; }
        public string Name => innerDevice.Name;
        public FeatureInfo Features => innerDevice.Features;
        public IMemoryAllocator MemoryAllocator => innerDevice.MemoryAllocator;
        public IBuffer CreateBuffer(in BufferDescription description) => innerDevice.CreateBuffer(description);
        public ITexture CreateTexture(in TextureDescription description)
        {
            if ((description.Usage & TextureUsage.Storage) != 0)
                throw new InvalidOperationException("Storage textures are unsupported by this device.");
            TexturesWithoutStorage++;
            if ((description.Usage & (TextureUsage.Sampled | TextureUsage.TransferDestination))
                == (TextureUsage.Sampled | TextureUsage.TransferDestination))
                SampledTransferTextures++;
            return innerDevice.CreateTexture(description);
        }
        public ITextureView CreateTextureView(ITexture texture, TextureViewType type) => innerDevice.CreateTextureView(texture, type);
        public ITextureView CreateTextureView(ITexture texture, in TextureViewDescription description) => innerDevice.CreateTextureView(texture, description);
        public ISampler CreateSampler(in SamplerDescription description) => innerDevice.CreateSampler(description);
        public IShaderProgram CreateShaderProgram(in ShaderProgramDescription description) => innerDevice.CreateShaderProgram(description);
        public IGraphicsPipeline CreateGraphicsPipeline(in GraphicsPipelineDescription description) => innerDevice.CreateGraphicsPipeline(description);
        public IComputePipeline CreateComputePipeline(in ComputePipelineDescription description) => innerDevice.CreateComputePipeline(description);
        public ICommandBuffer CreateCommandBuffer() => innerDevice.CreateCommandBuffer();
        public void UploadBufferData(IBuffer buffer, ReadOnlySpan<byte> data, ulong bufferOffset = 0) => innerDevice.UploadBufferData(buffer, data, bufferOffset);
        public void UploadTextureData(ITexture texture, ReadOnlySpan<byte> data) => innerDevice.UploadTextureData(texture, data);
        public void UploadTextureSubresources(ITexture texture, ReadOnlySpan<TextureSubresourceData> subresources) => innerDevice.UploadTextureSubresources(texture, subresources);
        public void FlushUploads() => innerDevice.FlushUploads();
        public void Submit(ICommandBuffer commandBuffer) => innerDevice.Submit(commandBuffer);
        public void WaitIdle() => innerDevice.WaitIdle();
        public void Dispose() => innerDevice.Dispose();
    }
}

public class TemporalHistoryTests
{
    [Fact]
    public void Recreate_CommitAndInvalidate_TracksPingPongInitialization()
    {
        NullDevice device = new();
        using TemporalHistory history = new();

        history.Recreate(device, new Size2D(32, 24));
        ITexture firstRead = history.ReadTexture;
        ITexture firstWrite = history.WriteTexture;
        Assert.NotSame(firstRead, firstWrite);
        Assert.False(history.IsValid);
        Assert.False(history.ReadInitialized);
        Assert.False(history.WriteInitialized);

        history.Commit();
        Assert.True(history.IsValid);
        Assert.Same(firstWrite, history.ReadTexture);
        Assert.True(history.ReadInitialized);
        Assert.False(history.WriteInitialized);

        history.Invalidate();
        Assert.False(history.IsValid);
        Assert.True(history.ReadInitialized);

        history.Commit();
        Assert.Same(firstRead, history.ReadTexture);
        Assert.True(history.ReadInitialized);
        Assert.True(history.WriteInitialized);
    }

    [Fact]
    public void Recreate_ResetsHistoryAndReallocatesTextures()
    {
        NullDevice device = new();
        using TemporalHistory history = new();
        history.Recreate(device, new Size2D(16, 16));
        ITexture oldRead = history.ReadTexture;
        history.Commit();

        history.Recreate(device, new Size2D(64, 48));

        Assert.NotSame(oldRead, history.ReadTexture);
        Assert.Equal(64u, history.ReadTexture.Width);
        Assert.Equal(48u, history.ReadTexture.Height);
        Assert.False(history.IsValid);
        Assert.False(history.ReadInitialized);
        Assert.False(history.WriteInitialized);
    }
}

public class LightSelectionTests
{
    [Fact]
    public void SelectImportant_RanksSourcesByEstimatedContribution()
    {
        Light dimDirectional = Light.Directional(Vector3.Down, Vector3.One, 0.25f);
        Light distantPoint = Light.Point(new Vector3(100f, 0f, 0f), Vector3.One, 500f, 10f);
        Light importantPoint = Light.Point(new Vector3(1f, 0f, 0f), new Vector3(2f, 1f, 1f), 20f, 10f);
        Light[] lights = [dimDirectional, distantPoint, importantPoint];

        IReadOnlyList<Light> selected = LightSelection.SelectImportant(
            lights,
            [Vector3.Zero],
            Vector3.Zero,
            2);

        Assert.Equal([importantPoint, dimDirectional], selected);
    }

    [Fact]
    public void SelectImportant_UsesStableSourceOrderForEqualScores()
    {
        Light first = Light.Directional(Vector3.Down, Vector3.One, 2f);
        Light second = Light.Directional(Vector3.Down, Vector3.One, 2f);

        IReadOnlyList<Light> selected = LightSelection.SelectImportant([first, second], [Vector3.Zero], Vector3.Zero, 2);

        Assert.Equal([first, second], selected);
    }

    [Fact]
    public void ShadowLightIndex_UsesActiveShaderArrayOrder()
    {
        Light point = Light.Point(Vector3.Zero, Vector3.One);
        Light directional = Light.Directional(Vector3.Down, Vector3.One);

        Assert.Equal(1, LightSelection.FindShadowLightIndex([point, directional]));
        Assert.Equal(-1, LightSelection.FindShadowLightIndex([point]));
    }

    [Fact]
    public void PointAttenuation_MatchesInverseSquareAndRangeFadeContract()
    {
        Assert.Equal(0.9998f, LightSelection.PointAttenuation(1f, 10f), 5);
        Assert.Equal(0.2492f, LightSelection.PointAttenuation(2f, 10f), 5);
        Assert.Equal(0f, LightSelection.PointAttenuation(10f, 10f), 5);
        Assert.Equal(0f, LightSelection.PointAttenuation(1f, 0f));
    }

    [Fact]
    public void DirectionalShadowMatrix_TracksCameraFrustumAndUsesStableTexelSnapping()
    {
        Camera camera = new() { Position = new Vector3(2f, 3f, 5f) };
        camera.LookAt(Vector3.Zero);
        Light light = Light.Directional(new Vector3(-0.4f, -1f, -0.3f), Vector3.One);
        Matrix4x4 first = LightSelection.CreateDirectionalShadowMatrix(camera, light, 1024);

        camera.Position += camera.Right * 0.00001f;
        Matrix4x4 subTexelMove = LightSelection.CreateDirectionalShadowMatrix(camera, light, 1024);
        Assert.Equal(first.M14, subTexelMove.M14, 5);
        Assert.Equal(first.M24, subTexelMove.M24, 5);

        camera.Position += camera.Right * 1f;
        Matrix4x4 moved = LightSelection.CreateDirectionalShadowMatrix(camera, light, 1024);
        Assert.NotEqual(first.M14, moved.M14);
        AssertFrustumFitsShadow(camera, moved, 40f);
    }

    [Fact]
    public void DirectionalShadowMatrix_ContainsMultipleFrustumsAndNearVerticalLight()
    {
        Camera sceneCamera = new()
        {
            Position = new Vector3(-7f, 2f, 4f),
            FieldOfViewY = MathUtil.DegToRad * 105f,
            AspectRatio = 2.1f,
            NearPlane = 0.05f,
            FarPlane = 100f,
        };
        sceneCamera.LookAt(new Vector3(1f, 0f, -2f));
        Camera gameCamera = new()
        {
            Position = new Vector3(3f, 1f, 8f),
            FieldOfViewY = MathUtil.DegToRad * 50f,
            AspectRatio = 1.4f,
            NearPlane = 0.2f,
            FarPlane = 250f,
        };
        gameCamera.LookAt(Vector3.Zero);
        Light nearVertical = Light.Directional(new Vector3(0.00001f, -1f, 0.00002f), Vector3.One);
        Matrix4x4 matrix = LightSelection.CreateDirectionalShadowMatrix([sceneCamera, gameCamera], nearVertical, 2048, 60f);

        AssertFrustumFitsShadow(sceneCamera, matrix, 60f);
        AssertFrustumFitsShadow(gameCamera, matrix, 60f);
    }

    private static void AssertFrustumFitsShadow(Camera camera, Matrix4x4 shadowMatrix, float maximumDistance)
    {
        float nearDistance = MathF.Max(camera.NearPlane, 0.01f);
        float farDistance = MathF.Max(nearDistance + 0.01f, MathF.Min(camera.FarPlane, maximumDistance));
        float tangent = MathF.Tan(camera.FieldOfViewY * 0.5f);
        foreach (float distance in new[] { nearDistance, farDistance })
        {
            float halfHeight = tangent * distance;
            float halfWidth = halfHeight * camera.AspectRatio;
            Vector3 planeCenter = camera.Position + camera.Forward * distance;
            foreach (float x in new[] { -1f, 1f })
            foreach (float y in new[] { -1f, 1f })
            {
                Vector3 corner = planeCenter + camera.Right * (x * halfWidth) + camera.Up * (y * halfHeight);
                Vector4 clip = Vector4.Transform(new Vector4(corner, 1f), shadowMatrix);
                float ndcX = clip.X / clip.W;
                float ndcY = clip.Y / clip.W;
                float ndcZ = clip.Z / clip.W;
                Assert.InRange(ndcX, -1.001f, 1.001f);
                Assert.InRange(ndcY, -1.001f, 1.001f);
                Assert.InRange(ndcZ, -0.001f, 1.001f);
            }
        }
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
