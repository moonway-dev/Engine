using System.Runtime.InteropServices;
using Engine.Mathematics;
using Engine.RHI;

namespace Engine.Rendering;

[StructLayout(LayoutKind.Sequential)]
public struct RayTracingVertexData
{
    public Vector4 Position;
    public Vector4 Normal;
    public Vector4 Tangent;
    public Vector4 Uv;
}

[StructLayout(LayoutKind.Sequential)]
public struct RayTracingSurfaceData
{
    public Matrix4x4 Model;
    public Matrix4x4 InverseModel;
    public uint VertexOffset;
    public uint IndexOffset;
    public uint TextureOffset;
    public uint Padding;
    public MaterialData Material;
    public Vector2 Padding1;
    public Vector2 Padding2;
    public Vector2 Padding3;
}

public readonly record struct RayTracingDrawItem(RenderItem Item, Mesh Mesh);

public sealed class RayTracingScene : IDisposable
{
    internal sealed class Geometry : IDisposable
    {
        public required Mesh Mesh;
        public required IDisposable Lease;
        public required IAccelerationStructure Structure;
        public int References;

        public void Dispose()
        {
            Structure.Dispose();
            Lease.Dispose();
        }
    }

    public sealed class FrameResources : IDisposable
    {
        private readonly List<Geometry> _geometries = [];
        public IAccelerationStructure? TopLevel { get; internal set; }
        public IBuffer? Instances { get; internal set; }
        public IBuffer? Scratch { get; internal set; }
        public IBuffer? Vertices { get; internal set; }
        public IBuffer? Indices { get; internal set; }
        public IBuffer? Surfaces { get; internal set; }
        public AccelerationStructureBuildDescription? BuildDescription { get; internal set; }
        public ulong ScratchOffset { get; internal set; }
        public uint InstanceCount { get; internal set; }
        public IReadOnlyList<ITexture> Textures { get; internal set; } = [];
        internal bool CanUpdate;
        internal Mesh[] MeshOrder = [];
        internal Mesh[] ShadingMeshOrder = [];
        internal Dictionary<Mesh, (uint Vertex, uint Index)> ShadingOffsets = [];
        internal RayTracingInstance[] InstanceData = [];
        internal RayTracingSurfaceData[] SurfaceData = [];
        internal bool InstancesHostWritten;
        internal bool SurfacesHostWritten;

        internal void ReleaseGeometry()
        {
            foreach (Geometry geometry in _geometries)
                geometry.References--;
            _geometries.Clear();
        }

        internal void AddGeometry(Geometry geometry)
        {
            geometry.References++;
            _geometries.Add(geometry);
        }

        public void Dispose()
        {
            ReleaseGeometry();
            TopLevel?.Dispose();
            Instances?.Dispose();
            Scratch?.Dispose();
            Vertices?.Dispose();
            Indices?.Dispose();
            Surfaces?.Dispose();
            TopLevel = null;
            Instances = null;
            Scratch = null;
            Vertices = null;
            Indices = null;
            Surfaces = null;
            BuildDescription = null;
            CanUpdate = false;
            MeshOrder = [];
            ShadingMeshOrder = [];
            ShadingOffsets.Clear();
            InstanceData = [];
            SurfaceData = [];
            Textures = [];
            InstancesHostWritten = false;
            SurfacesHostWritten = false;
        }
    }

    private readonly IDevice _device;
    private readonly int _materialTextureCapacity;
    private readonly Dictionary<Mesh, Geometry> _geometry = [];
    private readonly FrameResources[] _frames;
    private readonly MaterialDefaultTextures _defaults;
    private Mesh[] _activeMeshes = [];
    private bool _disposed;

    public long GeometryRevision { get; private set; }
    public int BottomLevelBuildCount { get; private set; }
    public int TopLevelBuildCount { get; private set; }
    public int TopLevelUpdateCount { get; private set; }
    public IReadOnlyList<IAccelerationStructure> BottomLevels => _activeMeshes.Select(mesh => _geometry[mesh].Structure).ToArray();
    public IReadOnlyList<Mesh> GeometryMeshes => _activeMeshes;

    public RayTracingScene(IDevice device, int framesInFlight, MaterialDefaultTextures defaults, int materialTextureCapacity)
    {
        if (!device.Features.SupportsAccelerationStructures || !device.Features.SupportsRayTracing)
            throw new NotSupportedException("Ray tracing scene requires enabled acceleration structures and pipelines.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(framesInFlight);
        if (materialTextureCapacity < RayTracingTextureCapacity.TexturesPerMaterial
            || materialTextureCapacity % RayTracingTextureCapacity.TexturesPerMaterial != 0)
            throw new ArgumentOutOfRangeException(nameof(materialTextureCapacity));
        _device = device;
        _defaults = defaults;
        _materialTextureCapacity = materialTextureCapacity;
        _frames = Enumerable.Range(0, framesInFlight).Select(_ => new FrameResources()).ToArray();
    }

    internal int MaterialTextureCapacity => _materialTextureCapacity;

    public FrameResources GetFrame(int slot) => _frames[slot];

    public bool Prepare(int slot, IReadOnlyList<RenderItem> items, bool prepareMaterials) =>
        Prepare(slot, items.Select(item => new RayTracingDrawItem(item, item.Mesh)).ToArray(), prepareMaterials);

    public bool Prepare(int slot, IReadOnlyList<RayTracingDrawItem> items, bool prepareMaterials)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(_frames.Length);
        if ((uint)slot >= (uint)_frames.Length)
            throw new ArgumentOutOfRangeException(nameof(slot));
        ArgumentNullException.ThrowIfNull(items);
        FrameResources frame = _frames[slot];
        frame.ReleaseGeometry();
        List<RayTracingDrawItem> validDraws = [];
        List<Matrix4x4> models = [];
        foreach (RayTracingDrawItem draw in items)
        {
            if (draw.Item.RenderingMode.RayTracingMask() == 0 || draw.Mesh.IndexCount < 3)
                continue;
            Matrix4x4 model = draw.Item.ModelMatrix;
            if (!IsInvertible(model))
                continue;
            validDraws.Add(draw);
            models.Add(model);
        }
        RayTracingDrawItem[] validItems = validDraws.ToArray();
        Mesh[] meshes = validItems.Select(draw => draw.Mesh).Distinct().ToArray();
        bool changed = !_activeMeshes.SequenceEqual(meshes);
        foreach (Mesh mesh in meshes)
        {
            if (!_geometry.ContainsKey(mesh))
                _geometry.Add(mesh, BuildGeometry(mesh));
        }
        if (changed)
        {
            _activeMeshes = meshes;
            GeometryRevision++;
        }
        foreach (Mesh mesh in meshes)
            frame.AddGeometry(_geometry[mesh]);
        HashSet<Mesh> active = meshes.ToHashSet();
        foreach (Mesh mesh in _geometry.Keys.Where(mesh => !active.Contains(mesh) && _geometry[mesh].References == 0).ToArray())
        {
            _geometry[mesh].Dispose();
            _geometry.Remove(mesh);
        }
        RayTracingInstance[] instances = new RayTracingInstance[validItems.Length];
        for (int index = 0; index < validItems.Length; index++)
        {
            RayTracingDrawItem draw = validItems[index];
            instances[index] = RayTracingInstance.Create(models[index], (uint)index, draw.Item.RenderingMode.RayTracingMask(), 0,
                RayTracingInstanceFlags.TriangleFacingCullDisable, _geometry[draw.Mesh].Structure.DeviceAddress);
        }
        bool sameTopology = frame.MeshOrder.SequenceEqual(validItems.Select(draw => draw.Mesh));
        bool instancesReallocated = EnsureBuffer(frame.Instances, Math.Max(64UL, checked((ulong)instances.Length * 64)),
            BufferUsage.AccelerationStructureBuildInput | BufferUsage.DeviceAddress | BufferUsage.TransferDestination,
            out IBuffer instanceBuffer, MemoryUsage.CpuToGpu);
        frame.Instances = instanceBuffer;
        ReadOnlySpan<byte> instanceBytes = MemoryMarshal.AsBytes(instances.AsSpan());
        if (instancesReallocated || !instanceBytes.SequenceEqual(MemoryMarshal.AsBytes(frame.InstanceData.AsSpan())))
        {
            frame.InstancesHostWritten = WriteDynamic(instanceBuffer, instanceBytes);
            frame.InstanceData = instances;
        }
        AccelerationStructureBuildDescription description = new()
        {
            Type = AccelerationStructureType.TopLevel,
            Flags = AccelerationStructureBuildFlags.PreferFastTrace | AccelerationStructureBuildFlags.AllowUpdate,
            Geometries = [new InstanceGeometry { Buffer = instanceBuffer, Count = (uint)instances.Length, BottomLevels = BottomLevels }],
        };
        AccelerationStructureBuildSizes sizes = _device.GetAccelerationStructureBuildSizes(description);
        bool sameInstanceCount = frame.BuildDescription is not null
            && frame.BuildDescription.Geometries.Count == 1
            && frame.BuildDescription.Geometries[0] is InstanceGeometry previousInstances
            && description.Geometries[0] is InstanceGeometry newInstances
            && previousInstances.Count == newInstances.Count;
        bool canUpdate = sameInstanceCount && !instancesReallocated && sameTopology
            && frame.TopLevel is not null && frame.TopLevel.IsBuilt;
        bool structureReallocated = frame.TopLevel is null || frame.TopLevel.Size < sizes.StorageSize
            || (frame.TopLevel.IsBuilt && !canUpdate);
        if (structureReallocated)
        {
            frame.TopLevel?.Dispose();
            frame.TopLevel = _device.CreateAccelerationStructure(AccelerationStructureType.TopLevel, sizes.StorageSize);
        }
        ulong scratchSize = Math.Max(sizes.BuildScratchSize, sizes.UpdateScratchSize);
        EnsureBuffer(frame.Scratch, Math.Max(1UL, scratchSize) + _device.Features.RayTracing.MinScratchAlignment,
            BufferUsage.Storage | BufferUsage.DeviceAddress, out IBuffer scratch);
        frame.Scratch = scratch;
        frame.ScratchOffset = ShaderBindingTable.AlignUp(scratch.DeviceAddress, _device.Features.RayTracing.MinScratchAlignment) - scratch.DeviceAddress;
        frame.CanUpdate = !structureReallocated && frame.TopLevel!.IsBuilt;
        frame.MeshOrder = validItems.Select(draw => draw.Mesh).ToArray();
        frame.InstanceCount = (uint)instances.Length;
        frame.BuildDescription = description;
        if (prepareMaterials)
            PrepareShading(frame, validItems, meshes, CollectionsMarshal.AsSpan(models));
        return changed;
    }

    public void BuildTopLevel(ICommandBuffer commandBuffer, int slot)
    {
        FrameResources frame = _frames[slot];
        if (frame.InstancesHostWritten)
            commandBuffer.MemoryBarrier(PipelineStages.Host, ResourceAccess.HostWrite,
                PipelineStages.AccelerationStructureBuild, ResourceAccess.AccelerationStructureRead);
        if (frame.Vertices is not null && frame.Indices is not null && frame.Surfaces is not null)
            commandBuffer.MemoryBarrier(PipelineStages.Transfer | (frame.SurfacesHostWritten ? PipelineStages.Host : PipelineStages.None),
                ResourceAccess.TransferWrite | (frame.SurfacesHostWritten ? ResourceAccess.HostWrite : ResourceAccess.None),
                PipelineStages.RayTracingShader, ResourceAccess.ShaderRead);
        commandBuffer.BuildAccelerationStructure(frame.TopLevel!, frame.BuildDescription!, frame.Scratch!, frame.ScratchOffset, frame.CanUpdate);
        if (frame.CanUpdate)
            TopLevelUpdateCount++;
        else
            TopLevelBuildCount++;
    }

    private Geometry BuildGeometry(Mesh mesh)
    {
        IDisposable lease = mesh.RetainForRayTracing();
        IAccelerationStructure? structure = null;
        IBuffer? scratch = null;
        try
        {
            AccelerationStructureBuildDescription description = new()
            {
                Type = AccelerationStructureType.BottomLevel,
                Geometries = [new TriangleGeometry
                {
                    VertexBuffer = mesh.VertexBuffer,
                    VertexCount = (uint)mesh.VertexCount,
                    VertexStride = Vertex.Layout.Stride,
                    IndexBuffer = mesh.IndexBuffer,
                    IndexCount = mesh.IndexCount,
                }],
            };
            AccelerationStructureBuildSizes sizes = _device.GetAccelerationStructureBuildSizes(description);
            structure = _device.CreateAccelerationStructure(AccelerationStructureType.BottomLevel, sizes.StorageSize);
            scratch = _device.CreateBuffer(new BufferDescription
            {
                Size = Math.Max(1UL, sizes.BuildScratchSize) + _device.Features.RayTracing.MinScratchAlignment,
                Usage = BufferUsage.Storage | BufferUsage.DeviceAddress,
                Memory = MemoryUsage.GpuOnly,
            });
            ulong scratchOffset = ShaderBindingTable.AlignUp(scratch.DeviceAddress, _device.Features.RayTracing.MinScratchAlignment) - scratch.DeviceAddress;
            _device.FlushUploads();
            using ICommandBuffer commandBuffer = _device.CreateCommandBuffer();
            commandBuffer.Begin();
            commandBuffer.MemoryBarrier(PipelineStages.Transfer, ResourceAccess.TransferWrite,
                PipelineStages.AccelerationStructureBuild, ResourceAccess.AccelerationStructureRead);
            commandBuffer.BuildAccelerationStructure(structure, description, scratch, scratchOffset);
            commandBuffer.MemoryBarrier(PipelineStages.AccelerationStructureBuild, ResourceAccess.AccelerationStructureWrite,
                PipelineStages.AccelerationStructureBuild | PipelineStages.RayTracingShader, ResourceAccess.AccelerationStructureRead);
            commandBuffer.End();
            _device.Submit(commandBuffer);
            BottomLevelBuildCount++;
            return new Geometry { Mesh = mesh, Lease = lease, Structure = structure };
        }
        catch
        {
            structure?.Dispose();
            lease.Dispose();
            throw;
        }
        finally
        {
            scratch?.Dispose();
        }
    }

    private void PrepareShading(FrameResources frame, RayTracingDrawItem[] items, Mesh[] meshes, ReadOnlySpan<Matrix4x4> models)
    {
        if (frame.Vertices is null || frame.Indices is null || !frame.ShadingMeshOrder.SequenceEqual(meshes))
            PrepareShadingGeometry(frame, meshes);
        List<ITexture> textures = [];
        RayTracingMaterialTexturePool texturePool = new(_materialTextureCapacity);
        RayTracingSurfaceData[] surfaces = new RayTracingSurfaceData[items.Length];
        for (int index = 0; index < items.Length; index++)
        {
            RayTracingDrawItem draw = items[index];
            RenderItem item = draw.Item;
            (uint textureOffset, bool allocateTextures) = texturePool.Assign(item.Material);
            if (allocateTextures)
            {
                MaterialTextureSet materialTextures = item.Material.ResolveTextures(_defaults);
                textures.Add(materialTextures.Albedo);
                textures.Add(materialTextures.Metallic);
                textures.Add(materialTextures.Roughness);
                textures.Add(materialTextures.Normal);
                textures.Add(materialTextures.AmbientOcclusion);
                textures.Add(materialTextures.Emission!);
            }
            (uint vertexOffset, uint indexOffset) = frame.ShadingOffsets[draw.Mesh];
            Matrix4x4 model = models[index];
            surfaces[index] = new RayTracingSurfaceData
            {
                Model = model, InverseModel = model.Invert(), VertexOffset = vertexOffset,
                IndexOffset = indexOffset, TextureOffset = textureOffset, Material = item.Material.ToData(),
            };
        }
        while (textures.Count < texturePool.DefaultTextureOffset)
            textures.Add(_defaults.WhiteLinear);
        MaterialTextureSet defaults = _defaults.TextureSet;
        textures.Add(defaults.Albedo);
        textures.Add(defaults.Metallic);
        textures.Add(defaults.Roughness);
        textures.Add(defaults.Normal);
        textures.Add(defaults.AmbientOcclusion);
        textures.Add(defaults.Emission!);
        if (frame.Textures.Count != textures.Count || frame.Textures.Where((texture, index) => !ReferenceEquals(texture, textures[index])).Any())
            frame.Textures = textures.ToArray();
        ReadOnlySpan<byte> surfaceBytes = MemoryMarshal.AsBytes(surfaces.AsSpan());
        bool surfacesReallocated = EnsureBuffer(frame.Surfaces, Math.Max(16UL, (ulong)surfaceBytes.Length),
            BufferUsage.Storage | BufferUsage.TransferDestination, out IBuffer surfaceBuffer, MemoryUsage.CpuToGpu);
        frame.Surfaces = surfaceBuffer;
        if (surfacesReallocated || !surfaceBytes.SequenceEqual(MemoryMarshal.AsBytes(frame.SurfaceData.AsSpan())))
        {
            frame.SurfacesHostWritten = WriteDynamic(surfaceBuffer, surfaceBytes);
            frame.SurfaceData = surfaces;
        }
    }

    private void PrepareShadingGeometry(FrameResources frame, Mesh[] meshes)
    {
        List<RayTracingVertexData> vertices = [];
        List<uint> indices = [];
        Dictionary<Mesh, (uint Vertex, uint Index)> offsets = [];
        foreach (Mesh mesh in meshes)
        {
            Vertex[] sourceVertices = mesh.RayTracingVertices ?? throw new InvalidOperationException("RT geometry source was not retained.");
            uint[] sourceIndices = mesh.RayTracingIndices ?? throw new InvalidOperationException("RT indices were not retained.");
            offsets[mesh] = ((uint)vertices.Count, (uint)indices.Count);
            foreach (Vertex vertex in sourceVertices)
                vertices.Add(new RayTracingVertexData
                {
                    Position = new Vector4(vertex.Position, 1f), Normal = new Vector4(vertex.Normal, 0f),
                    Tangent = vertex.Tangent, Uv = new Vector4(vertex.UV.X, vertex.UV.Y, 0f, 0f),
                });
            indices.AddRange(sourceIndices);
        }
        frame.Vertices = UploadStorage(frame.Vertices, CollectionsMarshal.AsSpan(vertices));
        frame.Indices = UploadStorage(frame.Indices, CollectionsMarshal.AsSpan(indices));
        frame.ShadingOffsets = offsets;
        frame.ShadingMeshOrder = meshes;
    }

    private unsafe bool WriteDynamic(IBuffer buffer, ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
            return false;
        IntPtr mapped = buffer.Map();
        if (mapped == IntPtr.Zero)
        {
            _device.UploadBufferData(buffer, bytes);
            return false;
        }
        try
        {
            bytes.CopyTo(new Span<byte>((void*)mapped, bytes.Length));
        }
        finally
        {
            buffer.Unmap();
        }
        return true;
    }

    private IBuffer UploadStorage<T>(IBuffer? previous, Span<T> values) where T : unmanaged
    {
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(values);
        EnsureBuffer(previous, Math.Max(16UL, (ulong)bytes.Length), BufferUsage.Storage | BufferUsage.TransferDestination, out IBuffer buffer);
        if (!bytes.IsEmpty)
            _device.UploadBufferData(buffer, bytes);
        return buffer;
    }

    private bool EnsureBuffer(IBuffer? previous, ulong minimumSize, BufferUsage usage, out IBuffer buffer, MemoryUsage memory = MemoryUsage.GpuOnly)
    {
        if (previous is not null && previous.Size >= minimumSize && previous.Usage == usage && previous.Memory == memory)
        {
            buffer = previous;
            return false;
        }
        previous?.Dispose();
        buffer = _device.CreateBuffer(new BufferDescription { Size = minimumSize, Usage = usage, Memory = memory });
        return true;
    }

    private static bool IsInvertible(Matrix4x4 model)
    {
        float determinant = model.M11 * (model.M22 * model.M33 - model.M23 * model.M32)
            - model.M12 * (model.M21 * model.M33 - model.M23 * model.M31)
            + model.M13 * (model.M21 * model.M32 - model.M22 * model.M31);
        return float.IsFinite(determinant) && MathF.Abs(determinant) >= 1e-12f;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (FrameResources frame in _frames)
            frame.Dispose();
        foreach (Geometry geometry in _geometry.Values)
            geometry.Dispose();
        _geometry.Clear();
        _activeMeshes = [];
    }
}
