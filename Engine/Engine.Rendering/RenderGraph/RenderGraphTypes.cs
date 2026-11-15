using Engine.Mathematics;
using Engine.RHI;

namespace Engine.Rendering;

public enum RenderPassKind
{
    Graphics,
    Compute,
    Transfer,
}

public enum ResourceUsageKind
{
    ColorAttachmentWrite = 0,
    ColorAttachment = ColorAttachmentWrite,
    DepthAttachmentWrite = 1,
    DepthAttachment = DepthAttachmentWrite,
    ShaderRead = 2,
    Sampled = ShaderRead,
    SampledRead = ShaderRead,
    TransferWrite = 3,
    TransferDestination = TransferWrite,
    StorageRead = 4,
    StorageWrite = 5,
    StorageReadWrite = 6,
    TransferRead = 7,
    TransferSource = TransferRead,
    Present = 8,
    ColorAttachmentRead = 9,
    DepthAttachmentRead = 10,
}

public readonly record struct RenderGraphResourceState(
    ImageLayout Layout,
    PipelineStages Stages,
    ResourceAccess Access)
{
    public static RenderGraphResourceState Undefined => new(ImageLayout.Undefined, PipelineStages.Top, ResourceAccess.None);

    public static RenderGraphResourceState FromLayout(ImageLayout layout) => layout switch
    {
        ImageLayout.Undefined => Undefined,
        ImageLayout.ColorAttachmentOptimal => new(layout, PipelineStages.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite),
        ImageLayout.DepthAttachmentOptimal => new(layout, PipelineStages.EarlyDepth | PipelineStages.LateDepth, ResourceAccess.DepthRead | ResourceAccess.DepthWrite),
        ImageLayout.DepthStencilReadOnlyOptimal or ImageLayout.DepthReadOnly => new(layout, PipelineStages.FragmentShader, ResourceAccess.ShaderRead),
        ImageLayout.ShaderReadOnlyOptimal => new(layout, PipelineStages.FragmentShader, ResourceAccess.ShaderRead),
        ImageLayout.TransferSrcOptimal => new(layout, PipelineStages.Transfer, ResourceAccess.TransferRead),
        ImageLayout.TransferDstOptimal => new(layout, PipelineStages.Transfer, ResourceAccess.TransferWrite),
        ImageLayout.PresentSrcKhr => new(layout, PipelineStages.Bottom, ResourceAccess.None),
        ImageLayout.General => new(layout, PipelineStages.AllCommands, ResourceAccess.MemoryRead | ResourceAccess.MemoryWrite),
        _ => new(layout, PipelineStages.AllCommands, ResourceAccess.MemoryRead | ResourceAccess.MemoryWrite),
    };
}

public sealed class RenderGraphResource
{
    private RenderGraphResourceState _currentState;

    public string Name { get; }
    public TextureDescription Description { get; }
    public TextureDescription CompiledDescription { get; internal set; }
    public TextureDescription ResolvedDescription => CompiledDescription;
    public bool IsDepth { get; }
    public ITexture? Texture { get; internal set; }
    public bool Transient { get; internal set; } = true;
    public bool IsImported => !Transient;
    public int FirstUse { get; internal set; } = -1;
    public int LastUse { get; internal set; } = -1;
    public int FirstUsedPass => FirstUse;
    public int LastUsedPass { get; internal set; } = -1;
    public bool UsedThisFrame { get; internal set; }
    public int PhysicalAllocationId { get; internal set; } = -1;
    public bool IsAliased { get; internal set; }
    public RenderGraphResourceState InitialState { get; internal set; }
    public RenderGraphResourceState? FinalState { get; internal set; }

    public RenderGraphResourceState CurrentState
    {
        get => _currentState;
        internal set => _currentState = value;
    }

    public ImageLayout InitialLayout => InitialState.Layout;
    public ImageLayout? FinalLayout => FinalState?.Layout;

    public ImageLayout CurrentLayout
    {
        get => _currentState.Layout;
        internal set => _currentState = RenderGraphResourceState.FromLayout(value);
    }

    internal RenderGraph Owner { get; }
    internal int DeclarationIndex { get; }
    internal bool IsPinned { get; set; }
    internal TextureDescription PhysicalDescription { get; set; }
    internal RenderGraphResource? AliasPredecessor { get; set; }

    internal RenderGraphResource(
        RenderGraph owner,
        int declarationIndex,
        string name,
        TextureDescription description,
        bool isDepth,
        bool transient,
        RenderGraphResourceState initialState,
        RenderGraphResourceState? finalState)
    {
        Owner = owner;
        DeclarationIndex = declarationIndex;
        Name = name;
        Description = description;
        CompiledDescription = description;
        PhysicalDescription = description;
        IsDepth = isDepth;
        Transient = transient;
        InitialState = initialState;
        FinalState = finalState;
        _currentState = initialState;
    }
}

public readonly record struct RenderResourceAccess
{
    public required RenderGraphResource Resource { get; init; }
    public required ResourceUsageKind Usage { get; init; }
    public PipelineStages Stages { get; init; }
    public PipelineStages PipelineStages => Stages;
    public bool ClearOnLoad { get; init; }
    public Vector4 ClearColor { get; init; }
    public float ClearDepth { get; init; }
}

public sealed class RenderGraphCompiledAccess
{
    public RenderResourceAccess Declaration { get; }
    public RenderGraphResource Resource => Declaration.Resource;
    public ResourceUsageKind Usage => Declaration.Usage;
    public RenderGraphResourceState State { get; }
    public bool Reads { get; }
    public bool Writes { get; }

    internal RenderGraphCompiledAccess(
        in RenderResourceAccess declaration,
        in RenderGraphResourceState state,
        bool reads,
        bool writes)
    {
        Declaration = declaration;
        State = state;
        Reads = reads;
        Writes = writes;
    }
}

public sealed class RenderGraphPass
{
    private IReadOnlyList<RenderGraphPass> _dependencies = [];
    private IReadOnlyList<RenderGraphBarrier> _barriersBefore = [];
    private IReadOnlyList<RenderGraphBarrier> _barriersAfter = [];
    private IReadOnlyList<RenderGraphCompiledAccess> _compiledAccesses = [];

    public string Name { get; }
    public Func<bool>? ShouldExecute { get; set; }
    public RenderPassKind Kind { get; }
    public int DeclarationIndex { get; }
    public int ExecutionIndex { get; internal set; } = -1;
    public IReadOnlyList<RenderResourceAccess> Accesses => MutableAccesses;
    public IReadOnlyList<RenderGraphPass> Dependencies => _dependencies;
    public IReadOnlyList<RenderGraphBarrier> BarriersBefore => _barriersBefore;
    public IReadOnlyList<RenderGraphBarrier> BarriersAfter => _barriersAfter;
    public IReadOnlyList<RenderGraphCompiledAccess> CompiledAccesses => _compiledAccesses;

    internal RenderGraph Owner { get; }
    internal List<RenderResourceAccess> MutableAccesses { get; } = [];
    internal HashSet<RenderGraphPass> ExplicitDependencies { get; } = [];
    internal Action<ICommandBuffer, RenderGraph>? ExecuteAction { get; }

    internal RenderGraphPass(
        RenderGraph owner,
        int declarationIndex,
        string name,
        RenderPassKind kind,
        Action<ICommandBuffer, RenderGraph> executeAction)
    {
        Owner = owner;
        DeclarationIndex = declarationIndex;
        Name = name;
        Kind = kind;
        ExecuteAction = executeAction;
    }

    internal void SetCompilation(
        IReadOnlyList<RenderGraphPass> dependencies,
        IReadOnlyList<RenderGraphBarrier> barriersBefore,
        IReadOnlyList<RenderGraphBarrier> barriersAfter,
        IReadOnlyList<RenderGraphCompiledAccess> compiledAccesses)
    {
        _dependencies = dependencies;
        _barriersBefore = barriersBefore;
        _barriersAfter = barriersAfter;
        _compiledAccesses = compiledAccesses;
    }

    internal void ResetCompilation()
    {
        ExecutionIndex = -1;
        _dependencies = [];
        _barriersBefore = [];
        _barriersAfter = [];
        _compiledAccesses = [];
    }
}

public sealed class RenderGraphBarrier
{
    public RenderGraphResource Resource { get; }
    public RenderGraphResource? SourceResource { get; }
    public RenderGraphPass? Pass { get; }
    public RenderGraphResourceState SourceState { get; }
    public RenderGraphResourceState DestinationState { get; }
    public bool IsFinalTransition { get; }
    public bool IsAliasingBarrier => SourceResource is not null && SourceResource != Resource;
    public ImageLayout OldLayout => SourceState.Layout;
    public ImageLayout NewLayout => DestinationState.Layout;
    public PipelineStages SourceStages => SourceState.Stages;
    public PipelineStages DestinationStages => DestinationState.Stages;
    public ResourceAccess SourceAccess => SourceState.Access;
    public ResourceAccess DestinationAccess => DestinationState.Access;

    internal RenderGraphBarrier(
        RenderGraphResource resource,
        RenderGraphResource? sourceResource,
        RenderGraphPass? pass,
        in RenderGraphResourceState sourceState,
        in RenderGraphResourceState destinationState,
        bool isFinalTransition)
    {
        Resource = resource;
        SourceResource = sourceResource;
        Pass = pass;
        SourceState = sourceState;
        DestinationState = destinationState;
        IsFinalTransition = isFinalTransition;
    }
}

public sealed class RenderGraphCompilation
{
    public IReadOnlyList<RenderGraphPass> OrderedPasses { get; }
    public IReadOnlyList<RenderGraphPass> Passes => OrderedPasses;
    public IReadOnlyList<RenderGraphResource> Resources { get; }
    public IReadOnlyList<RenderGraphBarrier> Barriers { get; }
    public IReadOnlyList<RenderGraphBarrier> TailBarriers { get; }
    public IReadOnlyList<RenderGraphBarrier> FinalBarriers => TailBarriers;

    internal RenderGraphCompilation(
        IReadOnlyList<RenderGraphPass> orderedPasses,
        IReadOnlyList<RenderGraphResource> resources,
        IReadOnlyList<RenderGraphBarrier> barriers,
        IReadOnlyList<RenderGraphBarrier> tailBarriers)
    {
        OrderedPasses = orderedPasses;
        Resources = resources;
        Barriers = barriers;
        TailBarriers = tailBarriers;
    }
}

public sealed class RenderGraphValidationException : InvalidOperationException
{
    public RenderGraphValidationException(string message)
        : base(message)
    {
    }
}
