using Camera = Engine.Rendering.Camera;
using Light = Engine.Rendering.Light;
using System.Runtime.InteropServices;
using Engine.Backends.Null;
using Engine.Mathematics;
using Engine.Rendering;
using Engine.RHI;
using Engine.Scene;
using Xunit;

namespace Engine.Tests.Rendering;

public sealed class RayTracingSceneCachingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Prepare_UnchangedSlotSkipsEveryBufferWrite(bool mapped)
    {
        using SceneFixture fixture = new(mapped);
        RenderItem item = fixture.CreateItem();
        fixture.Scene.Prepare(0, [item], true);
        RayTracingScene.FrameResources frame = fixture.Scene.GetFrame(0);
        IReadOnlyList<ITexture> textures = frame.Textures;
        int uploads = fixture.Device.Uploads.Count;
        int maps = fixture.Device.MapCalls;

        fixture.Scene.Prepare(0, [item], true);

        Assert.Equal(uploads, fixture.Device.Uploads.Count);
        Assert.Equal(maps, fixture.Device.MapCalls);
        Assert.Same(textures, frame.Textures);
        Assert.Equal(MemoryUsage.CpuToGpu, frame.Instances!.Memory);
        Assert.Equal(MemoryUsage.CpuToGpu, frame.Surfaces!.Memory);
        Assert.Equal(MemoryUsage.GpuOnly, frame.Vertices!.Memory);
        Assert.Equal(MemoryUsage.GpuOnly, frame.Indices!.Memory);
        Assert.Equal(1, fixture.Scene.BottomLevelBuildCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Prepare_TransformChangeWritesOnlyInstancesAndSurfaces(bool mapped)
    {
        using SceneFixture fixture = new(mapped);
        RenderItem item = fixture.CreateItem();
        fixture.Scene.Prepare(0, [item], true);
        RayTracingScene.FrameResources frame = fixture.Scene.GetFrame(0);
        int vertexWrites = fixture.Device.Writes(frame.Vertices!);
        int indexWrites = fixture.Device.Writes(frame.Indices!);
        int instanceWrites = fixture.Device.Writes(frame.Instances!);
        int surfaceWrites = fixture.Device.Writes(frame.Surfaces!);
        item.Position = new Vector3(2f, 3f, 4f);
        item.Scale = new Vector3(2f, 1f, 3f);

        fixture.Scene.Prepare(0, [item], true);

        Assert.Equal(vertexWrites, fixture.Device.Writes(frame.Vertices!));
        Assert.Equal(indexWrites, fixture.Device.Writes(frame.Indices!));
        Assert.Equal(instanceWrites + 1, fixture.Device.Writes(frame.Instances!));
        Assert.Equal(surfaceWrites + 1, fixture.Device.Writes(frame.Surfaces!));
        RayTracingSurfaceData surface = fixture.Surface(0);
        Assert.Equal(item.ModelMatrix, surface.Model);
        Assert.Equal(item.ModelMatrix.Invert(), surface.InverseModel);
        RayTracingInstance instance = MemoryMarshal.Read<RayTracingInstance>(fixture.Device.Data(frame.Instances!));
        Assert.Equal(surface.Model.M14, instance.M14);
        Assert.Equal(surface.Model.M24, instance.M24);
        Assert.Equal(surface.Model.M34, instance.M34);
    }

    [Fact]
    public void Prepare_MaterialValuesRefreshWithoutParamsDirtyOrGeometryUploads()
    {
        using SceneFixture fixture = new();
        RenderItem item = fixture.CreateItem();
        fixture.Scene.Prepare(0, [item], true);
        RayTracingScene.FrameResources frame = fixture.Scene.GetFrame(0);
        int uploads = fixture.Device.Uploads.Count;
        item.Material.ParamsDirty = false;
        item.Material.Roughness = 0.27f;
        item.Material.UvOffset = new Vector2(0.1f, 0.2f);
        item.Material.EmissionColor = new Vector3(0.3f, 0.4f, 0.5f);

        fixture.Scene.Prepare(0, [item], true);

        NullBufferUpload upload = Assert.Single(fixture.Device.Uploads.Skip(uploads));
        Assert.Same(frame.Surfaces, upload.Buffer);
        Assert.Equal(item.Material.ToData(), fixture.Surface(0).Material);
        Assert.False(item.Material.ParamsDirty);
    }

    [Fact]
    public void Prepare_TextureIdentityChangeRefreshesTableWithoutBufferUploads()
    {
        using SceneFixture fixture = new();
        RenderItem item = fixture.CreateItem();
        fixture.Scene.Prepare(0, [item], true);
        RayTracingScene.FrameResources frame = fixture.Scene.GetFrame(0);
        IReadOnlyList<ITexture> previous = frame.Textures;
        int uploads = fixture.Device.Uploads.Count;
        using ITexture texture = fixture.Device.CreateTexture(new TextureDescription
        {
            Extent = new Size2D(1, 1), Format = TextureFormat.R8G8B8A8Srgb,
        });
        item.Material.AlbedoTexture = texture;
        item.Material.EmissionTexture = texture;

        fixture.Scene.Prepare(0, [item], true);

        Assert.Equal(uploads, fixture.Device.Uploads.Count);
        Assert.NotSame(previous, frame.Textures);
        Assert.Same(texture, frame.Textures[0]);
        Assert.Same(texture, frame.Textures[5]);
        Assert.Equal(24, frame.Textures.Count);
        Assert.Same(fixture.Defaults.WhiteSrgb, frame.Textures[18]);
        IReadOnlyList<ITexture> refreshed = frame.Textures;
        fixture.Scene.Prepare(0, [item], true);
        Assert.Same(refreshed, frame.Textures);
    }

    [Fact]
    public void Prepare_CacheIsIndependentForEveryFrameSlot()
    {
        using SceneFixture fixture = new();
        RenderItem item = fixture.CreateItem();
        fixture.Scene.Prepare(0, [item], true);
        fixture.Scene.Prepare(1, [item], true);
        RayTracingScene.FrameResources first = fixture.Scene.GetFrame(0);
        RayTracingScene.FrameResources second = fixture.Scene.GetFrame(1);
        Assert.NotSame(first.Vertices, second.Vertices);
        Assert.Equal(1, fixture.Device.Writes(first.Vertices!));
        Assert.Equal(1, fixture.Device.Writes(second.Vertices!));
        item.Position = new Vector3(5f, 0f, 0f);
        fixture.Scene.Prepare(1, [item], true);
        Assert.Equal(0f, fixture.Surface(0).Model.M14);
        Assert.Equal(5f, fixture.Surface(1).Model.M14);
        int uploads = fixture.Device.Uploads.Count;

        fixture.Scene.Prepare(0, [item], true);

        Assert.Equal(2, fixture.Device.Uploads.Count - uploads);
        Assert.Equal(5f, fixture.Surface(0).Model.M14);
        Assert.Equal(1, fixture.Device.Writes(first.Vertices!));
        Assert.Equal(1, fixture.Device.Writes(second.Vertices!));
        uploads = fixture.Device.Uploads.Count;
        fixture.Scene.Prepare(1, [item], true);
        Assert.Equal(uploads, fixture.Device.Uploads.Count);
    }

    [Fact]
    public void Prepare_OrderedMeshChangeRepacksGeometryAndRefreshesOffsets()
    {
        using SceneFixture fixture = new();
        RenderItem first = fixture.CreateItem();
        RenderItem second = fixture.CreateItem(vertexCount: 4);
        fixture.Scene.Prepare(0, [first, second], true);
        RayTracingScene.FrameResources frame = fixture.Scene.GetFrame(0);
        IBuffer vertices = frame.Vertices!;
        IBuffer indices = frame.Indices!;
        Assert.Equal(3u, fixture.Surface(0, 1).VertexOffset);
        Assert.Equal(3u, fixture.Surface(0, 1).IndexOffset);

        fixture.Scene.Prepare(0, [second, first], true);

        Assert.Same(vertices, frame.Vertices);
        Assert.Same(indices, frame.Indices);
        Assert.Equal(2, fixture.Device.Writes(vertices));
        Assert.Equal(2, fixture.Device.Writes(indices));
        Assert.Equal(0u, fixture.Surface(0).VertexOffset);
        Assert.Equal(4u, fixture.Surface(0, 1).VertexOffset);
        Assert.Equal(3u, fixture.Surface(0, 1).IndexOffset);
        Assert.Equal(2, fixture.Scene.BottomLevelBuildCount);
    }

    [Fact]
    public void Prepare_ReorderedInstancesOfSameMeshDoNotRepackGeometry()
    {
        using SceneFixture fixture = new();
        RenderItem first = fixture.CreateItem();
        RenderItem second = new() { Mesh = first.Mesh, Material = new Material { Metallic = 0.8f }, Position = Vector3.One };
        fixture.Scene.Prepare(0, [first, second], true);
        RayTracingScene.FrameResources frame = fixture.Scene.GetFrame(0);
        int uploads = fixture.Device.Uploads.Count;

        fixture.Scene.Prepare(0, [second, first], true);

        Assert.Equal(2, fixture.Device.Uploads.Count - uploads);
        Assert.Equal(1, fixture.Device.Writes(frame.Vertices!));
        Assert.Equal(1, fixture.Device.Writes(frame.Indices!));
        Assert.Equal(second.ModelMatrix, fixture.Surface(0).Model);
        Assert.Equal(second.Material.ToData(), fixture.Surface(0).Material);
        Assert.Equal(first.Material.ToData(), fixture.Surface(0, 1).Material);
    }

    [Fact]
    public void Prepare_DeferredMaterialsRefreshAfterUnshadedLayoutAndMaterialChanges()
    {
        using SceneFixture fixture = new();
        RenderItem first = fixture.CreateItem();
        RenderItem second = fixture.CreateItem(vertexCount: 4);
        fixture.Scene.Prepare(0, [first], false);
        Assert.Null(fixture.Scene.GetFrame(0).Vertices);
        fixture.Scene.Prepare(0, [first], true);
        fixture.Scene.Prepare(0, [second], false);
        second.Material.Roughness = 0.12f;
        int uploads = fixture.Device.Uploads.Count;

        fixture.Scene.Prepare(0, [second], true);

        Assert.Equal(3, fixture.Device.Uploads.Count - uploads);
        Assert.Equal(second.Material.ToData(), fixture.Surface(0).Material);
        Assert.Equal(4 * Marshal.SizeOf<RayTracingVertexData>(), fixture.Device.Data(fixture.Scene.GetFrame(0).Vertices!).Length);
    }

    [Fact]
    public void Prepare_InterpolatedRenderPoseUpdatesBothDynamicBuffers()
    {
        using SceneFixture fixture = new();
        RenderItem item = fixture.CreateItem();
        GameObject owner = new Engine.Scene.Scene().CreateObject("Animated");
        owner.Transform.Position = new Vector3(10f, 0f, 0f);
        owner.Transform.SetRenderPose(Vector3.Zero, Quaternion.Identity);
        item.BoundTransform = owner.Transform;
        item.RenderInterpolationAlpha = 0.25f;
        fixture.Scene.Prepare(0, [item], true);
        Assert.Equal(2.5f, fixture.Surface(0).Model.M14);
        item.RenderInterpolationAlpha = 0.75f;

        fixture.Scene.Prepare(0, [item], true);

        RayTracingScene.FrameResources frame = fixture.Scene.GetFrame(0);
        Assert.Equal(7.5f, fixture.Surface(0).Model.M14);
        Assert.Equal(7.5f, MemoryMarshal.Read<RayTracingInstance>(fixture.Device.Data(frame.Instances!)).M14);
        Assert.Equal(1, fixture.Device.Writes(frame.Vertices!));
    }

    [Fact]
    public void Prepare_EmptyAndRestoredSlotDoesNotReuseStaleContent()
    {
        using SceneFixture fixture = new();
        RenderItem item = fixture.CreateItem();
        fixture.Scene.Prepare(0, [item], true);
        item.Scale = Vector3.Zero;
        fixture.Scene.Prepare(0, [item], true);
        Assert.Equal(0u, fixture.Scene.GetFrame(0).InstanceCount);
        item.Scale = Vector3.One;
        fixture.Scene.Prepare(0, [item], true);
        Assert.Equal(1u, fixture.Scene.GetFrame(0).InstanceCount);
        Assert.Equal(item.ModelMatrix, fixture.Surface(0).Model);
        int uploads = fixture.Device.Uploads.Count;

        fixture.Scene.Prepare(0, [item], true);

        Assert.Equal(uploads, fixture.Device.Uploads.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildTopLevel_PreservesBuildReadinessAndProvidesWriteVisibility(bool mapped)
    {
        using SceneFixture fixture = new(mapped);
        RenderItem item = fixture.CreateItem();
        fixture.Scene.Prepare(0, [item], true);
        RayTracingScene.FrameResources frame = fixture.Scene.GetFrame(0);
        Assert.False(frame.TopLevel!.IsBuilt);
        using RecordingCommandBuffer command = new();
        fixture.Scene.BuildTopLevel(command, 0);
        Assert.False(frame.TopLevel.IsBuilt);
        fixture.Device.Submit(command);
        Assert.True(frame.TopLevel.IsBuilt);
        Assert.Equal(1, fixture.Scene.TopLevelBuildCount);
        if (mapped)
        {
            Assert.Contains(command.Barriers, barrier => barrier.SourceStages == PipelineStages.Host
                && barrier.SourceAccess == ResourceAccess.HostWrite
                && barrier.DestinationStages == PipelineStages.AccelerationStructureBuild);
            Assert.Contains(command.Barriers, barrier => barrier.SourceAccess.HasFlag(ResourceAccess.HostWrite)
                && barrier.DestinationStages == PipelineStages.RayTracingShader);
            Assert.Equal(2, fixture.Device.MapCalls);
            Assert.Equal(2, fixture.Device.UnmapCalls);
        }
        else
        {
            Assert.DoesNotContain(command.Barriers, barrier => barrier.SourceAccess.HasFlag(ResourceAccess.HostWrite));
            Assert.Contains(command.Barriers, barrier => barrier.SourceAccess == ResourceAccess.TransferWrite
                && barrier.DestinationStages == PipelineStages.RayTracingShader);
        }
        IAccelerationStructure topLevel = frame.TopLevel;
        int uploads = fixture.Device.Uploads.Count;
        fixture.Scene.Prepare(0, [item], true);
        using RecordingCommandBuffer update = new();
        fixture.Scene.BuildTopLevel(update, 0);
        Assert.Same(topLevel, frame.TopLevel);
        Assert.Equal(uploads, fixture.Device.Uploads.Count);
        Assert.Equal(1, fixture.Scene.TopLevelUpdateCount);
        Assert.True(Assert.Single(update.Builds).Update);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Prepare_InstanceGrowthInitializesNewDynamicBuffersWithoutGeometryRepack(bool mapped)
    {
        using SceneFixture fixture = new(mapped);
        RenderItem first = fixture.CreateItem();
        fixture.Scene.Prepare(0, [first], true);
        RayTracingScene.FrameResources frame = fixture.Scene.GetFrame(0);
        IBuffer instances = frame.Instances!;
        IBuffer surfaces = frame.Surfaces!;
        RenderItem second = new() { Mesh = first.Mesh, Material = first.Material, Position = Vector3.One };

        fixture.Scene.Prepare(0, [first, second], true);

        Assert.NotSame(instances, frame.Instances);
        Assert.NotSame(surfaces, frame.Surfaces);
        Assert.Equal(2u, frame.InstanceCount);
        Assert.Equal(1, fixture.Device.Writes(frame.Instances!));
        Assert.Equal(1, fixture.Device.Writes(frame.Surfaces!));
        Assert.Equal(1, fixture.Device.Writes(frame.Vertices!));
        Assert.Equal(1, fixture.Device.Writes(frame.Indices!));
        Assert.Equal(second.ModelMatrix, fixture.Surface(0, 1).Model);
        int uploads = fixture.Device.Uploads.Count;
        int maps = fixture.Device.MapCalls;
        fixture.Scene.Prepare(0, [first, second], true);
        Assert.Equal(uploads, fixture.Device.Uploads.Count);
        Assert.Equal(maps, fixture.Device.MapCalls);
    }

    [Fact]
    public void Prepare_MaskChangeWritesOnlyInstances()
    {
        using SceneFixture fixture = new();
        RenderItem item = fixture.CreateItem();
        fixture.Scene.Prepare(0, [item], true);
        RayTracingScene.FrameResources frame = fixture.Scene.GetFrame(0);
        int uploads = fixture.Device.Uploads.Count;
        item.RenderingMode = MeshRenderingMode.RenderOnly;

        fixture.Scene.Prepare(0, [item], true);

        Assert.Same(frame.Instances, Assert.Single(fixture.Device.Uploads.Skip(uploads)).Buffer);
        RayTracingInstance instance = MemoryMarshal.Read<RayTracingInstance>(fixture.Device.Data(frame.Instances!));
        Assert.Equal(item.RenderingMode.RayTracingMask(), (byte)(instance.CustomIndexAndMask >> 24));
    }

    [Fact]
    public void SurfaceData_PreservesPaddedLayout()
    {
        Assert.Equal(256, Marshal.SizeOf<RayTracingSurfaceData>());
        Assert.Equal(144, Marshal.OffsetOf<RayTracingSurfaceData>(nameof(RayTracingSurfaceData.Material)).ToInt32());
        Assert.Equal(248, Marshal.OffsetOf<RayTracingSurfaceData>(nameof(RayTracingSurfaceData.Padding3)).ToInt32());
    }

    [Fact]
    public void Prepare_SlotLayoutRefreshesEvenWhenGlobalGeometryRevisionIsUnchanged()
    {
        using SceneFixture fixture = new();
        RenderItem first = fixture.CreateItem();
        RenderItem second = fixture.CreateItem(vertexCount: 4);
        fixture.Scene.Prepare(0, [first], true);
        fixture.Scene.Prepare(1, [second], true);
        long revision = fixture.Scene.GeometryRevision;
        int uploads = fixture.Device.Uploads.Count;

        Assert.False(fixture.Scene.Prepare(0, [second], true));

        Assert.Equal(revision, fixture.Scene.GeometryRevision);
        Assert.Equal(3, fixture.Device.Uploads.Count - uploads);
        RayTracingVertexData[] vertices = MemoryMarshal.Cast<byte, RayTracingVertexData>(
            fixture.Device.Data(fixture.Scene.GetFrame(0).Vertices!)).ToArray();
        Assert.Equal(4, vertices.Length);
        Assert.Equal(second.Mesh.RayTracingVertices![3].Position, vertices[3].Position.XYZ);
    }

    private sealed class SceneFixture : IDisposable
    {
        private readonly List<Mesh> _meshes = [];
        public TestDevice Device { get; }
        public MaterialDefaultTextures Defaults { get; }
        public RayTracingScene Scene { get; }

        public SceneFixture(bool mapped = false)
        {
            Device = new TestDevice(mapped);
            Defaults = MaterialDefaultTextures.Create(Device);
            Scene = new RayTracingScene(Device, 2, Defaults, 24);
        }

        public RenderItem CreateItem(int vertexCount = 3)
        {
            Vertex[] vertices = Enumerable.Range(0, vertexCount).Select(index => new Vertex
            {
                Position = new Vector3(index % 2, index / 2, 0f), Normal = new Vector3(0f, 0f, 1f),
                Tangent = new Vector4(1f, 0f, 0f, 1f), UV = new Vector2(index % 2, index / 2),
            }).ToArray();
            Mesh mesh = new(Device, vertices, [0u, 1u, 2u], "CacheTest");
            _meshes.Add(mesh);
            return new RenderItem { Mesh = mesh, Material = new Material() };
        }

        public RayTracingSurfaceData Surface(int slot, int index = 0) =>
            MemoryMarshal.Read<RayTracingSurfaceData>(Device.Data(Scene.GetFrame(slot).Surfaces!).AsSpan(index * Marshal.SizeOf<RayTracingSurfaceData>()));

        public void Dispose()
        {
            Scene.Dispose();
            foreach (Mesh mesh in _meshes)
                mesh.Dispose();
            Defaults.Dispose();
            Device.Dispose();
        }
    }

    private sealed class TestDevice(bool mapped) : IDevice
    {
        private readonly NullDevice _inner = new();
        private readonly List<TestBuffer> _buffers = [];
        private ulong _address = 4096;
        public IReadOnlyList<NullBufferUpload> Uploads => _inner.BufferUploads;
        public int MapCalls => _buffers.Sum(buffer => buffer.MapCalls);
        public int UnmapCalls => _buffers.Sum(buffer => buffer.UnmapCalls);
        public string Name => _inner.Name;
        public FeatureInfo Features { get; } = new()
        {
            DeviceName = "Null RT", VendorName = "Engine", ApiVersion = "Null 1.0",
            DedicatedVideoMemory = 0, SupportsCompute = false, SupportsMeshShaders = false,
            SupportsAccelerationStructures = true, SupportsRayTracing = true,
            RayTracing = new RayTracingLimits { MinScratchAlignment = 256 },
        };
        public IMemoryAllocator MemoryAllocator => _inner.MemoryAllocator;
        public int Writes(IBuffer buffer) => Uploads.Count(upload => ReferenceEquals(upload.Buffer, buffer)) + ((TestBuffer)buffer).UnmapCalls;
        public byte[] Data(IBuffer buffer) => ((TestBuffer)buffer).Mapped != IntPtr.Zero
            ? ((TestBuffer)buffer).Read()
            : Uploads.Last(upload => ReferenceEquals(upload.Buffer, buffer)).Data;
        public IBuffer CreateBuffer(in BufferDescription description)
        {
            TestBuffer buffer = new(description, _address += 4096, mapped);
            _buffers.Add(buffer);
            return buffer;
        }
        public AccelerationStructureBuildSizes GetAccelerationStructureBuildSizes(AccelerationStructureBuildDescription description) => new(256, 256, 256);
        public IAccelerationStructure CreateAccelerationStructure(AccelerationStructureType type, ulong size) => new TestStructure(type, size, _address += 4096);
        public ITexture CreateTexture(in TextureDescription description) => _inner.CreateTexture(description);
        public ITextureView CreateTextureView(ITexture texture, TextureViewType type) => _inner.CreateTextureView(texture, type);
        public ITextureView CreateTextureView(ITexture texture, in TextureViewDescription description) => _inner.CreateTextureView(texture, description);
        public ISampler CreateSampler(in SamplerDescription description) => _inner.CreateSampler(description);
        public IShaderProgram CreateShaderProgram(in ShaderProgramDescription description) => _inner.CreateShaderProgram(description);
        public IGraphicsPipeline CreateGraphicsPipeline(in GraphicsPipelineDescription description) => _inner.CreateGraphicsPipeline(description);
        public IComputePipeline CreateComputePipeline(in ComputePipelineDescription description) => _inner.CreateComputePipeline(description);
        public ICommandBuffer CreateCommandBuffer() => new RecordingCommandBuffer();
        public void UploadBufferData(IBuffer buffer, ReadOnlySpan<byte> data, ulong bufferOffset = 0) => _inner.UploadBufferData(buffer, data, bufferOffset);
        public void UploadTextureData(ITexture texture, ReadOnlySpan<byte> data) => _inner.UploadTextureData(texture, data);
        public void UploadTextureSubresources(ITexture texture, ReadOnlySpan<TextureSubresourceData> subresources) => _inner.UploadTextureSubresources(texture, subresources);
        public void FlushUploads() => _inner.FlushUploads();
        public void Submit(ICommandBuffer commandBuffer)
        {
            foreach (var build in ((RecordingCommandBuffer)commandBuffer).Builds)
                build.Structure.IsBuilt = true;
        }
        public void WaitIdle() => _inner.WaitIdle();
        public void Dispose()
        {
            foreach (TestBuffer buffer in _buffers)
                buffer.Dispose();
            _inner.Dispose();
        }
    }

    private sealed class TestBuffer : IBuffer
    {
        public ulong Size { get; }
        public BufferUsage Usage { get; }
        public MemoryUsage Memory { get; }
        public ulong DeviceAddress { get; }
        public IntPtr Mapped { get; private set; }
        public int MapCalls { get; private set; }
        public int UnmapCalls { get; private set; }
        public TestBuffer(in BufferDescription description, ulong address, bool mapped)
        {
            Size = description.Size;
            Usage = description.Usage;
            Memory = description.Memory;
            DeviceAddress = address;
            if (mapped && Memory == MemoryUsage.CpuToGpu)
                Mapped = Marshal.AllocHGlobal(checked((int)Size));
        }
        public IntPtr Map()
        {
            MapCalls++;
            return Mapped;
        }
        public void Unmap() => UnmapCalls++;
        public byte[] Read()
        {
            byte[] data = new byte[checked((int)Size)];
            Marshal.Copy(Mapped, data, 0, data.Length);
            return data;
        }
        public void Dispose()
        {
            if (Mapped == IntPtr.Zero)
                return;
            Marshal.FreeHGlobal(Mapped);
            Mapped = IntPtr.Zero;
        }
    }

    private sealed class TestStructure(AccelerationStructureType type, ulong size, ulong address) : IAccelerationStructure
    {
        public AccelerationStructureType Type => type;
        public ulong Size => size;
        public ulong DeviceAddress => address;
        public bool IsBuilt { get; set; }
        public void Dispose() { }
    }

    private sealed class RecordingCommandBuffer : ICommandBuffer
    {
        private readonly NullCommandBuffer _inner = new();
        public List<(TestStructure Structure, bool Update)> Builds { get; } = [];
        public List<(PipelineStages SourceStages, ResourceAccess SourceAccess, PipelineStages DestinationStages, ResourceAccess DestinationAccess)> Barriers { get; } = [];
        public void BuildAccelerationStructure(IAccelerationStructure destination, AccelerationStructureBuildDescription description, IBuffer scratch, ulong scratchOffset = 0, bool update = false) => Builds.Add(((TestStructure)destination, update));
        public void MemoryBarrier(PipelineStages sourceStages, ResourceAccess sourceAccess, PipelineStages destinationStages, ResourceAccess destinationAccess) => Barriers.Add((sourceStages, sourceAccess, destinationStages, destinationAccess));
        public void Begin() => _inner.Begin();
        public void End() => _inner.End();
        public void BeginRendering(in RenderPassInfo info) => _inner.BeginRendering(info);
        public void EndRendering() => _inner.EndRendering();
        public void BeginTimestamp(string name) => _inner.BeginTimestamp(name);
        public void EndTimestamp() => _inner.EndTimestamp();
        public void SetViewport(in Viewport viewport) => _inner.SetViewport(viewport);
        public void SetScissor(in Rect scissor) => _inner.SetScissor(scissor);
        public void BindGraphicsPipeline(IGraphicsPipeline pipeline) => _inner.BindGraphicsPipeline(pipeline);
        public void BindComputePipeline(IComputePipeline pipeline) => _inner.BindComputePipeline(pipeline);
        public void BindDescriptorSet(IDescriptorSet set, uint setIndex, ReadOnlySpan<uint> dynamicOffsets = default) => _inner.BindDescriptorSet(set, setIndex, dynamicOffsets);
        public void BindVertexBuffer(uint slot, IBuffer buffer, ulong offset = 0) => _inner.BindVertexBuffer(slot, buffer, offset);
        public void BindIndexBuffer(IBuffer buffer, IndexType indexType) => _inner.BindIndexBuffer(buffer, indexType);
        public void PushConstants(ReadOnlySpan<byte> data, ShaderStages stages, uint offset = 0) => _inner.PushConstants(data, stages, offset);
        public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0) => _inner.Draw(vertexCount, instanceCount, firstVertex, firstInstance);
        public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int vertexOffset = 0, uint firstInstance = 0) => _inner.DrawIndexed(indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
        public void Dispatch(uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1) => _inner.Dispatch(groupCountX, groupCountY, groupCountZ);
        public void CopyBufferToBuffer(IBuffer source, IBuffer destination, ulong size, ulong sourceOffset = 0, ulong destinationOffset = 0) => _inner.CopyBufferToBuffer(source, destination, size, sourceOffset, destinationOffset);
        public void CopyBufferToImage(IBuffer source, ITexture destination, ulong sourceOffset = 0) => _inner.CopyBufferToImage(source, destination, sourceOffset);
        public void ImageBarrier(ITexture texture, ImageLayout oldLayout, ImageLayout newLayout) => _inner.ImageBarrier(texture, oldLayout, newLayout);
        public void ImageBarrier(ITexture texture, ImageLayout oldLayout, ImageLayout newLayout, PipelineStages sourceStages, ResourceAccess sourceAccess, PipelineStages destinationStages, ResourceAccess destinationAccess, TextureSubresourceRange subresources = default) => _inner.ImageBarrier(texture, oldLayout, newLayout, sourceStages, sourceAccess, destinationStages, destinationAccess, subresources);
        public void CopyImage(ITexture source, ImageLayout sourceLayout, ITexture destination, ImageLayout destinationLayout) => _inner.CopyImage(source, sourceLayout, destination, destinationLayout);
        public void BufferBarrier(IBuffer buffer, ulong size, ulong offset = 0) => _inner.BufferBarrier(buffer, size, offset);
        public void BufferBarrier(IBuffer buffer, ulong size, PipelineStages sourceStages, ResourceAccess sourceAccess, PipelineStages destinationStages, ResourceAccess destinationAccess, ulong offset = 0) => _inner.BufferBarrier(buffer, size, sourceStages, sourceAccess, destinationStages, destinationAccess, offset);
        public void Dispose() => _inner.Dispose();
    }
}
