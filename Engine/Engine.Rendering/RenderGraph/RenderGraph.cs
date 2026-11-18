using Engine.Core;
using Engine.Mathematics;
using Engine.RHI;

namespace Engine.Rendering;

public sealed class RenderGraph : IDisposable
{
    private readonly IDevice _device;
    private readonly ILogger _log;
    private readonly List<RenderGraphPass> _passes = [];
    private readonly List<RenderGraphResource> _resources = [];
    private readonly List<RenderGraphMemoryResource> _memoryResources = [];
    private readonly List<RenderGraphBufferResource> _buffers = [];
    private readonly List<RenderGraphAccelerationStructureResource> _accelerationStructures = [];
    private readonly HashSet<IBuffer> _ownedBuffers = [];
    private readonly Dictionary<TextureDescriptionKey, List<PooledTexture>> _texturePool = [];
    private readonly Dictionary<ITexture, RenderGraphResourceState> _executionTextureStates = [];
    private RenderGraphCompilation? _compilation;
    private bool _disposed;
    private int _nextPhysicalAllocationId;

    public RenderGraph(IDevice device, ILogger log)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public IReadOnlyList<RenderGraphPass> Passes => _passes;
    public IReadOnlyList<RenderGraphResource> Resources => _resources;
    public IReadOnlyList<RenderGraphMemoryResource> MemoryResources => _memoryResources;
    public IReadOnlyList<RenderGraphBufferResource> Buffers => _buffers;
    public IReadOnlyList<RenderGraphAccelerationStructureResource> AccelerationStructures => _accelerationStructures;
    public RenderGraphCompilation? Compilation => _compilation;
    public bool IsCompiled => _compilation is not null;

    public RenderGraphResource DeclareTransientTexture(string name, TextureFormat format, int width, int height) =>
        DeclareTransientTexture(name, new TextureDescription
        {
            Extent = new Size2D(width, height),
            Format = format,
        });

    public RenderGraphResource DeclareTransientTexture(string name, in TextureDescription description)
    {
        ThrowIfDisposed();
        ValidateTextureDescription(description, nameof(description));
        RenderGraphResource resource = new(
            this,
            _resources.Count,
            ValidateName(name, nameof(name)),
            NormalizeDescription(description),
            description.Format.IsDepth(),
            true,
            RenderGraphResourceState.Undefined,
            null);
        _resources.Add(resource);
        InvalidateCompilation();
        return resource;
    }

    public RenderGraphResource CreateTransientTexture(string name, in TextureDescription description) =>
        DeclareTransientTexture(name, description);

    public RenderGraphResource ImportTexture(string name, ITexture texture) =>
        ImportTexture(name, texture, ImageLayout.Undefined, finalLayout: null);

    public RenderGraphResource ImportTexture(
        string name,
        ITexture texture,
        ImageLayout initialLayout,
        ImageLayout? finalLayout = null) =>
        ImportTexture(
            name,
            texture,
            RenderGraphResourceState.FromLayout(initialLayout),
            finalLayout is ImageLayout layout ? RenderGraphResourceState.FromLayout(layout) : null);

    public RenderGraphResource ImportTexture(
        string name,
        ITexture texture,
        in RenderGraphResourceState initialState,
        RenderGraphResourceState? finalState = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(texture);
        TextureDescription description = NormalizeDescription(new TextureDescription
        {
            Extent = new Size2D(checked((int)texture.Width), checked((int)texture.Height)),
            Format = texture.Format,
            MipLevels = texture.MipLevels,
            ArrayLayers = texture.ArrayLayers,
            Dimension = texture.Dimension,
        });
        RenderGraphResource resource = new(
            this,
            _resources.Count,
            ValidateName(name, nameof(name)),
            description,
            texture.Format.IsDepth(),
            false,
            initialState,
            finalState)
        {
            Texture = texture,
            CurrentState = initialState,
            PhysicalDescription = description,
        };
        _resources.Add(resource);
        InvalidateCompilation();
        return resource;
    }

    public void RebindImportedTexture(RenderGraphResource resource, ITexture texture) =>
        RebindImportedTexture(resource, texture, initialState: null, finalState: null, updateFinalState: false);

    public void RebindImportedTexture(
        RenderGraphResource resource,
        ITexture texture,
        ImageLayout initialLayout,
        ImageLayout? finalLayout = null) =>
        RebindImportedTexture(
            resource,
            texture,
            RenderGraphResourceState.FromLayout(initialLayout),
            finalLayout is ImageLayout layout ? RenderGraphResourceState.FromLayout(layout) : null,
            updateFinalState: true);

    public void RebindImportedTexture(
        RenderGraphResource resource,
        ITexture texture,
        in RenderGraphResourceState initialState,
        RenderGraphResourceState? finalState = null) =>
        RebindImportedTexture(resource, texture, initialState, finalState, updateFinalState: true);

    public void SetImportedTexture(RenderGraphResource resource, ITexture texture) => RebindImportedTexture(resource, texture);

    public void SetImportedTexture(
        RenderGraphResource resource,
        ITexture texture,
        ImageLayout initialLayout,
        ImageLayout? finalLayout = null) =>
        RebindImportedTexture(resource, texture, initialLayout, finalLayout);

    public void SetImportedStates(
        RenderGraphResource resource,
        in RenderGraphResourceState initialState,
        RenderGraphResourceState? finalState = null)
    {
        ThrowIfDisposed();
        ValidateResource(resource);
        if (resource.Transient)
            throw new InvalidOperationException($"Resource '{resource.Name}' is transient and has no imported state.");
        resource.InitialState = initialState;
        resource.FinalState = finalState;
        resource.CurrentState = initialState;
        InvalidateCompilation();
    }

    public RenderGraphBufferResource DeclareTransientBuffer(string name, in BufferDescription description)
    {
        ThrowIfDisposed();
        if (description.Size == 0)
            throw new ArgumentOutOfRangeException(nameof(description), "Buffer size must be positive.");
        RenderGraphBufferResource resource = new(
            this,
            _memoryResources.Count,
            ValidateName(name, nameof(name)),
            description,
            true,
            RenderGraphMemoryState.Undefined,
            null);
        _buffers.Add(resource);
        _memoryResources.Add(resource);
        InvalidateCompilation();
        return resource;
    }

    public RenderGraphBufferResource CreateTransientBuffer(string name, in BufferDescription description) =>
        DeclareTransientBuffer(name, description);

    public RenderGraphBufferResource ImportBuffer(string name, IBuffer buffer) =>
        ImportBuffer(name, buffer, RenderGraphMemoryState.Undefined);

    public RenderGraphBufferResource ImportBuffer(
        string name,
        IBuffer buffer,
        in RenderGraphMemoryState initialState,
        RenderGraphMemoryState? finalState = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.Size == 0)
            throw new ArgumentException("Imported buffer size must be positive.", nameof(buffer));
        RenderGraphBufferResource resource = new(
            this,
            _memoryResources.Count,
            ValidateName(name, nameof(name)),
            new BufferDescription { Size = buffer.Size, Usage = buffer.Usage, Memory = buffer.Memory },
            false,
            initialState,
            finalState)
        {
            Buffer = buffer,
        };
        _buffers.Add(resource);
        _memoryResources.Add(resource);
        InvalidateCompilation();
        return resource;
    }

    public RenderGraphAccelerationStructureResource ImportAccelerationStructure(string name, IAccelerationStructure accelerationStructure) =>
        ImportAccelerationStructure(name, accelerationStructure, RenderGraphMemoryState.Undefined);

    public RenderGraphAccelerationStructureResource ImportAccelerationStructure(
        string name,
        IAccelerationStructure accelerationStructure,
        in RenderGraphMemoryState initialState,
        RenderGraphMemoryState? finalState = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(accelerationStructure);
        RenderGraphAccelerationStructureResource resource = new(
            this,
            _memoryResources.Count,
            ValidateName(name, nameof(name)),
            accelerationStructure,
            initialState,
            finalState);
        _accelerationStructures.Add(resource);
        _memoryResources.Add(resource);
        InvalidateCompilation();
        return resource;
    }

    public void RebindImportedBuffer(RenderGraphBufferResource resource, IBuffer buffer)
    {
        ThrowIfDisposed();
        ValidateResource(resource);
        RebindImportedBuffer(resource, buffer, resource.InitialState, resource.FinalState);
    }

    public void RebindImportedBuffer(
        RenderGraphBufferResource resource,
        IBuffer buffer,
        in RenderGraphMemoryState initialState,
        RenderGraphMemoryState? finalState = null)
    {
        ThrowIfDisposed();
        ValidateResource(resource);
        ArgumentNullException.ThrowIfNull(buffer);
        if (resource.Transient)
            throw new InvalidOperationException($"Resource '{resource.Name}' is transient and cannot be rebound as an imported buffer.");
        if (buffer.Size != resource.Description.Size || buffer.Usage != resource.Description.Usage || buffer.Memory != resource.Description.Memory)
            throw new ArgumentException($"Buffer rebound to '{resource.Name}' must exactly match its size, usage, and memory.", nameof(buffer));
        resource.Buffer = buffer;
        UpdateImportedStates(resource, initialState, finalState);
    }

    public void RefreshImportedBufferShape(
        RenderGraphBufferResource resource,
        IBuffer buffer,
        in RenderGraphMemoryState initialState,
        RenderGraphMemoryState? finalState = null)
    {
        ThrowIfDisposed();
        ValidateResource(resource);
        ArgumentNullException.ThrowIfNull(buffer);
        if (resource.Transient)
            throw new InvalidOperationException($"Resource '{resource.Name}' is transient and has no imported shape to refresh.");
        if (buffer.Size == 0)
            throw new ArgumentException("Imported buffer size must be positive.", nameof(buffer));

        BufferDescription description = new() { Size = buffer.Size, Usage = buffer.Usage, Memory = buffer.Memory };
        bool shapeChanged = resource.Description != description;
        resource.Buffer = buffer;
        resource.Description = description;
        resource.CompiledDescription = description;
        resource.PhysicalDescription = description;
        UpdateImportedStates(resource, initialState, finalState);
        if (shapeChanged)
            InvalidateCompilation();
    }

    public void RebindImportedAccelerationStructure(RenderGraphAccelerationStructureResource resource, IAccelerationStructure accelerationStructure)
    {
        ThrowIfDisposed();
        ValidateResource(resource);
        RebindImportedAccelerationStructure(resource, accelerationStructure, resource.InitialState, resource.FinalState);
    }

    public void RebindImportedAccelerationStructure(
        RenderGraphAccelerationStructureResource resource,
        IAccelerationStructure accelerationStructure,
        in RenderGraphMemoryState initialState,
        RenderGraphMemoryState? finalState = null)
    {
        ThrowIfDisposed();
        ValidateResource(resource);
        ArgumentNullException.ThrowIfNull(accelerationStructure);
        if (accelerationStructure.Type != resource.Type || accelerationStructure.Size != resource.Size)
            throw new ArgumentException($"Acceleration structure rebound to '{resource.Name}' must exactly match its type and size.", nameof(accelerationStructure));
        resource.AccelerationStructure = accelerationStructure;
        UpdateImportedStates(resource, initialState, finalState);
    }

    public void RefreshImportedAccelerationStructureShape(
        RenderGraphAccelerationStructureResource resource,
        IAccelerationStructure accelerationStructure,
        in RenderGraphMemoryState initialState,
        RenderGraphMemoryState? finalState = null)
    {
        ThrowIfDisposed();
        ValidateResource(resource);
        ArgumentNullException.ThrowIfNull(accelerationStructure);
        bool shapeChanged = accelerationStructure.Type != resource.Type || accelerationStructure.Size != resource.Size;
        resource.AccelerationStructure = accelerationStructure;
        resource.Type = accelerationStructure.Type;
        resource.Size = accelerationStructure.Size;
        if (shapeChanged)
        {
            resource.InitialState = initialState;
            resource.FinalState = finalState;
            resource.CurrentState = initialState;
            InvalidateCompilation();
        }
        else
            UpdateImportedStates(resource, initialState, finalState);
    }

    public void SetImportedStates(
        RenderGraphMemoryResource resource,
        in RenderGraphMemoryState initialState,
        RenderGraphMemoryState? finalState = null)
    {
        ThrowIfDisposed();
        ValidateResource(resource);
        if (resource.Transient)
            throw new InvalidOperationException($"Resource '{resource.Name}' is transient and has no imported state.");
        UpdateImportedStates(resource, initialState, finalState);
    }

    private void UpdateImportedStates(
        RenderGraphMemoryResource resource,
        in RenderGraphMemoryState initialState,
        RenderGraphMemoryState? finalState)
    {
        bool changed = resource.InitialState != initialState || resource.FinalState != finalState;
        resource.InitialState = initialState;
        resource.FinalState = finalState;
        resource.CurrentState = initialState;
        if (changed)
            InvalidateCompilation();
    }

    public void ClearPasses()
    {
        ThrowIfDisposed();
        _passes.Clear();
        InvalidateCompilation();
    }

    public RenderGraphPass AddGraphicsPass(string name, Action<ICommandBuffer, RenderGraph> execute) =>
        AddPass(name, RenderPassKind.Graphics, execute);

    public RenderGraphPass AddGraphicsPassBefore(RenderGraphPass before, string name, Action<ICommandBuffer, RenderGraph> execute) =>
        AddPassBefore(before, name, RenderPassKind.Graphics, execute);

    public RenderGraphPass AddComputePass(string name, Action<ICommandBuffer, RenderGraph> execute) =>
        AddPass(name, RenderPassKind.Compute, execute);

    public RenderGraphPass AddComputePassBefore(RenderGraphPass before, string name, Action<ICommandBuffer, RenderGraph> execute) =>
        AddPassBefore(before, name, RenderPassKind.Compute, execute);

    public RenderGraphPass AddTransferPass(string name, Action<ICommandBuffer, RenderGraph> execute) =>
        AddPass(name, RenderPassKind.Transfer, execute);

    public RenderGraphPass AddAccelerationStructureBuildPass(string name, Action<ICommandBuffer, RenderGraph> execute) =>
        AddPass(name, RenderPassKind.AccelerationStructureBuild, execute);

    public RenderGraphPass AddAccelerationStructureBuildPassBefore(
        RenderGraphPass before,
        string name,
        Action<ICommandBuffer, RenderGraph> execute) =>
        AddPassBefore(before, name, RenderPassKind.AccelerationStructureBuild, execute);

    public RenderGraphPass AddRayTracingPass(string name, Action<ICommandBuffer, RenderGraph> execute) =>
        AddPass(name, RenderPassKind.RayTracing, execute);

    public RenderGraphPass AddRayTracingPassBefore(
        RenderGraphPass before,
        string name,
        Action<ICommandBuffer, RenderGraph> execute) =>
        AddPassBefore(before, name, RenderPassKind.RayTracing, execute);

    public void ReadsShader(RenderGraphPass pass, RenderGraphBufferResource resource, PipelineStages stages = PipelineStages.None) =>
        AddMemoryAccess(pass, resource, RenderGraphMemoryUsageKind.ShaderRead, stages);

    public void ReadsStorage(RenderGraphPass pass, RenderGraphBufferResource resource, PipelineStages stages = PipelineStages.None) =>
        AddMemoryAccess(pass, resource, RenderGraphMemoryUsageKind.StorageRead, stages);

    public void WritesStorage(RenderGraphPass pass, RenderGraphBufferResource resource, PipelineStages stages = PipelineStages.None) =>
        AddMemoryAccess(pass, resource, RenderGraphMemoryUsageKind.StorageWrite, stages);

    public void ReadsWritesStorage(RenderGraphPass pass, RenderGraphBufferResource resource, PipelineStages stages = PipelineStages.None) =>
        AddMemoryAccess(pass, resource, RenderGraphMemoryUsageKind.StorageReadWrite, stages);

    public void ReadsTransfer(RenderGraphPass pass, RenderGraphBufferResource resource) =>
        AddMemoryAccess(pass, resource, RenderGraphMemoryUsageKind.TransferRead);

    public void WritesTransfer(RenderGraphPass pass, RenderGraphBufferResource resource) =>
        AddMemoryAccess(pass, resource, RenderGraphMemoryUsageKind.TransferWrite);

    public void ReadsAccelerationStructureBuildInput(RenderGraphPass pass, RenderGraphBufferResource resource) =>
        AddMemoryAccess(pass, resource, RenderGraphMemoryUsageKind.AccelerationStructureBuildInput);

    public void UsesAccelerationStructureScratch(RenderGraphPass pass, RenderGraphBufferResource resource) =>
        AddMemoryAccess(pass, resource, RenderGraphMemoryUsageKind.AccelerationStructureScratch);

    public void ReadsShaderBindingTable(RenderGraphPass pass, RenderGraphBufferResource resource) =>
        AddMemoryAccess(pass, resource, RenderGraphMemoryUsageKind.ShaderBindingTable);

    public void ReadsAccelerationStructure(
        RenderGraphPass pass,
        RenderGraphAccelerationStructureResource resource,
        PipelineStages stages = PipelineStages.None) =>
        AddMemoryAccess(pass, resource, RenderGraphMemoryUsageKind.AccelerationStructureRead, stages);

    public void WritesAccelerationStructure(RenderGraphPass pass, RenderGraphAccelerationStructureResource resource) =>
        AddMemoryAccess(pass, resource, RenderGraphMemoryUsageKind.AccelerationStructureWrite);

    public void ReadsWritesAccelerationStructure(RenderGraphPass pass, RenderGraphAccelerationStructureResource resource) =>
        AddMemoryAccess(pass, resource, RenderGraphMemoryUsageKind.AccelerationStructureReadWrite);

    public void DependsOn(RenderGraphPass pass, RenderGraphPass dependency)
    {
        ThrowIfDisposed();
        ValidatePass(pass);
        ValidatePass(dependency);
        if (ReferenceEquals(pass, dependency))
            throw new RenderGraphValidationException($"Pass '{pass.Name}' cannot depend on itself.");
        pass.ExplicitDependencies.Add(dependency);
        InvalidateCompilation();
    }

    public void ReadsShader(RenderGraphPass pass, RenderGraphResource resource) =>
        AddAccess(pass, resource, ResourceUsageKind.ShaderRead);

    public void ReadsShader(
        RenderGraphPass pass,
        RenderGraphResource resource,
        PipelineStages stages) =>
        AddAccess(pass, resource, ResourceUsageKind.ShaderRead, stages);

    public void ReadsSampled(RenderGraphPass pass, RenderGraphResource resource) => ReadsShader(pass, resource);

    public void ReadsSampled(
        RenderGraphPass pass,
        RenderGraphResource resource,
        PipelineStages stages) => ReadsShader(pass, resource, stages);

    public void ReadsStorage(RenderGraphPass pass, RenderGraphResource resource) =>
        AddAccess(pass, resource, ResourceUsageKind.StorageRead);

    public void ReadsStorage(
        RenderGraphPass pass,
        RenderGraphResource resource,
        PipelineStages stages) =>
        AddAccess(pass, resource, ResourceUsageKind.StorageRead, stages);

    public void WritesStorage(RenderGraphPass pass, RenderGraphResource resource) =>
        AddAccess(pass, resource, ResourceUsageKind.StorageWrite);

    public void WritesStorage(
        RenderGraphPass pass,
        RenderGraphResource resource,
        PipelineStages stages) =>
        AddAccess(pass, resource, ResourceUsageKind.StorageWrite, stages);

    public void ReadsWritesStorage(RenderGraphPass pass, RenderGraphResource resource) =>
        AddAccess(pass, resource, ResourceUsageKind.StorageReadWrite);

    public void ReadsWritesStorage(
        RenderGraphPass pass,
        RenderGraphResource resource,
        PipelineStages stages) =>
        AddAccess(pass, resource, ResourceUsageKind.StorageReadWrite, stages);

    public void ReadsTransfer(RenderGraphPass pass, RenderGraphResource resource) =>
        AddAccess(pass, resource, ResourceUsageKind.TransferRead);

    public void WritesTransfer(RenderGraphPass pass, RenderGraphResource resource) =>
        AddAccess(pass, resource, ResourceUsageKind.TransferWrite);

    public void ReadsTransferSource(RenderGraphPass pass, RenderGraphResource resource) => ReadsTransfer(pass, resource);

    public void WritesTransferDestination(RenderGraphPass pass, RenderGraphResource resource) => WritesTransfer(pass, resource);

    public void Presents(RenderGraphPass pass, RenderGraphResource resource) =>
        AddAccess(pass, resource, ResourceUsageKind.Present);

    public void WritesColor(
        RenderGraphPass pass,
        RenderGraphResource resource,
        bool clear = true,
        in Vector4 clearColor = default)
    {
        ValidatePassResource(pass, resource);
        if (resource.IsDepth)
            throw new InvalidOperationException($"Resource '{resource.Name}' is a depth texture and cannot be used as a color attachment.");
        AddAccess(pass, new RenderResourceAccess
        {
            Resource = resource,
            Usage = ResourceUsageKind.ColorAttachmentWrite,
            ClearOnLoad = clear,
            ClearColor = clearColor,
        });
    }

    public void SetColorClear(RenderGraphPass pass, RenderGraphResource resource, in Vector4 clearColor)
    {
        ThrowIfDisposed();
        ValidatePassResource(pass, resource);
        if (_compilation is null)
            throw new InvalidOperationException("The render graph must be compiled before changing a clear color.");
        if (!pass.SetClearColor(resource, clearColor))
            throw new InvalidOperationException($"Pass '{pass.Name}' has no clear color attachment for resource '{resource.Name}'.");
    }

    public void ReadsColor(RenderGraphPass pass, RenderGraphResource resource)
    {
        ValidatePassResource(pass, resource);
        if (resource.IsDepth)
            throw new InvalidOperationException($"Resource '{resource.Name}' is a depth texture and cannot be used as a color attachment.");
        AddAccess(pass, resource, ResourceUsageKind.ColorAttachmentRead);
    }

    public void WritesDepth(
        RenderGraphPass pass,
        RenderGraphResource resource,
        bool clear = true,
        float clearDepth = 1f)
    {
        ValidatePassResource(pass, resource);
        if (!resource.IsDepth)
            throw new InvalidOperationException($"Resource '{resource.Name}' is not a depth texture.");
        AddAccess(pass, new RenderResourceAccess
        {
            Resource = resource,
            Usage = ResourceUsageKind.DepthAttachmentWrite,
            ClearOnLoad = clear,
            ClearDepth = clearDepth,
        });
    }

    public void ReadsDepth(RenderGraphPass pass, RenderGraphResource resource)
    {
        ValidatePassResource(pass, resource);
        if (!resource.IsDepth)
            throw new InvalidOperationException($"Resource '{resource.Name}' is not a depth texture.");
        AddAccess(pass, resource, ResourceUsageKind.DepthAttachmentRead);
    }

    public void Realize(RenderGraphResource resource)
    {
        ThrowIfDisposed();
        ValidateResource(resource);
        if (resource.Transient && !resource.IsPinned)
        {
            resource.IsPinned = true;
            InvalidateCompilation();
        }
        Compile();
        EnsureCompiledTextures();
    }

    public RenderGraphCompilation Compile()
    {
        ThrowIfDisposed();
        if (_compilation is not null)
            return _compilation;

        Dictionary<RenderGraphResource, (ITexture Texture, TextureDescription Description)> previousTextures = [];
        foreach (RenderGraphResource resource in _resources.Where(resource => resource.Transient && resource.Texture is not null))
            previousTextures.Add(resource, (resource.Texture!, resource.PhysicalDescription));

        foreach (RenderGraphPass pass in _passes)
            pass.ResetCompilation();
        foreach (RenderGraphResource resource in _resources)
        {
            resource.FirstUse = -1;
            resource.LastUse = -1;
            resource.LastUsedPass = -1;
            resource.IsAliased = false;
            resource.AliasPredecessor = null;
            resource.CompiledDescription = resource.Description;
            if (resource.Transient)
                resource.PhysicalAllocationId = -1;
        }

        foreach (RenderGraphMemoryResource resource in _memoryResources)
        {
            resource.FirstUse = -1;
            resource.LastUse = -1;
            resource.LastUsedPass = -1;
            if (resource is RenderGraphBufferResource buffer)
                buffer.CompiledDescription = buffer.Description;
        }

        Dictionary<RenderGraphPass, List<CompiledDeclaration>> declarations = ValidatePassDeclarations();
        Dictionary<RenderGraphPass, IReadOnlyList<RenderGraphCompiledMemoryAccess>> memoryAccesses = ValidateMemoryDeclarations();
        GraphEdges edges = BuildDependencies(declarations, memoryAccesses);
        IReadOnlyList<RenderGraphPass> orderedPasses = StableTopologicalSort(edges);
        Dictionary<RenderGraphPass, IReadOnlyList<RenderGraphCompiledAccess>> compiledAccesses = BuildCompiledAccesses(orderedPasses, declarations);
        ComputeLifetimes(orderedPasses, compiledAccesses);
        PlanTransientAllocations();
        PlanBufferAllocations(orderedPasses, memoryAccesses);
        ValidateImportedUsages();
        (IReadOnlyList<RenderGraphBarrier> barriers, IReadOnlyList<RenderGraphBarrier> tailBarriers) =
            BuildBarriers(orderedPasses, compiledAccesses);
        (IReadOnlyList<RenderGraphMemoryBarrier> memoryBarriers, IReadOnlyList<RenderGraphMemoryBarrier> tailMemoryBarriers) =
            BuildMemoryBarriers(orderedPasses, memoryAccesses);

        foreach (RenderGraphPass pass in orderedPasses)
        {
            IReadOnlyList<RenderGraphPass> dependencies = edges.Incoming[pass]
                .OrderBy(dependency => dependency.DeclarationIndex)
                .ToArray();
            IReadOnlyList<RenderGraphBarrier> barriersBefore = barriers
                .Where(barrier => ReferenceEquals(barrier.Pass, pass) && !barrier.IsFinalTransition)
                .ToArray();
            IReadOnlyList<RenderGraphBarrier> barriersAfter = barriers
                .Where(barrier => ReferenceEquals(barrier.Pass, pass) && barrier.IsFinalTransition)
                .ToArray();
            pass.SetCompilation(
                dependencies,
                barriersBefore,
                barriersAfter,
                compiledAccesses[pass],
                memoryAccesses[pass],
                memoryBarriers.Where(barrier => ReferenceEquals(barrier.Pass, pass) && !barrier.IsFinalTransition).ToArray(),
                memoryBarriers.Where(barrier => ReferenceEquals(barrier.Pass, pass) && barrier.IsFinalTransition).ToArray());
        }

        ReconcileTextureAllocations(previousTextures);
        _compilation = new RenderGraphCompilation(
            orderedPasses.ToArray(),
            _resources.ToArray(),
            barriers.ToArray(),
            tailBarriers.ToArray(),
            _memoryResources.ToArray(),
            memoryBarriers.ToArray(),
            tailMemoryBarriers.ToArray());
        return _compilation;
    }

    public void Execute(ICommandBuffer commandBuffer)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(commandBuffer);
        RenderGraphCompilation compilation = Compile();
        EnsureCompiledTextures();
        EnsureCompiledBuffers();
        PrepareExecutionStates();

        foreach (RenderGraphPass pass in compilation.OrderedPasses)
            ExecutePass(pass, commandBuffer);
        foreach (RenderGraphBarrier barrier in compilation.TailBarriers)
            ExecuteBarrier(barrier, commandBuffer);
        foreach (RenderGraphMemoryResource resource in _memoryResources.Where(resource => resource.IsImported && resource.FirstUse >= 0 && resource.FinalState is not null))
        {
            RenderGraphMemoryState finalState = resource.FinalState!.Value;
            TransitionMemoryResource(resource, finalState, IsWriteAccess(finalState.Access), commandBuffer);
            resource.CurrentState = finalState;
        }
        foreach (RenderGraphResource resource in _resources)
            resource.UsedThisFrame = false;
        foreach (RenderGraphMemoryResource resource in _memoryResources)
            resource.UsedThisFrame = false;
    }

    public ITexture GetTexture(RenderGraphResource resource)
    {
        ThrowIfDisposed();
        ValidateResource(resource);
        if (resource.Texture is null)
        {
            Compile();
            EnsureCompiledTextures();
        }
        return resource.Texture ?? throw new InvalidOperationException($"Resource '{resource.Name}' has no physical texture.");
    }

    private RenderGraphPass AddPass(
        string name,
        RenderPassKind kind,
        Action<ICommandBuffer, RenderGraph> execute)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(execute);
        RenderGraphPass pass = new(this, _passes.Count, ValidateName(name, nameof(name)), kind, execute);
        _passes.Add(pass);
        InvalidateCompilation();
        return pass;
    }

    private RenderGraphPass AddPassBefore(
        RenderGraphPass before,
        string name,
        RenderPassKind kind,
        Action<ICommandBuffer, RenderGraph> execute)
    {
        ThrowIfDisposed();
        ValidatePass(before);
        ArgumentNullException.ThrowIfNull(execute);
        int insertionIndex = _passes.IndexOf(before);
        RenderGraphPass pass = new(this, insertionIndex, ValidateName(name, nameof(name)), kind, execute);
        _passes.Insert(insertionIndex, pass);
        for (int index = insertionIndex + 1; index < _passes.Count; index++)
            _passes[index].DeclarationIndex = index;
        InvalidateCompilation();
        return pass;
    }

    private void AddAccess(
        RenderGraphPass pass,
        RenderGraphResource resource,
        ResourceUsageKind usage,
        PipelineStages stages = PipelineStages.None) =>
        AddAccess(pass, new RenderResourceAccess
        {
            Resource = resource,
            Usage = usage,
            Stages = stages,
        });

    private void AddAccess(RenderGraphPass pass, in RenderResourceAccess access)
    {
        ThrowIfDisposed();
        ValidatePassResource(pass, access.Resource);
        pass.MutableAccesses.Add(access);
        InvalidateCompilation();
    }

    private void RebindImportedTexture(
        RenderGraphResource resource,
        ITexture texture,
        RenderGraphResourceState? initialState,
        RenderGraphResourceState? finalState,
        bool updateFinalState)
    {
        ThrowIfDisposed();
        ValidateResource(resource);
        ArgumentNullException.ThrowIfNull(texture);
        if (resource.Transient)
            throw new InvalidOperationException($"Resource '{resource.Name}' is transient and cannot be rebound as an imported texture.");
        ValidateImportedTexture(resource, texture);
        resource.Texture = texture;
        bool compilationChanged = false;
        if (initialState is RenderGraphResourceState state && state != resource.InitialState)
        {
            resource.InitialState = state;
            compilationChanged = true;
        }
        if (updateFinalState && finalState != resource.FinalState)
        {
            resource.FinalState = finalState;
            compilationChanged = true;
        }
        resource.CurrentState = resource.InitialState;
        if (compilationChanged)
            InvalidateCompilation();
    }

    private Dictionary<RenderGraphPass, List<CompiledDeclaration>> ValidatePassDeclarations()
    {
        Dictionary<RenderGraphPass, List<CompiledDeclaration>> result = [];
        foreach (RenderGraphPass pass in _passes)
        {
            Dictionary<RenderGraphResource, List<CompiledDeclaration>> byResource = [];
            List<CompiledDeclaration> passDeclarations = [];
            foreach (RenderResourceAccess access in pass.MutableAccesses)
            {
                ValidateResource(access.Resource);
                CompiledDeclaration declaration = DescribeAccess(pass, access);
                passDeclarations.Add(declaration);
                if (!byResource.TryGetValue(access.Resource, out List<CompiledDeclaration>? resourceDeclarations))
                {
                    resourceDeclarations = [];
                    byResource.Add(access.Resource, resourceDeclarations);
                }
                resourceDeclarations.Add(declaration);
            }

            foreach ((RenderGraphResource resource, List<CompiledDeclaration> resourceDeclarations) in byResource)
            {
                if (resourceDeclarations.Count <= 1)
                    continue;
                string usages = string.Join(", ", resourceDeclarations.Select(item => item.Access.Usage));
                throw new RenderGraphValidationException(
                    $"Pass '{pass.Name}' declares conflicting accesses to resource '{resource.Name}': {usages}. " +
                    "Use a single StorageReadWrite access when simultaneous shader read/write is required.");
            }
            result.Add(pass, passDeclarations);
        }
        return result;
    }

    private GraphEdges BuildDependencies(
        Dictionary<RenderGraphPass, List<CompiledDeclaration>> declarations,
        Dictionary<RenderGraphPass, IReadOnlyList<RenderGraphCompiledMemoryAccess>> memoryAccesses)
    {
        GraphEdges edges = new(_passes);
        foreach (RenderGraphPass pass in _passes)
        foreach (RenderGraphPass dependency in pass.ExplicitDependencies)
            edges.Add(dependency, pass);

        foreach (RenderGraphResource resource in _resources)
        {
            AddResourceDependencies(edges, resource.Name, resource.Transient, pass =>
            {
                CompiledDeclaration? declaration = declarations[pass]
                    .FirstOrDefault(candidate => ReferenceEquals(candidate.Access.Resource, resource));
                return declaration is null ? null : (declaration.Reads, declaration.Writes);
            });
        }
        foreach (RenderGraphMemoryResource resource in _memoryResources)
        {
            AddResourceDependencies(edges, resource.Name, resource.Transient, pass =>
            {
                RenderGraphCompiledMemoryAccess? access = memoryAccesses[pass]
                    .FirstOrDefault(candidate => ReferenceEquals(candidate.Resource, resource));
                return access is null ? null : (access.Reads, access.Writes);
            });
        }
        return edges;
    }

    private void AddResourceDependencies(
        GraphEdges edges,
        string resourceName,
        bool transient,
        Func<RenderGraphPass, (bool Reads, bool Writes)?> describe)
    {
        RenderGraphPass? lastWriter = null;
        List<RenderGraphPass> readersSinceWrite = [];
        foreach (RenderGraphPass pass in _passes)
        {
            (bool Reads, bool Writes)? declaration = describe(pass);
            if (declaration is not { } access)
                continue;
            if (access.Reads && lastWriter is null && transient)
            {
                throw new RenderGraphValidationException(
                    $"Pass '{pass.Name}' reads transient resource '{resourceName}' before any pass writes it.");
            }
            if (access.Reads)
            {
                if (lastWriter is not null)
                    edges.Add(lastWriter, pass);
                if (!readersSinceWrite.Contains(pass))
                    readersSinceWrite.Add(pass);
            }
            if (access.Writes)
            {
                if (lastWriter is not null)
                    edges.Add(lastWriter, pass);
                foreach (RenderGraphPass reader in readersSinceWrite)
                {
                    if (!ReferenceEquals(reader, pass))
                        edges.Add(reader, pass);
                }
                readersSinceWrite.Clear();
                lastWriter = pass;
            }
        }
    }

    private IReadOnlyList<RenderGraphPass> StableTopologicalSort(GraphEdges edges)
    {
        Dictionary<RenderGraphPass, int> indegree = _passes.ToDictionary(pass => pass, pass => edges.Incoming[pass].Count);
        SortedSet<RenderGraphPass> ready = new(PassDeclarationComparer.Instance);
        foreach (RenderGraphPass pass in _passes)
        {
            if (indegree[pass] == 0)
                ready.Add(pass);
        }

        List<RenderGraphPass> ordered = [];
        while (ready.Count > 0)
        {
            RenderGraphPass pass = ready.Min!;
            ready.Remove(pass);
            pass.ExecutionIndex = ordered.Count;
            ordered.Add(pass);
            foreach (RenderGraphPass dependent in edges.Outgoing[pass].OrderBy(item => item.DeclarationIndex))
            {
                indegree[dependent]--;
                if (indegree[dependent] == 0)
                    ready.Add(dependent);
            }
        }

        if (ordered.Count != _passes.Count)
        {
            string cyclePasses = string.Join(", ", _passes.Where(pass => indegree[pass] > 0).Select(pass => $"'{pass.Name}'"));
            throw new RenderGraphValidationException($"Render graph contains a dependency cycle involving {cyclePasses}.");
        }
        return ordered;
    }

    private Dictionary<RenderGraphPass, IReadOnlyList<RenderGraphCompiledAccess>> BuildCompiledAccesses(
        IReadOnlyList<RenderGraphPass> orderedPasses,
        Dictionary<RenderGraphPass, List<CompiledDeclaration>> declarations)
    {
        Dictionary<RenderGraphPass, IReadOnlyList<RenderGraphCompiledAccess>> result = [];
        foreach (RenderGraphPass pass in orderedPasses)
        {
            RenderGraphCompiledAccess[] accesses = declarations[pass]
                .Select(declaration => new RenderGraphCompiledAccess(
                    declaration.Access,
                    ResolveState(pass, declaration),
                    declaration.Reads,
                    declaration.Writes))
                .ToArray();
            result.Add(pass, accesses);
        }
        return result;
    }

    private void ComputeLifetimes(
        IReadOnlyList<RenderGraphPass> orderedPasses,
        Dictionary<RenderGraphPass, IReadOnlyList<RenderGraphCompiledAccess>> compiledAccesses)
    {
        for (int passIndex = 0; passIndex < orderedPasses.Count; passIndex++)
        {
            RenderGraphPass pass = orderedPasses[passIndex];
            foreach (RenderGraphCompiledAccess access in compiledAccesses[pass])
            {
                RenderGraphResource resource = access.Resource;
                if (resource.FirstUse < 0)
                    resource.FirstUse = passIndex;
                resource.LastUse = passIndex;
                resource.LastUsedPass = passIndex;
            }
        }
    }

    private void PlanTransientAllocations()
    {
        List<AllocationPlan> plans = [];
        foreach (RenderGraphResource resource in _resources.Where(item => item.Transient && item.FirstUse >= 0).OrderBy(item => item.FirstUse).ThenBy(item => item.DeclarationIndex))
        {
            TextureUsage usage = TextureUsage.None;
            foreach (RenderGraphPass pass in _passes)
            foreach (RenderResourceAccess access in pass.MutableAccesses)
            {
                if (ReferenceEquals(access.Resource, resource))
                    usage |= UsageFlag(access.Usage);
            }
            TextureDescription compiledDescription = WithUsage(resource.Description, resource.Description.Usage | usage);
            resource.CompiledDescription = compiledDescription;
            TextureDescriptionKey key = TextureDescriptionKey.From(compiledDescription);
            AllocationPlan? reusable = resource.IsPinned
                ? null
                : plans
                    .Where(plan => plan.Key == key && plan.LastUse < resource.FirstUse && !plan.Pinned)
                    .OrderBy(plan => plan.LastUse)
                    .ThenBy(plan => plan.Id)
                    .FirstOrDefault();
            if (reusable is null)
            {
                AllocationPlan plan = new(_nextPhysicalAllocationId++, key, resource.LastUse, resource.IsPinned, resource);
                plans.Add(plan);
                resource.PhysicalAllocationId = plan.Id;
            }
            else
            {
                resource.PhysicalAllocationId = reusable.Id;
                resource.IsAliased = true;
                resource.AliasPredecessor = reusable.LastResource;
                reusable.LastUse = resource.LastUse;
                reusable.LastResource = resource;
            }
        }
    }

    private void ValidateImportedUsages()
    {
        foreach (RenderGraphResource resource in _resources.Where(item => !item.Transient && item.Description.Usage != TextureUsage.None))
        {
            TextureUsage required = TextureUsage.None;
            foreach (RenderGraphPass pass in _passes)
            foreach (RenderResourceAccess access in pass.MutableAccesses)
            {
                if (ReferenceEquals(access.Resource, resource))
                    required |= UsageFlag(access.Usage);
            }
            TextureUsage missing = required & ~resource.Description.Usage;
            if (missing != TextureUsage.None)
            {
                throw new RenderGraphValidationException(
                    $"Imported texture '{resource.Name}' is missing required usage flags: {missing}.");
            }
        }
    }

    private (IReadOnlyList<RenderGraphBarrier> Barriers, IReadOnlyList<RenderGraphBarrier> TailBarriers) BuildBarriers(
        IReadOnlyList<RenderGraphPass> orderedPasses,
        Dictionary<RenderGraphPass, IReadOnlyList<RenderGraphCompiledAccess>> compiledAccesses)
    {
        Dictionary<RenderGraphResource, RenderGraphResourceState> states = [];
        List<RenderGraphBarrier> barriers = [];
        List<RenderGraphBarrier> tailBarriers = [];
        foreach (RenderGraphResource resource in _resources)
            states[resource] = resource.Transient ? RenderGraphResourceState.Undefined : resource.InitialState;

        foreach (RenderGraphPass pass in orderedPasses)
        {
            foreach (RenderGraphCompiledAccess access in compiledAccesses[pass])
            {
                RenderGraphResource resource = access.Resource;
                RenderGraphResourceState sourceState = states[resource];
                RenderGraphResource? sourceResource = resource;
                if (resource.IsAliased && resource.FirstUse == pass.ExecutionIndex)
                {
                    sourceState = RenderGraphResourceState.Undefined;
                    sourceResource = resource.AliasPredecessor;
                }
                if (NeedsBarrier(sourceState, access.State, access.Reads, access.Writes) || sourceResource != resource)
                {
                    barriers.Add(new RenderGraphBarrier(
                        resource,
                        sourceResource,
                        pass,
                        sourceState,
                        access.State,
                        false));
                }
                states[resource] = MergeTextureState(sourceState, access.State);
            }
        }

        foreach (RenderGraphResource resource in _resources.Where(item => !item.Transient && item.FinalState is not null && item.FirstUse >= 0))
        {
            RenderGraphResourceState sourceState = states[resource];
            RenderGraphResourceState destinationState = resource.FinalState!.Value;
            if (!NeedsBarrier(sourceState, destinationState, destinationReads: true, destinationWrites: false))
                continue;
            RenderGraphBarrier barrier = new(resource, resource, null, sourceState, destinationState, true);
            barriers.Add(barrier);
            tailBarriers.Add(barrier);
            states[resource] = destinationState;
        }

        return (barriers, tailBarriers);
    }

    private Dictionary<RenderGraphPass, IReadOnlyList<RenderGraphCompiledMemoryAccess>> ValidateMemoryDeclarations()
    {
        Dictionary<RenderGraphPass, IReadOnlyList<RenderGraphCompiledMemoryAccess>> result = [];
        foreach (RenderGraphPass pass in _passes)
        {
            HashSet<RenderGraphMemoryResource> seen = [];
            List<RenderGraphCompiledMemoryAccess> accesses = [];
            foreach (RenderGraphMemoryAccess declaration in pass.MutableMemoryAccesses)
            {
                ValidateResource(declaration.Resource);
                if (!seen.Add(declaration.Resource))
                    throw new RenderGraphValidationException($"Pass '{pass.Name}' declares conflicting accesses to resource '{declaration.Resource.Name}'. Use a single StorageReadWrite or AccelerationStructureReadWrite access.");
                bool accelerationStructure = declaration.Resource is RenderGraphAccelerationStructureResource;
                bool accelerationStructureUsage = declaration.Usage is RenderGraphMemoryUsageKind.AccelerationStructureRead or RenderGraphMemoryUsageKind.AccelerationStructureWrite or RenderGraphMemoryUsageKind.AccelerationStructureReadWrite;
                if (accelerationStructure != accelerationStructureUsage)
                    throw new RenderGraphValidationException($"Pass '{pass.Name}' uses incompatible access '{declaration.Usage}' for resource '{declaration.Resource.Name}'.");
                if (declaration.Usage is RenderGraphMemoryUsageKind.AccelerationStructureBuildInput or RenderGraphMemoryUsageKind.AccelerationStructureScratch or RenderGraphMemoryUsageKind.AccelerationStructureWrite or RenderGraphMemoryUsageKind.AccelerationStructureReadWrite
                    && pass.Kind != RenderPassKind.AccelerationStructureBuild)
                    throw new RenderGraphValidationException($"Pass '{pass.Name}' must be an acceleration structure build pass for access '{declaration.Usage}'.");
                if (declaration.Usage == RenderGraphMemoryUsageKind.ShaderBindingTable && pass.Kind != RenderPassKind.RayTracing)
                    throw new RenderGraphValidationException($"Pass '{pass.Name}' must be a ray tracing pass for shader binding table access.");
                bool reads = declaration.Usage is RenderGraphMemoryUsageKind.ShaderRead or RenderGraphMemoryUsageKind.StorageRead or RenderGraphMemoryUsageKind.StorageReadWrite or RenderGraphMemoryUsageKind.TransferRead or RenderGraphMemoryUsageKind.AccelerationStructureBuildInput or RenderGraphMemoryUsageKind.ShaderBindingTable or RenderGraphMemoryUsageKind.AccelerationStructureRead or RenderGraphMemoryUsageKind.AccelerationStructureReadWrite;
                bool writes = declaration.Usage is RenderGraphMemoryUsageKind.StorageWrite or RenderGraphMemoryUsageKind.StorageReadWrite or RenderGraphMemoryUsageKind.TransferWrite or RenderGraphMemoryUsageKind.AccelerationStructureScratch or RenderGraphMemoryUsageKind.AccelerationStructureWrite or RenderGraphMemoryUsageKind.AccelerationStructureReadWrite;
                if (!reads && !writes)
                    throw new RenderGraphValidationException($"Pass '{pass.Name}' uses unsupported access '{declaration.Usage}'.");
                accesses.Add(new RenderGraphCompiledMemoryAccess(declaration, ResolveMemoryState(pass, declaration), reads, writes));
            }
            result.Add(pass, accesses);
        }
        return result;
    }

    private static RenderGraphMemoryState ResolveMemoryState(RenderGraphPass pass, in RenderGraphMemoryAccess access)
    {
        PipelineStages stages = access.Stages == PipelineStages.None ? DefaultShaderStages(pass.Kind) : access.Stages;
        return access.Usage switch
        {
            RenderGraphMemoryUsageKind.ShaderRead => new(stages,
                access.Resource is RenderGraphBufferResource buffer && (buffer.Description.Usage & BufferUsage.Uniform) != 0
                    ? ResourceAccess.UniformRead : ResourceAccess.ShaderRead),
            RenderGraphMemoryUsageKind.StorageRead => new(stages, ResourceAccess.ShaderRead),
            RenderGraphMemoryUsageKind.StorageWrite => new(stages, ResourceAccess.ShaderWrite),
            RenderGraphMemoryUsageKind.StorageReadWrite => new(stages, ResourceAccess.ShaderRead | ResourceAccess.ShaderWrite),
            RenderGraphMemoryUsageKind.TransferRead => new(PipelineStages.Transfer, ResourceAccess.TransferRead),
            RenderGraphMemoryUsageKind.TransferWrite => new(PipelineStages.Transfer, ResourceAccess.TransferWrite),
            RenderGraphMemoryUsageKind.AccelerationStructureBuildInput => new(PipelineStages.AccelerationStructureBuild, ResourceAccess.ShaderRead),
            RenderGraphMemoryUsageKind.AccelerationStructureScratch => new(PipelineStages.AccelerationStructureBuild, ResourceAccess.AccelerationStructureRead | ResourceAccess.AccelerationStructureWrite),
            RenderGraphMemoryUsageKind.ShaderBindingTable => new(PipelineStages.RayTracingShader, ResourceAccess.ShaderRead),
            RenderGraphMemoryUsageKind.AccelerationStructureRead => new(stages, ResourceAccess.AccelerationStructureRead),
            RenderGraphMemoryUsageKind.AccelerationStructureWrite => new(PipelineStages.AccelerationStructureBuild, ResourceAccess.AccelerationStructureWrite),
            RenderGraphMemoryUsageKind.AccelerationStructureReadWrite => new(PipelineStages.AccelerationStructureBuild, ResourceAccess.AccelerationStructureRead | ResourceAccess.AccelerationStructureWrite),
            _ => throw new RenderGraphValidationException($"Unsupported memory usage '{access.Usage}'."),
        };
    }

    private static PipelineStages DefaultShaderStages(RenderPassKind kind) => kind switch
    {
        RenderPassKind.Graphics => PipelineStages.FragmentShader,
        RenderPassKind.Compute => PipelineStages.ComputeShader,
        RenderPassKind.Transfer => PipelineStages.Transfer,
        RenderPassKind.AccelerationStructureBuild => PipelineStages.AccelerationStructureBuild,
        RenderPassKind.RayTracing => PipelineStages.RayTracingShader,
        _ => PipelineStages.AllCommands,
    };

    private void PlanBufferAllocations(
        IReadOnlyList<RenderGraphPass> orderedPasses,
        Dictionary<RenderGraphPass, IReadOnlyList<RenderGraphCompiledMemoryAccess>> accesses)
    {
        Dictionary<RenderGraphBufferResource, BufferUsage> usages = [];
        foreach (RenderGraphPass pass in orderedPasses)
        foreach (RenderGraphCompiledMemoryAccess access in accesses[pass])
        {
            RenderGraphMemoryResource resource = access.Resource;
            if (resource.FirstUse < 0)
                resource.FirstUse = pass.ExecutionIndex;
            resource.LastUse = pass.ExecutionIndex;
            resource.LastUsedPass = pass.ExecutionIndex;
            if (resource is RenderGraphBufferResource buffer)
                usages[buffer] = usages.GetValueOrDefault(buffer) | BufferUsageFlag(access.Declaration);
        }
        foreach ((RenderGraphBufferResource buffer, BufferUsage usage) in usages)
        {
            if (buffer.Transient)
            {
                buffer.CompiledDescription = buffer.Description with { Usage = buffer.Description.Usage | usage };
                if (buffer.PhysicalAllocationId < 0)
                    buffer.PhysicalAllocationId = _nextPhysicalAllocationId++;
            }
            else if ((usage & ~buffer.Description.Usage) != 0)
                throw new RenderGraphValidationException($"Imported buffer '{buffer.Name}' is missing required usage flags: {usage & ~buffer.Description.Usage}.");
        }
    }

    private static BufferUsage BufferUsageFlag(in RenderGraphMemoryAccess access) => access.Usage switch
    {
        RenderGraphMemoryUsageKind.ShaderRead => access.Resource is RenderGraphBufferResource buffer && (buffer.Description.Usage & BufferUsage.Uniform) != 0 ? BufferUsage.Uniform : BufferUsage.Storage,
        RenderGraphMemoryUsageKind.StorageRead or RenderGraphMemoryUsageKind.StorageWrite or RenderGraphMemoryUsageKind.StorageReadWrite => BufferUsage.Storage,
        RenderGraphMemoryUsageKind.TransferRead => BufferUsage.TransferSource,
        RenderGraphMemoryUsageKind.TransferWrite => BufferUsage.TransferDestination,
        RenderGraphMemoryUsageKind.AccelerationStructureBuildInput => BufferUsage.AccelerationStructureBuildInput | BufferUsage.DeviceAddress,
        RenderGraphMemoryUsageKind.AccelerationStructureScratch => BufferUsage.Storage | BufferUsage.DeviceAddress,
        RenderGraphMemoryUsageKind.ShaderBindingTable => BufferUsage.ShaderBindingTable | BufferUsage.DeviceAddress,
        _ => BufferUsage.None,
    };

    private (IReadOnlyList<RenderGraphMemoryBarrier> Barriers, IReadOnlyList<RenderGraphMemoryBarrier> TailBarriers) BuildMemoryBarriers(
        IReadOnlyList<RenderGraphPass> orderedPasses,
        Dictionary<RenderGraphPass, IReadOnlyList<RenderGraphCompiledMemoryAccess>> accesses)
    {
        Dictionary<RenderGraphMemoryResource, RenderGraphMemoryState> states = _memoryResources.ToDictionary(resource => resource, resource => resource.InitialState);
        List<RenderGraphMemoryBarrier> barriers = [];
        List<RenderGraphMemoryBarrier> tailBarriers = [];
        foreach (RenderGraphPass pass in orderedPasses)
        foreach (RenderGraphCompiledMemoryAccess access in accesses[pass])
        {
            RenderGraphMemoryState source = states[access.Resource];
            if (NeedsMemoryBarrier(source, access.State, access.Writes))
                barriers.Add(new RenderGraphMemoryBarrier(access.Resource, pass, source, access.State, false));
            states[access.Resource] = MergeMemoryState(source, access.State);
        }
        foreach (RenderGraphMemoryResource resource in _memoryResources.Where(resource => resource.IsImported && resource.FirstUse >= 0 && resource.FinalState is not null))
        {
            RenderGraphMemoryState destination = resource.FinalState!.Value;
            RenderGraphMemoryState source = states[resource];
            if (!NeedsMemoryBarrier(source, destination, IsWriteAccess(destination.Access)))
                continue;
            RenderGraphMemoryBarrier barrier = new(resource, null, source, destination, true);
            barriers.Add(barrier);
            tailBarriers.Add(barrier);
        }
        return (barriers, tailBarriers);
    }

    private static bool NeedsMemoryBarrier(in RenderGraphMemoryState source, in RenderGraphMemoryState destination, bool destinationWrites) =>
        source != destination || destinationWrites || IsWriteAccess(source.Access);

    private static RenderGraphMemoryState MergeMemoryState(in RenderGraphMemoryState source, in RenderGraphMemoryState destination) =>
        IsWriteAccess(source.Access) || IsWriteAccess(destination.Access)
            ? destination
            : new(source.Stages | destination.Stages, source.Access | destination.Access);

    private void EnsureCompiledBuffers()
    {
        foreach (RenderGraphBufferResource resource in _buffers.Where(resource => resource.Transient && (resource.FirstUse >= 0 || resource.IsPinned)))
        {
            if (resource.Buffer is not null && resource.PhysicalDescription == resource.CompiledDescription)
                continue;
            IBuffer buffer = _device.CreateBuffer(resource.CompiledDescription);
            _ownedBuffers.Add(buffer);
            resource.Buffer = buffer;
            resource.PhysicalDescription = resource.CompiledDescription;
            resource.PhysicalAllocationId = _nextPhysicalAllocationId++;
        }
    }

    private void AddMemoryAccess(
        RenderGraphPass pass,
        RenderGraphMemoryResource resource,
        RenderGraphMemoryUsageKind usage,
        PipelineStages stages = PipelineStages.None)
    {
        ThrowIfDisposed();
        ValidatePass(pass);
        ValidateResource(resource);
        pass.MutableMemoryAccesses.Add(new RenderGraphMemoryAccess { Resource = resource, Usage = usage, Stages = stages });
        InvalidateCompilation();
    }

    public void Realize(RenderGraphBufferResource resource)
    {
        ThrowIfDisposed();
        ValidateResource(resource);
        resource.IsPinned = resource.Transient;
        Compile();
        EnsureCompiledBuffers();
    }

    public IBuffer GetBuffer(RenderGraphBufferResource resource)
    {
        ThrowIfDisposed();
        ValidateResource(resource);
        if (resource.Buffer is null)
        {
            Compile();
            EnsureCompiledBuffers();
        }
        return resource.Buffer ?? throw new InvalidOperationException($"Resource '{resource.Name}' has no physical buffer.");
    }

    public IAccelerationStructure GetAccelerationStructure(RenderGraphAccelerationStructureResource resource)
    {
        ThrowIfDisposed();
        ValidateResource(resource);
        return resource.AccelerationStructure;
    }

    private void ValidateResource(RenderGraphMemoryResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (!ReferenceEquals(resource.Owner, this) || !_memoryResources.Contains(resource))
            throw new ArgumentException("Resource does not belong to this render graph.", nameof(resource));
    }

    private void ReconcileTextureAllocations(Dictionary<RenderGraphResource, (ITexture Texture, TextureDescription Description)> previousTextures)
    {
        Dictionary<int, ITexture> allocations = [];
        HashSet<ITexture> retained = [];
        foreach (RenderGraphResource resource in _resources.Where(resource => resource.Transient).OrderByDescending(resource => resource.IsPinned).ThenBy(resource => resource.DeclarationIndex))
        {
            resource.Texture = null;
            resource.PhysicalDescription = resource.CompiledDescription;
            if (resource.FirstUse < 0 && !resource.IsPinned)
                continue;
            if (resource.PhysicalAllocationId < 0)
                resource.PhysicalAllocationId = _nextPhysicalAllocationId++;
            if (allocations.ContainsKey(resource.PhysicalAllocationId))
                continue;
            if (previousTextures.TryGetValue(resource, out var previous)
                && previous.Description == resource.CompiledDescription
                && retained.Add(previous.Texture))
                allocations.Add(resource.PhysicalAllocationId, previous.Texture);
        }
        foreach (RenderGraphResource resource in _resources.Where(resource => resource.Transient))
            resource.Texture = allocations.GetValueOrDefault(resource.PhysicalAllocationId);
        foreach (List<PooledTexture> pool in _texturePool.Values)
        foreach (PooledTexture texture in pool)
            texture.InUse = retained.Contains(texture.Texture);
    }

    private void EnsureCompiledTextures()
    {
        Dictionary<int, ITexture> executionTextures = [];
        foreach (RenderGraphResource resource in _resources.Where(item => item.Transient && item.PhysicalAllocationId >= 0 && item.Texture is not null).OrderBy(item => item.DeclarationIndex))
            executionTextures.TryAdd(resource.PhysicalAllocationId, resource.Texture!);
        foreach (RenderGraphResource resource in _resources.Where(item => item.Transient && (item.FirstUse >= 0 || item.IsPinned)).OrderBy(item => item.DeclarationIndex))
        {
            if (resource.PhysicalAllocationId < 0)
            {
                TextureDescription description = resource.CompiledDescription;
                TextureDescriptionKey key = TextureDescriptionKey.From(description);
                resource.PhysicalAllocationId = _nextPhysicalAllocationId++;
                resource.PhysicalDescription = description;
                if (!executionTextures.TryGetValue(resource.PhysicalAllocationId, out ITexture? standaloneTexture))
                {
                    standaloneTexture = AcquirePooledTexture(key, description);
                    executionTextures.Add(resource.PhysicalAllocationId, standaloneTexture);
                }
                resource.Texture = standaloneTexture;
                continue;
            }

            if (!executionTextures.TryGetValue(resource.PhysicalAllocationId, out ITexture? texture))
            {
                texture = AcquirePooledTexture(TextureDescriptionKey.From(resource.PhysicalDescription), resource.PhysicalDescription);
                executionTextures.Add(resource.PhysicalAllocationId, texture);
            }
            resource.Texture = texture;
        }
    }

    private ITexture AcquirePooledTexture(TextureDescriptionKey key, in TextureDescription description)
    {
        if (!_texturePool.TryGetValue(key, out List<PooledTexture>? textures))
        {
            textures = [];
            _texturePool.Add(key, textures);
        }
        PooledTexture? available = textures.FirstOrDefault(item => !item.InUse);
        if (available is null)
        {
            ITexture texture = _device.CreateTexture(description);
            available = new PooledTexture(texture);
            textures.Add(available);
        }
        available.InUse = true;
        return available.Texture;
    }

    private void PrepareExecutionStates()
    {
        _executionTextureStates.Clear();
        HashSet<ITexture> usedTextures = [];
        foreach (RenderGraphResource resource in _resources)
        {
            resource.UsedThisFrame = false;
            resource.LastUsedPass = -1;
            resource.CurrentState = resource.Transient ? RenderGraphResourceState.Undefined : resource.InitialState;
            if (resource.Texture is not null)
                _executionTextureStates.TryAdd(resource.Texture, resource.CurrentState);
            if (resource.Transient && resource.Texture is not null)
                usedTextures.Add(resource.Texture);
        }
        foreach (RenderGraphMemoryResource resource in _memoryResources)
        {
            resource.UsedThisFrame = false;
            resource.LastUsedPass = -1;
            resource.CurrentState = resource.InitialState;
        }
        foreach (List<PooledTexture> pool in _texturePool.Values)
        foreach (PooledTexture texture in pool)
            texture.InUse = usedTextures.Contains(texture.Texture);
    }

    private void ExecutePass(RenderGraphPass pass, ICommandBuffer commandBuffer)
    {
        if (pass.ShouldExecute?.Invoke() == false)
            return;

        foreach (RenderGraphCompiledAccess access in pass.CompiledAccesses)
            TransitionTextureResource(access.Resource, access.State, access.Reads, access.Writes, commandBuffer);

        foreach (RenderGraphCompiledMemoryAccess access in pass.CompiledMemoryAccesses)
        {
            TransitionMemoryResource(access.Resource, access.State, access.Writes, commandBuffer);
            access.Resource.UsedThisFrame = true;
            access.Resource.LastUsedPass = pass.ExecutionIndex;
        }

        List<ColorAttachmentInfo> colorAttachments = [];
        DepthAttachmentInfo? depthAttachment = null;
        int areaWidth = 0;
        int areaHeight = 0;
        foreach (RenderGraphCompiledAccess access in pass.CompiledAccesses)
        {
            RenderGraphResource resource = access.Resource;
            resource.UsedThisFrame = true;
            resource.LastUsedPass = pass.ExecutionIndex;
            switch (access.Usage)
            {
                case ResourceUsageKind.ColorAttachmentWrite:
                    colorAttachments.Add(new ColorAttachmentInfo
                    {
                        Texture = resource.Texture!,
                        LoadOp = access.Declaration.ClearOnLoad ? AttachmentLoadOp.Clear : AttachmentLoadOp.Load,
                        StoreOp = AttachmentStoreOp.Store,
                        ClearColor = access.Declaration.ClearColor,
                    });
                    areaWidth = Math.Max(areaWidth, resource.CompiledDescription.Extent.Width);
                    areaHeight = Math.Max(areaHeight, resource.CompiledDescription.Extent.Height);
                    break;
                case ResourceUsageKind.ColorAttachmentRead:
                    colorAttachments.Add(new ColorAttachmentInfo
                    {
                        Texture = resource.Texture!,
                        LoadOp = AttachmentLoadOp.Load,
                        StoreOp = AttachmentStoreOp.Store,
                    });
                    areaWidth = Math.Max(areaWidth, resource.CompiledDescription.Extent.Width);
                    areaHeight = Math.Max(areaHeight, resource.CompiledDescription.Extent.Height);
                    break;
                case ResourceUsageKind.DepthAttachmentWrite:
                    depthAttachment = new DepthAttachmentInfo
                    {
                        Texture = resource.Texture!,
                        LoadOp = access.Declaration.ClearOnLoad ? AttachmentLoadOp.Clear : AttachmentLoadOp.Load,
                        StoreOp = AttachmentStoreOp.Store,
                        ClearDepth = access.Declaration.ClearDepth,
                    };
                    areaWidth = Math.Max(areaWidth, resource.CompiledDescription.Extent.Width);
                    areaHeight = Math.Max(areaHeight, resource.CompiledDescription.Extent.Height);
                    break;
                case ResourceUsageKind.DepthAttachmentRead:
                    depthAttachment = new DepthAttachmentInfo
                    {
                        Texture = resource.Texture!,
                        LoadOp = AttachmentLoadOp.Load,
                        StoreOp = AttachmentStoreOp.Store,
                    };
                    areaWidth = Math.Max(areaWidth, resource.CompiledDescription.Extent.Width);
                    areaHeight = Math.Max(areaHeight, resource.CompiledDescription.Extent.Height);
                    break;
            }
        }

        bool hasAttachments = colorAttachments.Count > 0 || depthAttachment is not null;

        commandBuffer.BeginTimestamp(pass.Name);
        try
        {
            if (hasAttachments)
                commandBuffer.BeginRendering(new RenderPassInfo
                {
                    ColorAttachments = [.. colorAttachments],
                    DepthAttachment = depthAttachment,
                    RenderArea = new Rect(0, 0, areaWidth, areaHeight),
                });

            pass.ExecuteAction?.Invoke(commandBuffer, this);

            if (hasAttachments)
                commandBuffer.EndRendering();
        }
        finally
        {
            commandBuffer.EndTimestamp();
        }
    }

    private static void TransitionMemoryResource(
        RenderGraphMemoryResource resource,
        in RenderGraphMemoryState destination,
        bool writes,
        ICommandBuffer commandBuffer)
    {
        RenderGraphMemoryState source = resource.CurrentState;
        if (NeedsMemoryBarrier(source, destination, writes))
        {
            if (resource is RenderGraphBufferResource buffer)
                commandBuffer.BufferBarrier(buffer.Buffer!, buffer.CompiledDescription.Size, source.Stages, source.Access, destination.Stages, destination.Access);
            else
                commandBuffer.MemoryBarrier(source.Stages, source.Access, destination.Stages, destination.Access);
        }
        resource.CurrentState = MergeMemoryState(source, destination);
    }

    private void ExecuteBarrier(RenderGraphBarrier barrier, ICommandBuffer commandBuffer) =>
        TransitionTextureResource(barrier.Resource, barrier.DestinationState, true, IsWriteAccess(barrier.DestinationAccess), commandBuffer);

    private void TransitionTextureResource(
        RenderGraphResource resource,
        in RenderGraphResourceState destination,
        bool reads,
        bool writes,
        ICommandBuffer commandBuffer)
    {
        ITexture texture = resource.Texture!;
        RenderGraphResourceState source = _executionTextureStates.GetValueOrDefault(texture, resource.CurrentState);
        if (NeedsBarrier(source, destination, reads, writes))
            commandBuffer.ImageBarrier(texture, source.Layout, destination.Layout, source.Stages, source.Access, destination.Stages, destination.Access);
        RenderGraphResourceState state = MergeTextureState(source, destination);
        _executionTextureStates[texture] = state;
        resource.CurrentState = state;
    }

    private static RenderGraphResourceState MergeTextureState(in RenderGraphResourceState source, in RenderGraphResourceState destination) =>
        source.Layout != destination.Layout || IsWriteAccess(source.Access) || IsWriteAccess(destination.Access)
            ? destination
            : new(destination.Layout, source.Stages | destination.Stages, source.Access | destination.Access);

    private CompiledDeclaration DescribeAccess(RenderGraphPass pass, in RenderResourceAccess access)
    {
        bool reads;
        bool writes;
        switch (access.Usage)
        {
            case ResourceUsageKind.ShaderRead:
            case ResourceUsageKind.StorageRead:
            case ResourceUsageKind.TransferRead:
            case ResourceUsageKind.Present:
            case ResourceUsageKind.ColorAttachmentRead:
            case ResourceUsageKind.DepthAttachmentRead:
                reads = true;
                writes = false;
                break;
            case ResourceUsageKind.ColorAttachmentWrite:
            case ResourceUsageKind.DepthAttachmentWrite:
            case ResourceUsageKind.StorageWrite:
            case ResourceUsageKind.TransferWrite:
                reads = false;
                writes = true;
                break;
            case ResourceUsageKind.StorageReadWrite:
                reads = true;
                writes = true;
                break;
            default:
                throw new RenderGraphValidationException($"Pass '{pass.Name}' uses unsupported access '{access.Usage}'.");
        }

        if (pass.Kind != RenderPassKind.Graphics && access.Usage is ResourceUsageKind.ColorAttachmentWrite or ResourceUsageKind.ColorAttachmentRead or ResourceUsageKind.DepthAttachmentWrite or ResourceUsageKind.DepthAttachmentRead)
        {
            throw new RenderGraphValidationException(
                $"Pass '{pass.Name}' is {pass.Kind} and cannot declare attachment access '{access.Usage}'.");
        }
        if (pass.Kind != RenderPassKind.Transfer && access.Usage == ResourceUsageKind.Present)
        {
            throw new RenderGraphValidationException(
                $"Pass '{pass.Name}' must be a transfer pass to declare present usage.");
        }
        if (access.Resource.IsDepth && access.Usage is ResourceUsageKind.ColorAttachmentWrite or ResourceUsageKind.ColorAttachmentRead)
            throw new RenderGraphValidationException($"Depth resource '{access.Resource.Name}' cannot be a color attachment.");
        if (!access.Resource.IsDepth && access.Usage is ResourceUsageKind.DepthAttachmentWrite or ResourceUsageKind.DepthAttachmentRead)
            throw new RenderGraphValidationException($"Color resource '{access.Resource.Name}' cannot be a depth attachment.");
        if (access.Resource.IsDepth && access.Usage is ResourceUsageKind.StorageRead or ResourceUsageKind.StorageWrite or ResourceUsageKind.StorageReadWrite)
            throw new RenderGraphValidationException($"Depth resource '{access.Resource.Name}' cannot be used as a storage texture.");

        return new CompiledDeclaration(access, reads, writes);
    }

    private static RenderGraphResourceState ResolveState(RenderGraphPass pass, CompiledDeclaration declaration)
    {
        PipelineStages defaultShaderStages = DefaultShaderStages(pass.Kind);
        PipelineStages requestedStages = declaration.Access.Stages;
        return declaration.Access.Usage switch
        {
            ResourceUsageKind.ColorAttachmentWrite => new(ImageLayout.ColorAttachmentOptimal, PipelineStages.ColorOutput, ResourceAccess.ColorWrite),
            ResourceUsageKind.ColorAttachmentRead => new(ImageLayout.ColorAttachmentOptimal, PipelineStages.ColorOutput, ResourceAccess.ColorRead),
            ResourceUsageKind.DepthAttachmentWrite => new(ImageLayout.DepthAttachmentOptimal, PipelineStages.EarlyDepth | PipelineStages.LateDepth, ResourceAccess.DepthWrite),
            ResourceUsageKind.DepthAttachmentRead => new(ImageLayout.DepthStencilReadOnlyOptimal, PipelineStages.EarlyDepth | PipelineStages.LateDepth, ResourceAccess.DepthRead),
            ResourceUsageKind.ShaderRead => new(
                declaration.Access.Resource.IsDepth ? ImageLayout.DepthStencilReadOnlyOptimal : ImageLayout.ShaderReadOnlyOptimal,
                requestedStages == PipelineStages.None ? defaultShaderStages : requestedStages,
                ResourceAccess.ShaderRead),
            ResourceUsageKind.StorageRead => new(
                ImageLayout.General,
                requestedStages == PipelineStages.None ? defaultShaderStages : requestedStages,
                ResourceAccess.ShaderRead),
            ResourceUsageKind.StorageWrite => new(
                ImageLayout.General,
                requestedStages == PipelineStages.None ? defaultShaderStages : requestedStages,
                ResourceAccess.ShaderWrite),
            ResourceUsageKind.StorageReadWrite => new(
                ImageLayout.General,
                requestedStages == PipelineStages.None ? defaultShaderStages : requestedStages,
                ResourceAccess.ShaderRead | ResourceAccess.ShaderWrite),
            ResourceUsageKind.TransferRead => new(ImageLayout.TransferSrcOptimal, PipelineStages.Transfer, ResourceAccess.TransferRead),
            ResourceUsageKind.TransferWrite => new(ImageLayout.TransferDstOptimal, PipelineStages.Transfer, ResourceAccess.TransferWrite),
            ResourceUsageKind.Present => new(ImageLayout.PresentSrcKhr, PipelineStages.Bottom, ResourceAccess.None),
            _ => throw new RenderGraphValidationException($"Unsupported resource usage '{declaration.Access.Usage}'."),
        };
    }

    private static bool NeedsBarrier(
        in RenderGraphResourceState source,
        in RenderGraphResourceState destination,
        bool destinationReads,
        bool destinationWrites)
    {
        if (source.Layout != destination.Layout)
            return true;
        if (source.Stages != destination.Stages || source.Access != destination.Access)
            return true;
        if (destinationWrites)
            return true;
        return destinationReads && IsWriteAccess(source.Access);
    }

    private static bool IsWriteAccess(ResourceAccess access) =>
        (access & (ResourceAccess.ShaderWrite |
                   ResourceAccess.ColorWrite |
                   ResourceAccess.DepthWrite |
                   ResourceAccess.TransferWrite |
                   ResourceAccess.HostWrite |
                   ResourceAccess.MemoryWrite |
                   ResourceAccess.AccelerationStructureWrite)) != 0;

    private static TextureUsage UsageFlag(ResourceUsageKind usage) => usage switch
    {
        ResourceUsageKind.ColorAttachmentWrite or ResourceUsageKind.ColorAttachmentRead => TextureUsage.ColorAttachment,
        ResourceUsageKind.DepthAttachmentWrite or ResourceUsageKind.DepthAttachmentRead => TextureUsage.DepthAttachment,
        ResourceUsageKind.ShaderRead => TextureUsage.Sampled,
        ResourceUsageKind.StorageRead or ResourceUsageKind.StorageWrite or ResourceUsageKind.StorageReadWrite => TextureUsage.Storage,
        ResourceUsageKind.TransferRead => TextureUsage.TransferSource,
        ResourceUsageKind.TransferWrite => TextureUsage.TransferDestination,
        ResourceUsageKind.Present => TextureUsage.None,
        _ => TextureUsage.None,
    };

    private static TextureDescription WithUsage(in TextureDescription description, TextureUsage usage) => new()
    {
        Extent = description.Extent,
        Format = description.Format,
        Usage = usage,
        MipLevels = description.MipLevels,
        ArrayLayers = description.ArrayLayers,
        Dimension = description.Dimension,
    };

    private static TextureDescription NormalizeDescription(in TextureDescription description) => new()
    {
        Extent = description.Extent,
        Format = description.Format,
        Usage = description.Usage,
        MipLevels = description.MipLevels == 0 ? 1u : description.MipLevels,
        ArrayLayers = description.ArrayLayers == 0
            ? (description.Dimension == TextureDimension.Cube ? 6u : 1u)
            : description.ArrayLayers,
        Dimension = description.Dimension,
    };

    private static void ValidateTextureDescription(in TextureDescription description, string parameterName)
    {
        if (description.Extent.Width <= 0 || description.Extent.Height <= 0)
            throw new ArgumentOutOfRangeException(parameterName, "Texture extent must be positive.");
        if (description.Format == TextureFormat.Unknown)
            throw new ArgumentException("Texture format must be specified.", parameterName);
        if (description.Dimension is < TextureDimension.Texture2D or > TextureDimension.Cube)
            throw new ArgumentOutOfRangeException(parameterName, "Texture dimension is not supported.");
        if (description.Dimension == TextureDimension.Texture2D && description.ArrayLayers is not (0 or 1))
            throw new ArgumentException("2D textures must have exactly one array layer.", parameterName);
        if (description.Dimension == TextureDimension.Cube
            && (description.Extent.Width != description.Extent.Height || (description.ArrayLayers != 0 && description.ArrayLayers != 6)))
            throw new ArgumentException("Cube textures must be square and have exactly six array layers.", parameterName);
    }

    private static string ValidateName(string name, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name must not be empty.", parameterName);
        return name;
    }

    private void ValidatePassResource(RenderGraphPass pass, RenderGraphResource resource)
    {
        ValidatePass(pass);
        ValidateResource(resource);
    }

    private void ValidatePass(RenderGraphPass pass)
    {
        ArgumentNullException.ThrowIfNull(pass);
        if (!ReferenceEquals(pass.Owner, this) || !_passes.Contains(pass))
            throw new ArgumentException("Pass does not belong to this render graph.", nameof(pass));
    }

    private void ValidateResource(RenderGraphResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (!ReferenceEquals(resource.Owner, this) || !_resources.Contains(resource))
            throw new ArgumentException("Resource does not belong to this render graph.", nameof(resource));
    }

    private static void ValidateImportedTexture(RenderGraphResource resource, ITexture texture)
    {
        TextureDescription description = resource.Description;
        if (texture.Format != description.Format ||
            texture.Width != (uint)description.Extent.Width ||
            texture.Height != (uint)description.Extent.Height ||
            texture.MipLevels != description.MipLevels ||
            texture.ArrayLayers != description.ArrayLayers ||
            texture.Dimension != description.Dimension)
        {
            throw new ArgumentException(
                $"Texture rebound to '{resource.Name}' must exactly match {description.Extent.Width}x{description.Extent.Height} " +
                $"{description.Format}, {description.MipLevels} mip level(s), and {description.ArrayLayers} layer(s).",
                nameof(texture));
        }
    }

    private void InvalidateCompilation()
    {
        _compilation = null;
        foreach (RenderGraphPass pass in _passes)
            pass.ResetCompilation();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        HashSet<ITexture> disposedTextures = [];
        foreach (List<PooledTexture> pool in _texturePool.Values)
        foreach (PooledTexture texture in pool)
        {
            if (disposedTextures.Add(texture.Texture))
                texture.Texture.Dispose();
        }
        foreach (RenderGraphResource resource in _resources)
        {
            if (resource.Transient)
                resource.Texture = null;
        }
        foreach (IBuffer buffer in _ownedBuffers)
            buffer.Dispose();
        foreach (RenderGraphBufferResource resource in _buffers.Where(resource => resource.Transient))
            resource.Buffer = null;
        _ownedBuffers.Clear();
        _buffers.Clear();
        _accelerationStructures.Clear();
        _memoryResources.Clear();
        _texturePool.Clear();
        _resources.Clear();
        _passes.Clear();
        _compilation = null;
        _disposed = true;
    }

    private sealed class GraphEdges
    {
        public Dictionary<RenderGraphPass, HashSet<RenderGraphPass>> Incoming { get; }
        public Dictionary<RenderGraphPass, HashSet<RenderGraphPass>> Outgoing { get; }

        public GraphEdges(IEnumerable<RenderGraphPass> passes)
        {
            Incoming = passes.ToDictionary(pass => pass, _ => new HashSet<RenderGraphPass>());
            Outgoing = passes.ToDictionary(pass => pass, _ => new HashSet<RenderGraphPass>());
        }

        public void Add(RenderGraphPass source, RenderGraphPass destination)
        {
            if (ReferenceEquals(source, destination))
                return;
            if (Outgoing[source].Add(destination))
                Incoming[destination].Add(source);
        }
    }

    private sealed class PassDeclarationComparer : IComparer<RenderGraphPass>
    {
        public static PassDeclarationComparer Instance { get; } = new();

        public int Compare(RenderGraphPass? left, RenderGraphPass? right)
        {
            if (ReferenceEquals(left, right))
                return 0;
            if (left is null)
                return -1;
            if (right is null)
                return 1;
            return left.DeclarationIndex.CompareTo(right.DeclarationIndex);
        }
    }

    private sealed record CompiledDeclaration(RenderResourceAccess Access, bool Reads, bool Writes);

    private sealed class AllocationPlan(
        int id,
        TextureDescriptionKey key,
        int lastUse,
        bool pinned,
        RenderGraphResource lastResource)
    {
        public int Id { get; } = id;
        public TextureDescriptionKey Key { get; } = key;
        public int LastUse { get; set; } = lastUse;
        public bool Pinned { get; } = pinned;
        public RenderGraphResource LastResource { get; set; } = lastResource;
    }

    private sealed class PooledTexture(ITexture texture)
    {
        public ITexture Texture { get; } = texture;
        public bool InUse { get; set; }
    }

    private readonly record struct TextureDescriptionKey(
        int Width,
        int Height,
        TextureFormat Format,
        TextureUsage Usage,
        uint MipLevels,
        uint ArrayLayers,
        TextureDimension Dimension)
    {
        public static TextureDescriptionKey From(in TextureDescription description) => new(
            description.Extent.Width,
            description.Extent.Height,
            description.Format,
            description.Usage,
            description.MipLevels == 0 ? 1u : description.MipLevels,
            description.ArrayLayers == 0
                ? (description.Dimension == TextureDimension.Cube ? 6u : 1u)
                : description.ArrayLayers,
            description.Dimension);
    }
}
