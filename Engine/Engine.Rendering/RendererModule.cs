using System.Runtime.InteropServices;
using Engine.Core;
using Engine.Mathematics;
using Engine.Platform;
using Engine.RHI;
using Engine.Scene;
using Engine.Shaders;

namespace Engine.Rendering;

public sealed class RendererModule : IUpdatableModule
{
    public const string LogCategory = "Renderer";

    private readonly RhiModule _rhiModule;
    private readonly string _shaderDirectory;
    private readonly List<RenderItem> _items = [];
    private readonly List<Light> _lights = [];
    private readonly List<Material> _materials = [];
    private readonly Dictionary<MeshRendererComponent, RenderItem> _sceneItems = [];
    private readonly Dictionary<MeshRendererComponent, Material> _sceneMaterials = [];
    private readonly Dictionary<MeshRendererComponent, SceneMaterialState> _sceneMaterialStates = [];
    private bool _materialIdle;
    private List<IDescriptorSet>[] _frameSets = [];
    private IEngineContext? _context;
    private IRHI? _rhi;
    private IDevice? _device;
    private ILogger? _log;
    private SlangShaderCompiler? _compiler;
    private RenderGraph? _graph;
    private FrameUniformRing? _uniformRing;
    private IGraphicsPipeline? _shadowPipeline;
    private IGraphicsPipeline? _pbrPipeline;
    private IGraphicsPipeline? _tonemapPipeline;
    private IComputePipeline? _bloomPipeline;
    private IShaderProgram? _shadowProgram;
    private IShaderProgram? _pbrProgram;
    private IShaderProgram? _tonemapProgram;
    private IShaderProgram? _bloomProgram;
    private RenderGraphResource? _shadowDepth;
    private RenderGraphResource? _mainDepth;
    private RenderGraphResource? _hdrTarget;
    private RenderGraphResource? _bloomTarget;
    private RenderGraphResource? _gameDepth;
    private RenderGraphResource? _gameHdrTarget;
    private RenderGraphResource? _gameBloomTarget;
    private RenderGraphResource? _gameTarget;
    private RenderGraphResource? _swapchainTarget;
    private RenderGraphResource? _tonemapTarget;
    private RenderGraphResource? _viewTarget;
    private ISampler? _linearSampler;
    private ISampler? _shadowCompareSampler;
    private MaterialDefaultTextures? _defaultMaterialTextures;
    private LightsData _lightsData;
    private ShaderStages _pbrPushStages = ShaderStages.Vertex;
    private ShaderStages _shadowPushStages = ShaderStages.Vertex;
    private ShaderStages _tonemapPushStages = ShaderStages.Fragment;
    private bool _tonemapProgramHasPush;
    private FrameAllocation _cameraAllocation;
    private FrameAllocation _shadowAllocation;
    private FrameAllocation _lightsAllocation;
    private FrameAllocation _gameCameraAllocation;
    private ShadowData _currentShadowData;
    private int _activeFrameSlot;
    private Size2D _lastTargetExtent;
    private Size2D _viewExtent;
    private Size2D _presentExtent;
    private bool _editorMode;
    private bool _lastEditorMode;
    private bool _initialized;
    private Scene.Scene? _attachedScene;

    public string Name => "Renderer";

    public Camera Camera { get; } = new();

    public Vector4 SkyColor { get; set; } = new(0.35f, 0.55f, 0.85f, 1f);

    public Vector4 EditorBackground { get; set; } = new(0.06f, 0.06f, 0.08f, 1f);

    public float Exposure { get; set; } = 1f;

    public float LodPixelBudget { get; set; } = 320f;

    public float ShadowStrength { get; set; } = 0.85f;

    public int ShadowMapSize { get; set; } = 2048;

    public IReadOnlyList<RenderItem> Items => _items;

    public Camera EditorCamera { get; } = new();

    public PickingHit? PickEditorView(Vector2 normalizedViewport) => ScenePicking.Pick(ScenePicking.CreateRay(EditorCamera, normalizedViewport), _items);

    public IReadOnlyList<Light> Lights => _lights;

    public TextureFormat OverlayFormat => TextureFormat.B8G8R8A8Srgb;

    public ITexture? ViewTexture => _viewTarget is null ? null : TryGetGraphTexture(_viewTarget);

    public TextureFormat ViewFormat => _viewTarget?.Description.Format ?? TextureFormat.B8G8R8A8Srgb;

    public ITexture? GameTexture => _gameTarget is null ? ViewTexture : TryGetGraphTexture(_gameTarget);

    public TextureFormat GameFormat => _gameTarget?.Description.Format ?? ViewFormat;

    public Action<ICommandBuffer>? OverlayRender { get; set; }

    public ITexture? DebugHdrTexture => _hdrTarget is null ? null : TryGetGraphTexture(_hdrTarget);

    public ImageLayout DebugHdrLayout => _hdrTarget?.CurrentLayout ?? ImageLayout.Undefined;

    public ITexture? DebugPresentTexture => _tonemapTarget is null ? null : TryGetGraphTexture(_tonemapTarget);

    public ImageLayout DebugPresentLayout => _tonemapTarget?.CurrentLayout ?? ImageLayout.Undefined;

    public ITexture? DebugShadowTexture => _shadowDepth is null ? null : TryGetGraphTexture(_shadowDepth);

    public ImageLayout DebugShadowLayout => _shadowDepth?.CurrentLayout ?? ImageLayout.Undefined;

    public int DebugRenderItemCount => _sceneItems.Count;

    public ITexture? SwapchainTargetTexture => _swapchainTarget?.Texture;

    public Func<string, Mesh>? MeshResolver { get; set; }

    public Func<string, Mesh?>? MeshRequestResolver { get; set; }

    public Func<string, string, Mesh?>? ModelPartRequestResolver { get; set; }

    public int LoadingMeshCount { get; private set; }

    public int FailedMeshCount { get; private set; }

    public Func<string, bool>? MeshImportFailedResolver { get; set; }

    public long SceneTriangleCount => _sceneItems.Values.Sum(item => (long)item.Mesh.IndexCount / 3);

    public long SceneVertexCount => _sceneItems.Values.Sum(item => (long)item.Mesh.VertexCount);

    public int LodMeshCount => _sceneItems.Values.Count(item => item.Mesh.LodCount > 1);

    public Func<AssetReference, MaterialTextureSlot, ITexture?>? TextureResolver { get; set; }

    public Func<AssetReference, ResolvedMaterial?>? MaterialResolver { get; set; }

    public Func<AssetReference, MaterialTextureSlot, long>? TextureRevisionResolver { get; set; }

    public RendererModule(RhiModule rhiModule, string shaderDirectory)
    {
        _rhiModule = rhiModule;
        _shaderDirectory = ResolveShaderDirectory(shaderDirectory);
    }

    public void EnableEditorMode(Size2D initialViewExtent)
    {
        _editorMode = true;
        _viewExtent = initialViewExtent;
        if (_initialized)
            RebuildTargets();
    }

    public void SetViewExtent(Size2D extent)
    {
        if (extent.Width < 8 || extent.Height < 8 || extent == _viewExtent)
            return;
        _viewExtent = extent;
        if (_initialized)
            RebuildTargets();
    }

    public void AttachScene(Scene.Scene scene)
    {
        _attachedScene = scene ?? throw new ArgumentNullException(nameof(scene));
        foreach (RenderItem item in _sceneItems.Values)
            _items.Remove(item);
        _sceneItems.Clear();
        if (_sceneMaterials.Count > 0)
            _rhi?.WaitIdle();
        foreach (Material material in _sceneMaterials.Values)
        {
            _materials.Remove(material);
            material.Dispose();
        }
        _sceneMaterials.Clear();
        _sceneMaterialStates.Clear();
        _lights.Clear();
    }

    public RenderItem AddMesh(Mesh mesh, Material material, Vector3 position, Quaternion? rotation = null, Vector3? scale = null)
    {
        RenderItem item = new()
        {
            Mesh = mesh,
            Material = material,
            Position = position,
            Rotation = rotation ?? Quaternion.Identity,
            Scale = scale ?? Vector3.One,
        };
        _items.Add(item);
        return item;
    }

    public void AddLight(Light light) => _lights.Add(light);

    public Material CreateMaterial(Vector3? albedo = null, float metallic = 0f, float roughness = 0.5f, Vector2? uvScale = null)
    {
        Material material = new()
        {
            AlbedoColor = albedo ?? new Vector3(0.8f, 0.8f, 0.8f),
            Metallic = metallic,
            Roughness = roughness,
            UvScale = uvScale ?? new Vector2(1f, 1f),
            ParamsBuffer = _device!.CreateBuffer(new BufferDescription
            {
                Size = (ulong)Marshal.SizeOf<MaterialData>(),
                Usage = BufferUsage.Uniform | BufferUsage.TransferDestination,
                Memory = MemoryUsage.CpuToGpu,
            }),
        };
        MaterialTextureSet textures = material.ResolveTextures(_defaultMaterialTextures!);
        material.Set = _pbrProgram!.CreateDescriptorSet(1);
        material.Set.WriteUniformBuffer(0, material.ParamsBuffer);
        material.Set.WriteTexture(1, textures.Albedo);
        material.Set.WriteTexture(2, textures.Metallic);
        material.Set.WriteTexture(3, textures.Roughness);
        material.Set.WriteTexture(4, textures.Normal);
        material.Set.WriteTexture(5, textures.AmbientOcclusion);
        material.Set.WriteSampler(6, _linearSampler!);
        material.Set.WriteTexture(7, textures[MaterialTextureSlot.Emission]);
        _materials.Add(material);
        return material;
    }

    public void Initialize(IEngineContext context)
    {
        _context = context;
        _log = context.Logging.CreateLogger(LogCategory);
        ComponentRegistry.RegisterGlobal(() => new MeshRendererComponent());
        _rhi = _rhiModule.Instance ?? throw new InvalidOperationException("RHI module must be initialized before Renderer.");
        _rhiModule.RenderFrames = false;
        _device = _rhi.Device;
        _compiler = new SlangShaderCompiler(_log);
        _graph = new RenderGraph(_device, _log);
        int framesInFlight = (int)_rhi.FramesInFlight;
        _uniformRing = new FrameUniformRing(_device, framesInFlight, 256 * 1024);
        _frameSets = Enumerable.Range(0, framesInFlight).Select(_ => new List<IDescriptorSet>()).ToArray();
        _linearSampler = _device.CreateSampler(new SamplerDescription { MagFilter = FilterMode.Linear, MinFilter = FilterMode.Linear, MaxAnisotropy = 1 });
        _shadowCompareSampler = _device.CreateSampler(new SamplerDescription
        {
            MagFilter = FilterMode.Linear,
            MinFilter = FilterMode.Linear,
            AddressMode = AddressMode.Clamp,
            CompareEnabled = true,
            CompareOp = CompareOp.Less,
        });
        _defaultMaterialTextures = MaterialDefaultTextures.Create(_device);
        CompilePipelines();
        _initialized = true;
        RebuildTargets();
        _log.Information($"Renderer initialized ({_items.Count} render item(s), {_lights.Count} light(s))");
    }

    public double SceneSyncMilliseconds { get; private set; }
    public double FrameWaitMilliseconds { get; private set; }
    public double UniformUploadMilliseconds { get; private set; }
    public double RenderCpuMilliseconds { get; private set; }
    public int DrawnObjects { get; private set; }
    public int CulledObjects { get; private set; }

    public void Update(in FrameTime frameTime)
    {
        if (!_initialized)
            return;
        DrawnObjects = 0;
        CulledObjects = 0;
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        SyncScene();
        UpdateLights();
        SceneSyncMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        start = System.Diagnostics.Stopwatch.GetTimestamp();
        FrameStatus status = _rhi!.BeginFrame();
        FrameWaitMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        if (status is FrameStatus.Skipped)
            return;
        start = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_rhi.Swapchain.Extent != _presentExtent)
            RebuildTargets();

        _activeFrameSlot = (int)_rhi.CurrentFrameSlot;
        foreach (IDescriptorSet frameSet in _frameSets[_activeFrameSlot])
            frameSet.Dispose();
        _frameSets[_activeFrameSlot].Clear();
        _uniformRing!.BeginFrame(_activeFrameSlot);
        AllocateFrameUniforms();
        _uniformRing.FlushFrame();
        _device!.FlushUploads();
        UniformUploadMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        start = System.Diagnostics.Stopwatch.GetTimestamp();

        _graph!.RebindImportedTexture(
            _swapchainTarget!,
            _rhi.Swapchain.GetRenderTarget(_rhi.CurrentImageIndex),
            ImageLayout.Undefined,
            ImageLayout.PresentSrcKhr);
        UpdateGraphDescriptors();

        ICommandBuffer commandBuffer = _rhi.GetFrameCommandBuffer();
        commandBuffer.Begin();
        _graph.Execute(commandBuffer);
        commandBuffer.End();
        _rhi.EndFrame(commandBuffer);
        RenderCpuMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    private void AllocateFrameUniforms()
    {
        CameraData cameraData = new()
        {
            ViewProjection = (_editorMode ? EditorCamera : Camera).ViewProjection,
            CameraPosition = (_editorMode ? EditorCamera : Camera).Position,
        };
        CameraData gameCameraData = new()
        {
            ViewProjection = Camera.ViewProjection,
            CameraPosition = Camera.Position,
        };
        _currentShadowData = BuildShadowData();
        _cameraAllocation = _uniformRing!.Allocate(CameraData.SizeInBytes);
        _uniformRing.Write(_cameraAllocation, StructuredBytes(cameraData));
        _gameCameraAllocation = _uniformRing.Allocate(CameraData.SizeInBytes);
        _uniformRing.Write(_gameCameraAllocation, StructuredBytes(gameCameraData));
        _shadowAllocation = _uniformRing.Allocate(Marshal.SizeOf<ShadowData>());
        _uniformRing.Write(_shadowAllocation, StructuredBytes(_currentShadowData));
        _lightsAllocation = _uniformRing.Allocate(Marshal.SizeOf<LightsData>());
        _uniformRing.Write(_lightsAllocation, StructuredBytes(_lightsData));
    }

    private void UpdateGraphDescriptors()
    {
    }

    public bool SceneViewVisible { get; set; } = true;
    public bool GameViewVisible { get; set; } = true;

    private void BuildGraphTopology(Size2D sceneExtent)
    {
        RenderGraphPass shadowPass = _graph!.AddGraphicsPass("ShadowPass", ExecuteShadowPass);
        _graph.WritesDepth(shadowPass, _shadowDepth!, clear: true, 1f);

        RenderGraphPass forwardPass = _graph.AddGraphicsPass("ForwardPass", (commandBuffer, _) => ExecuteForwardPass(commandBuffer, sceneExtent));
        _graph.WritesColor(forwardPass, _hdrTarget!, clear: true, SkyColor);
        _graph.WritesDepth(forwardPass, _mainDepth!, clear: true, 1f);
        _graph.ReadsShader(forwardPass, _shadowDepth!, PipelineStages.FragmentShader);

        RenderGraphPass bloomPass = _graph.AddComputePass("Bloom", ExecuteBloomPass);
        _graph.ReadsShader(bloomPass, _hdrTarget!, PipelineStages.ComputeShader);
        _graph.WritesStorage(bloomPass, _bloomTarget!, PipelineStages.ComputeShader);

        RenderGraphResource tonemapOutput = _viewTarget ?? _tonemapTarget!;
        RenderGraphPass tonemapPass = _graph.AddGraphicsPass("TonemapPass", (commandBuffer, _) => ExecuteTonemapPass(commandBuffer, tonemapOutput.Description.Extent));
        _graph.WritesColor(tonemapPass, tonemapOutput, clear: true, SkyColor);
        _graph.ReadsShader(tonemapPass, _hdrTarget!, PipelineStages.FragmentShader);
        _graph.ReadsShader(tonemapPass, _bloomTarget!, PipelineStages.FragmentShader);

        if (_editorMode)
        {
            RenderGraphPass gameForwardPass = _graph.AddGraphicsPass("GameForward", (commandBuffer, _) => ExecuteForwardPass(commandBuffer, sceneExtent, _gameCameraAllocation));
            _graph.WritesColor(gameForwardPass, _gameHdrTarget!, clear: true, SkyColor);
            _graph.WritesDepth(gameForwardPass, _gameDepth!, clear: true, 1f);
            _graph.ReadsShader(gameForwardPass, _shadowDepth!, PipelineStages.FragmentShader);

            RenderGraphPass gameBloomPass = _graph.AddComputePass("GameBloom", (commandBuffer, graph) => ExecuteBloomPass(commandBuffer, graph, _gameHdrTarget!, _gameBloomTarget!));
            _graph.ReadsShader(gameBloomPass, _gameHdrTarget!, PipelineStages.ComputeShader);
            _graph.WritesStorage(gameBloomPass, _gameBloomTarget!, PipelineStages.ComputeShader);

            RenderGraphPass gameTonemapPass = _graph.AddGraphicsPass("GameTonemap", (commandBuffer, _) => ExecuteTonemapPass(commandBuffer, _gameTarget!.Description.Extent, _gameHdrTarget!, _gameBloomTarget!));
            _graph.WritesColor(gameTonemapPass, _gameTarget!, clear: true, SkyColor);
            _graph.ReadsShader(gameTonemapPass, _gameHdrTarget!, PipelineStages.FragmentShader);
            _graph.ReadsShader(gameTonemapPass, _gameBloomTarget!, PipelineStages.FragmentShader);

            gameForwardPass.ShouldExecute = () => GameViewVisible;
            gameBloomPass.ShouldExecute = gameForwardPass.ShouldExecute;
            gameTonemapPass.ShouldExecute = gameForwardPass.ShouldExecute;

            RenderGraphPass overlayPass = _graph.AddGraphicsPass("EditorOverlay", (commandBuffer, _) => OverlayRender?.Invoke(commandBuffer));
            _graph.WritesColor(overlayPass, _tonemapTarget!, clear: true, EditorBackground);
            _graph.ReadsShader(overlayPass, _viewTarget!, PipelineStages.FragmentShader);
            _graph.ReadsShader(overlayPass, _gameTarget!, PipelineStages.FragmentShader);
        }

        RenderGraphResource copySource = _viewTarget is not null ? _tonemapTarget! : tonemapOutput;
        RenderGraphPass presentPass = _graph.AddTransferPass("PresentCopy", (commandBuffer, graph) =>
        {
            commandBuffer.CopyImage(
                graph.GetTexture(copySource),
                ImageLayout.TransferSrcOptimal,
                graph.GetTexture(_swapchainTarget!),
                ImageLayout.TransferDstOptimal);
        });
        shadowPass.ShouldExecute = () => !_editorMode || SceneViewVisible || GameViewVisible;
        forwardPass.ShouldExecute = () => !_editorMode || SceneViewVisible;
        bloomPass.ShouldExecute = forwardPass.ShouldExecute;
        tonemapPass.ShouldExecute = forwardPass.ShouldExecute;
        _graph.ReadsTransfer(presentPass, copySource);
        _graph.WritesTransfer(presentPass, _swapchainTarget!);
    }

    private void ExecuteShadowPass(ICommandBuffer commandBuffer, RenderGraph graph)
    {
        commandBuffer.BindGraphicsPipeline(_shadowPipeline!);
        commandBuffer.SetViewport(new Viewport(0f, 0f, ShadowMapSize, ShadowMapSize, 0f, 1f));
        commandBuffer.SetScissor(new Rect(0, 0, ShadowMapSize, ShadowMapSize));
        foreach (RenderItem renderItem in _sceneItems.Values)
        {
            Matrix4x4 matrix = _currentShadowData.LightViewProjection * renderItem.ModelMatrix;
            if (!FrustumCulling.IsVisible(renderItem.Mesh.BoundsMin, renderItem.Mesh.BoundsMax, matrix))
            {
                CulledObjects++;
                continue;
            }
            DrawnObjects++;
            ShadowModelConstants constants = new() { ModelViewProjection = matrix };
            commandBuffer.PushConstants(StructuredBytes(constants), _shadowPushStages);
            renderItem.Mesh.Draw(commandBuffer);
        }
    }

    private void ExecuteForwardPass(ICommandBuffer commandBuffer, Size2D extent) =>
        ExecuteForwardPass(commandBuffer, extent, _cameraAllocation);

    private void ExecuteForwardPass(ICommandBuffer commandBuffer, Size2D extent, FrameAllocation cameraAllocation)
    {
        commandBuffer.BindGraphicsPipeline(_pbrPipeline!);
        commandBuffer.SetViewport(new Viewport(0f, 0f, extent.Width, extent.Height, 0f, 1f));
        commandBuffer.SetScissor(new Rect(0, 0, extent.Width, extent.Height));

        IDescriptorSet frameSet = _pbrProgram!.CreateDescriptorSet(0);
        frameSet.WriteUniformBuffer(0, _uniformRing!.Buffer, cameraAllocation.Offset, (ulong)CameraData.SizeInBytes);
        frameSet.WriteUniformBuffer(1, _uniformRing.Buffer, _shadowAllocation.Offset, (ulong)Marshal.SizeOf<ShadowData>());
        frameSet.WriteTexture(2, _graph!.GetTexture(_shadowDepth!));
        frameSet.WriteSampler(3, _shadowCompareSampler!);
        frameSet.WriteUniformBuffer(4, _uniformRing.Buffer, _lightsAllocation.Offset, (ulong)Marshal.SizeOf<LightsData>());
        _frameSets[_activeFrameSlot].Add(frameSet);
        commandBuffer.BindDescriptorSet(frameSet, 0);

        Matrix4x4 viewProjection = cameraAllocation == _gameCameraAllocation ? Camera.ViewProjection : (_editorMode ? EditorCamera : Camera).ViewProjection;
        foreach (RenderItem renderItem in _sceneItems.Values)
        {
            if (!FrustumCulling.IsVisible(renderItem.Mesh.BoundsMin, renderItem.Mesh.BoundsMax, viewProjection * renderItem.ModelMatrix))
            {
                CulledObjects++;
                continue;
            }
            DrawnObjects++;
            if (renderItem.Material.ParamsDirty)
            {
                _device!.UploadBufferData(renderItem.Material.ParamsBuffer, StructuredBytes(renderItem.Material.ToData()));
                renderItem.Material.ParamsDirty = false;
            }
            commandBuffer.BindDescriptorSet(renderItem.Material.Set!, 1);
            ModelConstants constants = ModelConstants.FromModel(renderItem.ModelMatrix);
            commandBuffer.PushConstants(StructuredBytes(constants), _pbrPushStages);
            SelectForwardLodMesh(renderItem, cameraAllocation, extent).Draw(commandBuffer);
        }
    }

    private Mesh SelectForwardLodMesh(RenderItem renderItem, FrameAllocation cameraAllocation, Size2D extent)
    {
        if (renderItem.Mesh.LodCount <= 1)
            return renderItem.Mesh;
        bool sceneView = _editorMode && cameraAllocation != _gameCameraAllocation;
        return SelectLodMesh(
            renderItem,
            sceneView ? renderItem.SceneLodState : renderItem.GameLodState,
            sceneView ? EditorCamera : Camera,
            extent);
    }

    private Mesh SelectLodMesh(RenderItem renderItem, MeshLodState state, Camera camera, Size2D extent)
    {
        Mesh mesh = renderItem.Mesh;
        if (mesh.LodCount <= 1)
            return mesh;
        Matrix4x4 modelMatrix = renderItem.ModelMatrix;
        float pixelsPerUnit = MeshLodProjection.PixelsPerUnit(camera, extent, mesh, modelMatrix);
        int level = state.Select(mesh, renderItem.Lod, pixelsPerUnit, 1f, LodPixelBudget);
        return mesh.GetLod(level);
    }

    private void ExecuteBloomPass(ICommandBuffer commandBuffer, RenderGraph graph) =>
        ExecuteBloomPass(commandBuffer, graph, _hdrTarget!, _bloomTarget!);

    private void ExecuteBloomPass(ICommandBuffer commandBuffer, RenderGraph graph, RenderGraphResource source, RenderGraphResource destination)
    {
        IDescriptorSet set = _bloomProgram!.CreateDescriptorSet(0);
        set.WriteTexture(0, graph.GetTexture(source));
        set.WriteStorageImage(1, graph.GetTexture(destination));
        set.WriteSampler(2, _linearSampler!);
        _frameSets[_activeFrameSlot].Add(set);
        commandBuffer.BindComputePipeline(_bloomPipeline!);
        commandBuffer.BindDescriptorSet(set, 0);
        Size2D sourceSize = source.Description.Extent;
        Size2D destinationSize = destination.Description.Extent;
        BloomConstants constants = new()
        {
            SourceWidth = (uint)sourceSize.Width,
            SourceHeight = (uint)sourceSize.Height,
            DestinationWidth = (uint)destinationSize.Width,
            DestinationHeight = (uint)destinationSize.Height,
            Threshold = 1f,
            Intensity = 0.16f,
        };
        commandBuffer.PushConstants(StructuredBytes(constants), ShaderStages.Compute);
        commandBuffer.Dispatch((uint)(destinationSize.Width + 7) / 8, (uint)(destinationSize.Height + 7) / 8);
    }

    private void ExecuteTonemapPass(ICommandBuffer commandBuffer, Size2D extent) =>
        ExecuteTonemapPass(commandBuffer, extent, _hdrTarget!, _bloomTarget!);

    private void ExecuteTonemapPass(
        ICommandBuffer commandBuffer,
        Size2D extent,
        RenderGraphResource hdrSource,
        RenderGraphResource bloomSource)
    {
        IDescriptorSet tonemapSet = _tonemapProgram!.CreateDescriptorSet(0);
        tonemapSet.WriteTexture(0, _graph!.GetTexture(hdrSource));
        tonemapSet.WriteSampler(1, _linearSampler!);
        tonemapSet.WriteTexture(2, _graph.GetTexture(bloomSource));
        _frameSets[_activeFrameSlot].Add(tonemapSet);
        commandBuffer.BindGraphicsPipeline(_tonemapPipeline!);
        commandBuffer.SetViewport(new Viewport(0f, 0f, extent.Width, extent.Height, 0f, 1f));
        commandBuffer.SetScissor(new Rect(0, 0, extent.Width, extent.Height));
        commandBuffer.BindDescriptorSet(tonemapSet, 0);
        if (_tonemapProgramHasPush)
        {
            TonemapConstants constants = new() { Exposure = Exposure };
            commandBuffer.PushConstants(StructuredBytes(constants), _tonemapPushStages);
        }
        commandBuffer.Draw(3);
    }

    private void RebuildTargets()
    {
        IRHI rhi = _rhi!;
        Size2D sceneExtent = _editorMode ? _viewExtent : rhi.Swapchain.Extent;
        if (sceneExtent.Width <= 0 || sceneExtent.Height <= 0)
            return;
        if (_swapchainTarget is not null && sceneExtent == _lastTargetExtent && _editorMode == _lastEditorMode && _presentExtent == rhi.Swapchain.Extent)
            return;

        rhi.WaitIdle();
        _graph!.Dispose();
        _graph = new RenderGraph(_device!, _log!);
        _swapchainTarget = _graph.ImportTexture(
            "SwapchainTarget",
            rhi.Swapchain.GetRenderTarget(0),
            new RenderGraphResourceState(ImageLayout.Undefined, PipelineStages.Top, ResourceAccess.None),
            new RenderGraphResourceState(ImageLayout.PresentSrcKhr, PipelineStages.Bottom, ResourceAccess.None));
        _shadowDepth = _graph.DeclareTransientTexture("ShadowDepth", new TextureDescription
        {
            Extent = new Size2D(ShadowMapSize, ShadowMapSize),
            Format = TextureFormat.D32Float,
        });
        _mainDepth = _graph.DeclareTransientTexture("MainDepth", new TextureDescription
        {
            Extent = sceneExtent,
            Format = TextureFormat.D32Float,
        });
        _hdrTarget = _graph.DeclareTransientTexture("HdrColor", new TextureDescription
        {
            Extent = sceneExtent,
            Format = TextureFormat.R16G16B16A16Float,
        });
        _bloomTarget = _graph.DeclareTransientTexture("BloomColor", new TextureDescription
        {
            Extent = new Size2D(Math.Max(1, sceneExtent.Width / 2), Math.Max(1, sceneExtent.Height / 2)),
            Format = TextureFormat.R16G16B16A16Float,
        });
        _tonemapTarget = _graph.DeclareTransientTexture("PresentColor", new TextureDescription
        {
            Extent = rhi.Swapchain.Extent,
            Format = TextureFormat.B8G8R8A8Srgb,
        });
        _viewTarget = _editorMode
            ? _graph.DeclareTransientTexture("ViewColor", new TextureDescription
            {
                Extent = sceneExtent,
                Format = TextureFormat.B8G8R8A8Srgb,
                Usage = TextureUsage.Sampled,
            })
            : null;
        _gameTarget = _editorMode
            ? _graph.DeclareTransientTexture("GameColor", new TextureDescription
            {
                Extent = sceneExtent,
                Format = TextureFormat.B8G8R8A8Srgb,
                Usage = TextureUsage.Sampled,
            })
            : null;
        if (_editorMode)
        {
            _gameDepth = _graph.DeclareTransientTexture("GameDepth", new TextureDescription
            {
                Extent = sceneExtent,
                Format = TextureFormat.D32Float,
            });
            _gameHdrTarget = _graph.DeclareTransientTexture("GameHdr", new TextureDescription
            {
                Extent = sceneExtent,
                Format = TextureFormat.R16G16B16A16Float,
            });
            _gameBloomTarget = _graph.DeclareTransientTexture("GameBloom", new TextureDescription
            {
                Extent = new Size2D(Math.Max(1, sceneExtent.Width / 2), Math.Max(1, sceneExtent.Height / 2)),
                Format = TextureFormat.R16G16B16A16Float,
            });
        }
        else
        {
            _gameDepth = null;
            _gameHdrTarget = null;
            _gameBloomTarget = null;
        }
        BuildGraphTopology(sceneExtent);
        _graph.Compile();
        if (_viewTarget is not null)
            _graph.Realize(_viewTarget);
        if (_gameTarget is not null)
            _graph.Realize(_gameTarget);
        _lastTargetExtent = sceneExtent;
        _lastEditorMode = _editorMode;
        _presentExtent = rhi.Swapchain.Extent;
    }

    private void CompilePipelines()
    {
        ShaderProgramDescription shadowProgram = _compiler!.CompileProgram(
            Path.Combine(_shaderDirectory, "Shadow.slang"),
            [(ShaderStage.Vertex, "vsMain"), (ShaderStage.Fragment, "psMain")]);
        ShaderProgramDescription pbrProgram = _compiler.CompileProgram(
            Path.Combine(_shaderDirectory, "PBR.slang"),
            [(ShaderStage.Vertex, "vsMain"), (ShaderStage.Fragment, "psMain")]);
        ShaderProgramDescription tonemapProgram = _compiler.CompileProgram(
            Path.Combine(_shaderDirectory, "Tonemap.slang"),
            [(ShaderStage.Vertex, "vsMain"), (ShaderStage.Fragment, "psMain")]);
        ShaderProgramDescription bloomProgram = _compiler.CompileProgram(
            Path.Combine(_shaderDirectory, "Bloom.slang"),
            [(ShaderStage.Compute, "csMain")]);

        _shadowProgram = _device!.CreateShaderProgram(shadowProgram);
        _pbrProgram = _device.CreateShaderProgram(pbrProgram);
        _tonemapProgram = _device.CreateShaderProgram(tonemapProgram);
        _bloomProgram = _device.CreateShaderProgram(bloomProgram with
        {
            Reflection = bloomProgram.Reflection with
            {
                PushConstants = [new Engine.RHI.PushConstantRange
                {
                    Offset = 0,
                    Size = (uint)Marshal.SizeOf<BloomConstants>(),
                    Stages = ShaderStages.Compute,
                }],
            },
        });
        _shadowPipeline = _device.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            Name = "ShadowDepth",
            ShaderProgram = _shadowProgram,
            VertexLayouts = [Vertex.Layout],
            CullMode = CullMode.Front,
            FrontFace = FrontFace.Clockwise,
            DepthTest = true,
            DepthWrite = true,
            DepthBiasEnabled = true,
            DepthBiasConstant = 1.25f,
            DepthBiasSlope = 1.75f,
            DepthAttachmentFormat = TextureFormat.D32Float,
        });
        _pbrPipeline = _device.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            Name = "PBRForward",
            ShaderProgram = _pbrProgram,
            VertexLayouts = [Vertex.Layout],
            CullMode = CullMode.None,
            DepthTest = true,
            DepthWrite = true,
            ColorAttachmentFormats = [TextureFormat.R16G16B16A16Float],
            DepthAttachmentFormat = TextureFormat.D32Float,
        });
        _tonemapProgramHasPush = tonemapProgram.Reflection.PushConstants.Count > 0;
        _tonemapPipeline = _device.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            Name = "Tonemap",
            ShaderProgram = _tonemapProgram,
            VertexLayouts = [],
            CullMode = CullMode.None,
            DepthTest = false,
            DepthWrite = false,
            ColorAttachmentFormats = [TextureFormat.B8G8R8A8Srgb],
        });
        _bloomPipeline = _device.CreateComputePipeline(new ComputePipelineDescription
        {
            Name = "Bloom",
            ShaderProgram = _bloomProgram,
        });
        _shadowPushStages = shadowProgram.Reflection.PushConstants.Count > 0 ? shadowProgram.Reflection.PushConstants[0].Stages : ShaderStages.Vertex;
        _pbrPushStages = pbrProgram.Reflection.PushConstants.Count > 0 ? pbrProgram.Reflection.PushConstants[0].Stages : ShaderStages.Vertex;
        _tonemapPushStages = tonemapProgram.Reflection.PushConstants.Count > 0 ? tonemapProgram.Reflection.PushConstants[0].Stages : ShaderStages.Fragment;
        LogReflection("PBR", pbrProgram.Reflection);
    }

    private void SyncScene()
    {
        _materialIdle = false;
        LoadingMeshCount = 0;
        FailedMeshCount = 0;
        if (_attachedScene is null)
            return;
        HashSet<MeshRendererComponent> activeComponents = [];
        CameraComponent? sceneCamera = null;
        List<Light> sceneLights = [];
        foreach (GameObject gameObject in _attachedScene.EnumerateHierarchy())
        {
            if (!gameObject.IsActiveInHierarchy)
                continue;
            CameraComponent? camera = gameObject.GetComponent<CameraComponent>();
            if (camera is not null && sceneCamera is null)
                sceneCamera = camera;
            LightComponent? light = gameObject.GetComponent<LightComponent>();
            if (light is not null)
            {
                sceneLights.Add(light.Kind == LightKind.Directional
                    ? Light.Directional(gameObject.Transform.Forward, light.Color, light.Intensity)
                    : Light.Point(gameObject.Transform.Position, light.Color, light.Intensity, light.Range));
            }
            MeshRendererComponent? meshRenderer = gameObject.GetComponent<MeshRendererComponent>();
            if (meshRenderer is not null)
                activeComponents.Add(meshRenderer);
        }
        if (sceneCamera is not null)
        {
            Camera.FieldOfViewY = sceneCamera.FieldOfViewY;
            Camera.NearPlane = sceneCamera.NearPlane;
            Camera.FarPlane = sceneCamera.FarPlane;
            Camera.Position = sceneCamera.Transform.Position;
            Camera.Rotation = sceneCamera.Transform.Rotation;
        }
        if (!_editorMode)
        {
            EditorCamera.FieldOfViewY = Camera.FieldOfViewY;
            EditorCamera.NearPlane = Camera.NearPlane;
            EditorCamera.FarPlane = Camera.FarPlane;
            EditorCamera.Position = Camera.Position;
            EditorCamera.Rotation = Camera.Rotation;
            EditorCamera.AspectRatio = Camera.AspectRatio;
        }
        _lights.Clear();
        _lights.AddRange(sceneLights);
        foreach ((MeshRendererComponent component, RenderItem item) in _sceneItems.ToList())
        {
            if (!activeComponents.Contains(component) || component.Mesh is null)
            {
                _items.Remove(item);
                _sceneItems.Remove(component);
                EnsureMaterialIdle();
                if (_sceneMaterials.Remove(component, out Material? removed))
                {
                    _materials.Remove(removed);
                    removed.Dispose();
                }
                _sceneMaterialStates.Remove(component);
            }
        }
        foreach (MeshRendererComponent component in activeComponents)
        {
            bool created = !_sceneItems.TryGetValue(component, out RenderItem? item);
            if (created)
            {
                Mesh? mesh = ResolveMesh(component);
                if (mesh is null)
                {
                    if (MeshImportFailedResolver?.Invoke(component.ResourceKey) == true)
                        FailedMeshCount++;
                    else
                        LoadingMeshCount++;
                    continue;
                }
                Material material = CreateMaterial();
                _sceneMaterials[component] = material;
                item = AddMesh(mesh, material, component.Transform.Position);
                item.BoundTransform = component.Transform;
                _sceneItems[component] = item;
            }
            item!.Lod = new MeshLodSettings(component.LodMode, component.ForcedLodLevel, component.LodBias);
            SyncMaterial(component, item!.Material, created);
        }
    }

    private void EnsureMaterialIdle()
    {
        if (_materialIdle)
            return;
        _rhi?.WaitIdle();
        _materialIdle = true;
    }

    private void SyncMaterial(MeshRendererComponent component, Material material, bool created)
    {
        ResolvedMaterial? resolved = component.MaterialAsset.IsEmpty ? null : MaterialResolver?.Invoke(component.MaterialAsset);
        MaterialProperties properties = component.MaterialAsset.IsEmpty
            ? MaterialProperties.FromComponent(component)
            : resolved?.Properties ?? new MaterialProperties();
        MaterialTextureReferences references = MaterialTextureReferences.From(properties);
        TextureRevisions revisions = new(
            TextureRevision(references.Albedo, MaterialTextureSlot.Albedo),
            TextureRevision(references.Metallic, MaterialTextureSlot.Metallic),
            TextureRevision(references.Roughness, MaterialTextureSlot.Roughness),
            TextureRevision(references.Normal, MaterialTextureSlot.Normal),
            TextureRevision(references.AmbientOcclusion, MaterialTextureSlot.AmbientOcclusion),
            TextureRevision(references.Emission, MaterialTextureSlot.Emission));
        SceneMaterialState state = new(properties, resolved?.Revision ?? 0, revisions);
        _sceneMaterialStates.TryGetValue(component, out SceneMaterialState? previous);
        if (previous == state)
            return;
        bool texturesChanged = previous is null || MaterialTextureReferences.From(previous.Properties) != references
            || previous.TextureRevisions != revisions;
        if (!created)
            EnsureMaterialIdle();
        MaterialData oldData = material.ToData();
        material.AlbedoColor = properties.AlbedoColor;
        material.Metallic = properties.Metallic;
        material.Roughness = properties.Roughness;
        material.EmissiveIntensity = properties.EmissiveIntensity;
        material.EmissionColor = properties.EmissionColor;
        material.LegacyEmission = properties.LegacyEmission;
        material.UvScale = properties.UvScale;
        material.UvOffset = properties.UvOffset;
        material.NormalScale = properties.NormalScale;
        material.AoStrength = properties.AoStrength;
        material.MetallicChannel = properties.MetallicChannel;
        material.RoughnessChannel = properties.RoughnessChannel;
        material.AmbientOcclusionChannel = properties.AmbientOcclusionChannel;
        material.ParamsDirty |= !oldData.Equals(material.ToData());
        if (texturesChanged)
            ApplyMaterialTextures(references, material);
        _sceneMaterialStates[component] = state;
    }

    private long TextureRevision(AssetReference reference, MaterialTextureSlot slot)
    {
        try
        {
            return reference.IsEmpty ? 0 : TextureRevisionResolver?.Invoke(reference, slot) ?? 0;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
        {
            return -1;
        }
    }

    private void ApplyMaterialTextures(MaterialTextureReferences references, Material material)
    {
        material.AlbedoTexture = ResolveTexture(references.Albedo, MaterialTextureSlot.Albedo);
        material.MetallicTexture = ResolveTexture(references.Metallic, MaterialTextureSlot.Metallic);
        material.RoughnessTexture = ResolveTexture(references.Roughness, MaterialTextureSlot.Roughness);
        material.NormalTexture = ResolveTexture(references.Normal, MaterialTextureSlot.Normal);
        material.AmbientOcclusionTexture = ResolveTexture(references.AmbientOcclusion, MaterialTextureSlot.AmbientOcclusion);
        material.EmissionTexture = ResolveTexture(references.Emission, MaterialTextureSlot.Emission);
        MaterialTextureSet textures = material.ResolveTextures(_defaultMaterialTextures!);
        material.Set!.WriteTexture(1, textures.Albedo);
        material.Set.WriteTexture(2, textures.Metallic);
        material.Set.WriteTexture(3, textures.Roughness);
        material.Set.WriteTexture(4, textures.Normal);
        material.Set.WriteTexture(5, textures.AmbientOcclusion);
        material.Set.WriteTexture(7, textures[MaterialTextureSlot.Emission]);
    }

    private ITexture? ResolveTexture(AssetReference reference, MaterialTextureSlot slot) =>
        reference.IsEmpty || TextureResolver is null ? null : TextureResolver(reference, slot);

    private Mesh? ResolveMesh(MeshRendererComponent component)
    {
        if (component.Mesh is not null)
            return component.Mesh;
        if (!string.IsNullOrEmpty(component.PartId) && ModelPartRequestResolver is not null)
        {
            component.Mesh = ModelPartRequestResolver(component.ResourceKey, component.PartId);
            return component.Mesh;
        }
        if (MeshRequestResolver is not null)
        {
            component.Mesh = MeshRequestResolver(component.ResourceKey);
            return component.Mesh;
        }
        if (MeshResolver is null)
            throw new InvalidOperationException("MeshResolver must be configured to resolve MeshRendererComponent resources.");
        Mesh mesh = MeshResolver(component.ResourceKey) ?? throw new InvalidOperationException($"Mesh '{component.ResourceKey}' could not be resolved.");
        component.Mesh = mesh;
        return mesh;
    }

    private void UpdateLights()
    {
        _lightsData = default;
        int count = Math.Min(_lights.Count, LightsData.MaxLights);
        for (int index = 0; index < count; index++)
        {
            Light light = _lights[index];
            _lightsData.Lights[index] = new LightData
            {
                DirectionOrPosition = light.DirectionOrPosition,
                Type = light.Type == LightType.Directional ? LightData.Directional : LightData.Point,
                Color = light.Color,
                Intensity = light.Intensity,
                Range = light.Range,
            };
        }
        _lightsData.Count = (uint)count;
    }

    private ShadowData BuildShadowData()
    {
        Light? shadowLight = null;
        int shadowLightIndex = -1;
        for (int index = 0; index < _lights.Count; index++)
        {
            if (_lights[index].Type != LightType.Directional)
                continue;
            shadowLight = _lights[index];
            shadowLightIndex = index;
            break;
        }
        Matrix4x4 lightViewProjection = Matrix4x4.Identity;
        if (shadowLight is not null)
        {
            Vector3 direction = shadowLight.DirectionOrPosition.Normalized;
            Vector3 eye = Camera.Position - direction * 20f;
            Vector3 up = MathF.Abs(Vector3.Dot(direction, Vector3.Up)) > 0.98f ? Vector3.Right : Vector3.Up;
            Matrix4x4 view = Matrix4x4.CreateLookAt(eye, Camera.Position, up);
            float halfHeight = MathF.Tan(Camera.FieldOfViewY * 0.5f) * Math.Min(Camera.FarPlane, 40f);
            float extent = MathF.Max(12f, halfHeight * MathF.Max(1f, Camera.AspectRatio));
            float texelSize = extent * 2f / ShadowMapSize;
            Vector3 lightSpaceCenter = Vector3.TransformPoint(Camera.Position, view);
            lightSpaceCenter.X = MathF.Floor(lightSpaceCenter.X / texelSize) * texelSize;
            lightSpaceCenter.Y = MathF.Floor(lightSpaceCenter.Y / texelSize) * texelSize;
            Matrix4x4 projection = Matrix4x4.CreateOrthographic(extent * 2f, extent * 2f, 0.1f, 80f);
            projection.M22 = -projection.M22;
            lightViewProjection = projection * view;
        }
        return new ShadowData
        {
            LightViewProjection = lightViewProjection,
            TexelSize = 1f / ShadowMapSize,
            Strength = shadowLight is null ? 0f : ShadowStrength,
            LightIndex = shadowLightIndex,
        };
    }

    private ITexture? TryGetGraphTexture(RenderGraphResource resource)
    {
        try
        {
            return _graph?.GetTexture(resource);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private void LogReflection(string name, PipelineReflection reflection)
    {
        foreach (DescriptorSetLayout set in reflection.DescriptorSets)
        foreach (DescriptorBinding binding in set.Bindings)
            _log!.Debug($"{name} set {set.SetIndex} binding {binding.Binding}: {binding.Type} ({binding.Stages})");
    }

    private readonly record struct MaterialTextureReferences(
        AssetReference Albedo,
        AssetReference Metallic,
        AssetReference Roughness,
        AssetReference Normal,
        AssetReference AmbientOcclusion,
        AssetReference Emission)
    {
        public static MaterialTextureReferences From(MaterialProperties component) => new(
            component.AlbedoTexture,
            component.MetallicTexture,
            component.RoughnessTexture,
            component.NormalTexture,
            component.AmbientOcclusionTexture,
            component.EmissionTexture);
    }

    private sealed record SceneMaterialState(MaterialProperties Properties, long Revision, TextureRevisions TextureRevisions);

    private readonly record struct TextureRevisions(long Albedo, long Metallic, long Roughness, long Normal, long AmbientOcclusion, long Emission);

    private static string ResolveShaderDirectory(string requested)
    {
        List<string> candidates = [requested, Path.Combine(AppContext.BaseDirectory, "Shaders")];
        string? current = Directory.GetCurrentDirectory();
        for (int index = 0; index < 4 && current is not null; index++)
        {
            candidates.Add(Path.Combine(current, "Shaders"));
            current = Path.GetDirectoryName(current.TrimEnd(Path.DirectorySeparatorChar));
        }
        foreach (string candidate in candidates)
        {
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "PBR.slang")))
                return candidate;
        }
        throw new DirectoryNotFoundException("Shader directory not found. Searched: " + string.Join(", ", candidates.Select(Path.GetFullPath)));
    }

    private static unsafe byte[] StructuredBytes<T>(T value) where T : unmanaged
    {
        byte[] bytes = new byte[Marshal.SizeOf<T>()];
        T* source = &value;
        fixed (byte* destination = bytes)
            Buffer.MemoryCopy(source, destination, bytes.Length, bytes.Length);
        return bytes;
    }

    public void Shutdown()
    {
        _rhi?.WaitIdle();
        foreach (List<IDescriptorSet> frameSets in _frameSets)
        {
            foreach (IDescriptorSet frameSet in frameSets)
                frameSet.Dispose();
            frameSets.Clear();
        }
        _frameSets = [];
        _sceneMaterials.Clear();
        _sceneMaterialStates.Clear();
        _sceneItems.Clear();
        _attachedScene = null;
        foreach (Material material in _materials)
            material.Dispose();
        _materials.Clear();
        _defaultMaterialTextures?.Dispose();
        _shadowCompareSampler?.Dispose();
        _linearSampler?.Dispose();
        _uniformRing?.Dispose();
        _graph?.Dispose();
        _shadowPipeline?.Dispose();
        _pbrPipeline?.Dispose();
        _tonemapPipeline?.Dispose();
        _bloomPipeline?.Dispose();
        _shadowProgram?.Dispose();
        _pbrProgram?.Dispose();
        _tonemapProgram?.Dispose();
        _bloomProgram?.Dispose();
        _initialized = false;
        _log?.Information("Renderer shut down");
    }
}
