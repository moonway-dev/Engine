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
    private IReadOnlyList<Light> _activeLights = [];
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
    private IShaderProgram? _debugLinesProgram;
    private IGraphicsPipeline? _debugLinesPipeline;
    private IBuffer? _debugLinesBuffer;
    private ulong _debugLinesBufferCapacity;
    private uint _debugLinesVertexCount;
    private IShaderProgram? _skyProgram;
    private IGraphicsPipeline? _skyPipeline;
    private SkyboxMode _skyboxMode = SkyboxMode.SolidColor;
    private Vector3 _sunDirection = new(0.35f, 0.45f, -0.55f);
    private Vector3 _sceneSkyCameraPosition;
    private Vector3 _gameSkyCameraPosition;
    private IShaderProgram? _cloudsProgram;
    private IGraphicsPipeline? _cloudsPipeline;
    private ITexture? _cloudShapeTexture;
    private ITexture? _cloudDetailTexture;
    private ITexture? _cloudWeatherTexture;
    private ISampler? _cloudsSampler;
    private float _cloudWindPhase;
    private float _cloudJitterSeed;
    private const int CloudShapeSize = 128;
    private const int CloudDetailSize = 32;
    private const int CloudWeatherSize = 256;
    public string? CloudsCacheDirectory { get; set; }
    public bool CloudsEnabled { get; set; }
    public float CloudCoverage { get; set; } = 0.30f;
    public float CloudDensity { get; set; } = 0.55f;
    public float CloudWindSpeed { get; set; } = 0.3f;
    public float CloudBottom { get; set; } = 1.8f;
    public float CloudTop { get; set; } = 4.6f;
    public Action? ColliderGizmoSource { get; set; }
    private IComputePipeline? _bloomPipeline;
    private IComputePipeline? _temporalPipeline;
    private IComputePipeline? _rtGiTemporalPipeline;
    private IComputePipeline? _ssgiPipeline;
    private IComputePipeline? _giCompositePipeline;
    private IShaderProgram? _shadowProgram;
    private IShaderProgram? _pbrProgram;
    private IShaderProgram? _tonemapProgram;
    private IShaderProgram? _bloomProgram;
    private IShaderProgram? _temporalProgram;
    private IShaderProgram? _rtGiTemporalProgram;
    private IShaderProgram? _ssgiProgram;
    private IShaderProgram? _giCompositeProgram;
    private ITexture? _bloomFallbackTexture;
    private ImageBasedLightingResources? _iblResources;
    private RenderGraphResource? _shadowDepth;
    private RenderGraphResource? _gameShadowDepth;
    private RenderGraphResource? _mainDepth;
    private RenderGraphResource? _hdrTarget;
    private RenderGraphResource? _motionTarget;
    private RenderGraphResource? _historyRead;
    private RenderGraphResource? _historyWrite;
    private RenderGraphResource? _sceneReflectionHistoryRead;
    private RenderGraphResource? _sceneReflectionHistoryWrite;
    private RenderGraphResource? _gameReflectionHistoryRead;
    private RenderGraphResource? _gameReflectionHistoryWrite;
    private RenderGraphResource? _giTarget;
    private RenderGraphResource? _rtGiFiltered;
    private RenderGraphResource? _rtGiFilterScratch;
    private RenderGraphResource? _gameRtGiFiltered;
    private RenderGraphResource? _gameRtGiFilterScratch;
    private RenderGraphResource? _compositeTarget;
    private RenderGraphResource? _sceneGiHistoryRead;
    private RenderGraphResource? _sceneGiHistoryWrite;
    private RenderGraphResource? _bloomTarget;
    private RenderGraphResource? _bloomFallback;
    private RenderGraphResource? _gameDepth;
    private RenderGraphResource? _gameHdrTarget;
    private RenderGraphResource? _gameMotionTarget;
    private RenderGraphResource? _gameHistoryRead;
    private RenderGraphResource? _gameHistoryWrite;
    private RenderGraphResource? _gameGiTarget;
    private RenderGraphResource? _gameCompositeTarget;
    private RenderGraphResource? _gameGiHistoryRead;
    private RenderGraphResource? _gameGiHistoryWrite;
    private RenderGraphResource? _gameBloomTarget;
    private RenderGraphResource? _gameTarget;
    private RenderGraphResource? _swapchainTarget;
    private RenderGraphResource? _tonemapTarget;
    private RenderGraphResource? _viewTarget;
    private RenderGraphPass? _forwardPass;
    private RenderGraphPass? _tonemapPass;
    private RenderGraphPass? _gameForwardPass;
    private RenderGraphPass? _gameTonemapPass;
    private RenderGraphPass? _ssgiPass;
    private RenderGraphPass? _giTemporalPass;
    private RenderGraphPass? _giCompositePass;
    private RenderGraphPass? _gameSsgiPass;
    private RenderGraphPass? _gameGiTemporalPass;
    private RenderGraphPass? _gameGiCompositePass;
    private Vector4 _skyColor = new(0.35f, 0.55f, 0.85f, 1f);
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
    private FrameAllocation _gameShadowAllocation;
    private FrameAllocation _lightsAllocation;
    private FrameAllocation _gameCameraAllocation;
    private FrameAllocation _ssgiSceneAllocation;
    private FrameAllocation _ssgiGameAllocation;
    private RayTracingScene? _sceneRayTracingScene;
    private RayTracingScene? _gameRayTracingScene;
    private RayTracingFrameSlotReadiness? _sceneRtReadiness;
    private RayTracingFrameSlotReadiness? _gameRtReadiness;
    private readonly List<RayTracingBuildGraphSlot> _rtBuildGraphSlots = [];
    private bool _sceneRtCanTraceThisFrame;
    private bool _gameRtCanTraceThisFrame;
    private RenderGraphResource? _rtNormalRoughness;
    private RenderGraphResource? _rtDepth;
    private RenderGraphResource? _rtVisibility;
    private RenderGraphResource? _rtVisibilityValidity;
    private RenderGraphResource? _rtResolvedVisibility;
    private RenderGraphResource? _rtReflectionValidity;
    private RenderGraphResource? _rtGiValidity;
    private RenderGraphResource? _gameRtNormalRoughness;
    private RenderGraphResource? _gameRtDepth;
    private RenderGraphResource? _gameRtVisibility;
    private RenderGraphResource? _gameRtVisibilityValidity;
    private RenderGraphResource? _gameRtResolvedVisibility;
    private RenderGraphResource? _gameRtReflectionValidity;
    private RenderGraphResource? _gameRtGiValidity;
    private RenderGraphBufferResource? _rtSbt;
    private RenderGraphPass? _rtScenePrepassPass;
    private RenderGraphPass? _rtSceneTracePass;
    private RenderGraphPass? _rtGamePrepassPass;
    private RenderGraphPass? _rtGameTracePass;
    private FrameAllocation _rtSceneViewAllocation;
    private FrameAllocation _rtGameViewAllocation;
    private FrameAllocation _rtSceneControlAllocation;
    private FrameAllocation _rtGameControlAllocation;
    private IShaderProgram? _rtPrepassProgram;
    private IShaderProgram? _rtShadowProgram;
    private IShaderProgram? _pbrRtProgram;
    private IGraphicsPipeline? _rtPrepassPipeline;
    private IGraphicsPipeline? _pbrRtPipeline;
    private IRayTracingPipeline? _rtShadowPipeline;
    private ShaderBindingTable? _rtShadowSbt;
    private ShaderStages _rtPrepassPushStages = ShaderStages.Vertex;
    private IShaderProgram? _rtReflectionProgram;
    private IShaderProgram? _rtFilterProgram;
    private IShaderProgram? _rtCompositeProgram;
    private IShaderProgram? _rtGiProgram;
    private IRayTracingPipeline? _rtReflectionPipeline;
    private ShaderBindingTable? _rtReflectionSbt;
    private IRayTracingPipeline? _rtGiPipeline;
    private ShaderBindingTable? _rtGiSbt;
    private IComputePipeline? _rtFilterPipeline;
    private IComputePipeline? _rtCompositePipeline;
    private RenderGraphResource? _rtRawReflection;
    private RenderGraphResource? _rtFilteredReflection;
    private RenderGraphResource? _rtSpecular;
    private RenderGraphResource? _rtSpecularResponse;
    private RenderGraphResource? _rtCompositeTarget;
    private RenderGraphResource? _gameRtRawReflection;
    private RenderGraphResource? _gameRtFilteredReflection;
    private RenderGraphResource? _gameRtSpecular;
    private RenderGraphResource? _gameRtSpecularResponse;
    private RenderGraphResource? _gameRtCompositeTarget;
    private RenderGraphBufferResource? _rtReflectionSbtResource;
    private RenderGraphBufferResource? _rtGiSbtResource;
    private RenderGraphPass? _rtSceneReflectionTracePass;
    private RenderGraphPass? _rtSceneFilterPass;
    private RenderGraphPass? _rtSceneCompositePass;
    private RenderGraphPass? _rtGameReflectionTracePass;
    private RenderGraphPass? _rtGameFilterPass;
    private RenderGraphPass? _rtGameCompositePass;
    private RenderGraphPass? _rtSceneGiPass;
    private RenderGraphPass? _rtGameGiPass;
    private ShaderStages _rtFilterPushStages = ShaderStages.Compute;
    private ShaderStages _rtCompositePushStages = ShaderStages.Compute;
    private int _rtPointLightIndex = -1;
    private bool _rtMasterEnabled = true;
    private bool _rtReflectionsEnabled = true;
    private GlobalIlluminationMode _giMode = GlobalIlluminationMode.ScreenSpace;
    private ShadowMode _shadowMode = ShadowMode.Cascaded;
    private int _rtSamples = 2;
    private float _rtResolutionScale = 1f;
    private bool _rtAdaptiveQualityEnabled = true;
    private RayTracingQualityPreset _rtShadowPreset = RayTracingQualityPreset.High;
    private RayTracingQualityPreset _rtReflectionPreset = RayTracingQualityPreset.High;
    private RayTracingQualityPreset _rtGiPreset = RayTracingQualityPreset.High;
    private readonly RayTracingQualityController _rtQualityController = new();
    private readonly float[] _rtTimingMilliseconds = new float[6];
    private float _rtRayBias = 0.002f;
    private float _rtMaxDistance = 200f;
    private float _rtRoughnessLimit = 0.3f;
    private float _rtReflectionStrength = 0.6f;
    private int _rtReflectionTextureArraySize;
    private readonly TemporalHistory _sceneHistory = new();
    private readonly TemporalHistory _gameHistory = new();
    private readonly TemporalHistory _sceneGiHistory = new();
    private readonly TemporalHistory _gameGiHistory = new();
    private readonly TemporalHistory _sceneReflectionHistory = new();
    private readonly TemporalHistory _gameReflectionHistory = new();
    private readonly Dictionary<RenderItem, Matrix4x4> _scenePreviousModels = [];
    private readonly Dictionary<RenderItem, Matrix4x4> _gamePreviousModels = [];
    private readonly List<RayTracingDrawItem> _scenePreparedDrawItems = [];
    private readonly List<RayTracingDrawItem> _gamePreparedDrawItems = [];
    private readonly List<RayTracingDrawItem> _sceneRtDrawItems = [];
    private readonly List<RayTracingDrawItem> _gameRtDrawItems = [];
    private readonly HashSet<RenderItem> _sceneDrawnItems = [];
    private readonly HashSet<RenderItem> _gameDrawnItems = [];
    private Matrix4x4 _sceneCurrentViewProjection;
    private Matrix4x4 _gameCurrentViewProjection;
    private Matrix4x4 _scenePreviousViewProjection;
    private Matrix4x4 _gamePreviousViewProjection;
    private Vector2 _sceneJitter;
    private Vector2 _gameJitter;
    private Vector2 _scenePreviousJitter;
    private Vector2 _gamePreviousJitter;
    private uint _sceneJitterIndex;
    private uint _gameJitterIndex;
    private bool _sceneViewRendered;
    private bool _gameViewRendered;
    private const float SkyDepthThreshold = 0.9999f;
    private ShadowData _currentShadowData;
    private ShadowData _currentGameShadowData;
    private CascadedShadowSettings _shadowSettings = new();
    private int _lastShadowResolution;
    private float _shadowConstantBias = 1.25f;
    private float _shadowSlopeBias = 1.75f;
    private int _activeFrameSlot;
    private Size2D _lastTargetExtent;
    private Size2D _viewExtent;
    private Size2D _gameViewExtent;
    private Size2D _gameRenderExtent;
    private Size2D _lastGameExtent;
    private Size2D _presentExtent;
    private bool _editorMode;
    private bool _lastEditorMode;
    private bool _gameCameraPresent;
    private bool _skipOverlayThisFrame;
    private bool _temporalEnabled;
    private bool _rayTracingEnabled;
    private bool _lastGiEnabled;
    private bool _lastGiViaRt;
    private bool _lastRtMasterEnabled;
    private bool _lastRtGiActive;
    private float _lastRtResolutionScale;
    private bool _initialized;
    private long _lastReportedTimingFrame = -1;
    private Scene.Scene? _attachedScene;

    public float RenderInterpolationAlpha { get; set; } = 1f;

    public string? ShaderCacheDirectory { get; set; }

    public string Name => "Renderer";

    public Camera Camera { get; } = new();

    public Vector4 SkyColor
    {
        get => _skyColor;
        set
        {
            if (_skyColor == value)
                return;
            _skyColor = value;
            if (_graph is null)
                return;
            if (_forwardPass is not null)
                _graph.SetColorClear(_forwardPass, _hdrTarget!, _skyColor);
            if (_tonemapPass is not null)
                _graph.SetColorClear(_tonemapPass, _viewTarget ?? _tonemapTarget!, _skyColor);
            if (_gameForwardPass is not null)
                _graph.SetColorClear(_gameForwardPass, _gameHdrTarget!, _skyColor);
        }
    }

    public Vector4 EditorBackground { get; set; } = new(0.06f, 0.06f, 0.08f, 1f);

    public float Exposure { get; set; } = 1f;

    public float LodPixelBudget { get; set; } = 320f;

    public float ShadowStrength
    {
        get => _shadowSettings.Strength;
        set => ShadowSettings = _shadowSettings with { Strength = value };
    }

    public float ShadowIntensity { get; set; } = 1f;

    public int ShadowMapSize
    {
        get => _shadowSettings.Resolution;
        set => ShadowSettings = _shadowSettings with { Resolution = value };
    }

    public CascadedShadowSettings ShadowSettings
    {
        get => _shadowSettings;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            value.Validate();
            if (_shadowSettings == value)
                return;
            _shadowSettings = value;
            _sceneHistory.Invalidate();
            _gameHistory.Invalidate();
        }
    }

    public float ShadowConstantBias
    {
        get => _shadowConstantBias;
        set => SetShadowRasterBias(value, _shadowSlopeBias);
    }

    public float ShadowSlopeBias
    {
        get => _shadowSlopeBias;
        set => SetShadowRasterBias(_shadowConstantBias, value);
    }

    private void SetShadowRasterBias(float constant, float slope)
    {
        if (!float.IsFinite(constant) || constant < 0f || constant > 16f || !float.IsFinite(slope) || slope < 0f || slope > 16f)
            throw new ArgumentOutOfRangeException(nameof(constant));
        if (_shadowConstantBias == constant && _shadowSlopeBias == slope)
            return;
        _shadowConstantBias = constant;
        _shadowSlopeBias = slope;
        if (_initialized)
        {
            _rhi!.WaitIdle();
            _shadowPipeline?.Dispose();
            CreateShadowPipeline();
        }
        _sceneHistory.Invalidate();
        _gameHistory.Invalidate();
    }

    public GlobalIlluminationMode GiMode
    {
        get => _giMode;
        set
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            if (_giMode == value)
                return;
            _giMode = value;
            _sceneGiHistory.Invalidate();
            _gameGiHistory.Invalidate();
        }
    }

    public ShadowMode ShadowMode
    {
        get => _shadowMode;
        set
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            if (_shadowMode == value)
                return;
            _shadowMode = value;
            ResetTemporalHistory();
        }
    }

    public float ScreenSpaceGIIntensity { get; set; } = 0.5f;

    public bool RayTracingAvailable => _initialized && _rayTracingEnabled;

    public bool RayTracingEnabled
    {
        get => _rtMasterEnabled;
        set => _rtMasterEnabled = value;
    }

    public bool RayTracingReflectionsEnabled
    {
        get => _rtReflectionsEnabled;
        set => _rtReflectionsEnabled = value;
    }

    public float RayTracingResolutionScale
    {
        get => _rtResolutionScale;
        set
        {
            if (!float.IsFinite(value) || value < 0.25f || value > 1f)
                throw new ArgumentOutOfRangeException(nameof(value));
            _rtResolutionScale = value;
            _sceneHistory.Invalidate();
            _gameHistory.Invalidate();
            _sceneGiHistory.Invalidate();
            _gameGiHistory.Invalidate();
        }
    }

    public float RayTracingRayBias
    {
        get => _rtRayBias;
        set
        {
            if (!float.IsFinite(value) || value < 0.0001f || value > 0.1f)
                throw new ArgumentOutOfRangeException(nameof(value));
            _rtRayBias = value;
        }
    }

    public float RayTracingMaxDistance
    {
        get => _rtMaxDistance;
        set
        {
            if (!float.IsFinite(value) || value < 1f || value > 10000f)
                throw new ArgumentOutOfRangeException(nameof(value));
            _rtMaxDistance = value;
        }
    }

    public float RayTracingReflectionRoughnessLimit
    {
        get => _rtRoughnessLimit;
        set
        {
            if (!float.IsFinite(value) || value < 0.045f || value > 1f)
                throw new ArgumentOutOfRangeException(nameof(value));
            _rtRoughnessLimit = value;
        }
    }

    public int RayTracingSamples
    {
        get => _rtSamples;
        set
        {
            if (value is < 1 or > 8)
                throw new ArgumentOutOfRangeException(nameof(value));
            _rtSamples = value;
        }
    }

    public bool RayTracingAdaptiveQualityEnabled
    {
        get => _rtAdaptiveQualityEnabled;
        set => _rtAdaptiveQualityEnabled = value;
    }

    public RayTracingQualityPreset RayTracingShadowQuality
    {
        get => _rtShadowPreset;
        set => _rtShadowPreset = ValidateRayTracingPreset(value);
    }

    public RayTracingQualityPreset RayTracingReflectionQuality
    {
        get => _rtReflectionPreset;
        set => _rtReflectionPreset = ValidateRayTracingPreset(value);
    }

    public RayTracingQualityPreset RayTracingGiQuality
    {
        get => _rtGiPreset;
        set => _rtGiPreset = ValidateRayTracingPreset(value);
    }

    public (int Stage, RayTracingQualityProfile Profile, double GpuMilliseconds) GetRayTracingQuality(RayTracingQualityCamera camera, RayTracingQualityEffect effect)
    {
        RayTracingQualityPreset preset = effect switch
        {
            RayTracingQualityEffect.Shadows => _rtShadowPreset,
            RayTracingQualityEffect.Reflections => _rtReflectionPreset,
            _ => _rtGiPreset,
        };
        bool sceneActive = !_editorMode || SceneViewVisible;
        bool gameActive = _editorMode && GameViewVisible && _gameCameraPresent;
        int stage = _rtQualityController.GetStage(camera, effect, preset, _rtAdaptiveQualityEnabled, sceneActive, gameActive);
        return (stage, RayTracingQualityProfile.FromStage(stage), _rtQualityController.GetLastMilliseconds(camera, effect));
    }

    private static RayTracingQualityPreset ValidateRayTracingPreset(RayTracingQualityPreset value) =>
        Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value));

    public float RayTracingReflectionStrength
    {
        get => _rtReflectionStrength;
        set
        {
            if (!float.IsFinite(value) || value < 0f || value > 1f)
                throw new ArgumentOutOfRangeException(nameof(value));
            _rtReflectionStrength = value;
        }
    }

    public float ScreenSpaceGIRadius { get; set; } = 1.5f;

    public float ScreenSpaceGIHistoryWeight { get; set; } = 0.95f;

    public IReadOnlyList<RenderItem> Items => _items;

    public Camera EditorCamera { get; } = new();

    public PickingHit? PickEditorView(Vector2 normalizedViewport) => ScenePicking.Pick(ScenePicking.CreateRay(EditorCamera, normalizedViewport), _items);

    public IReadOnlyList<Light> Lights => _lights;

    public IReadOnlyList<Light> ActiveLights => _activeLights;

    public TextureFormat OverlayFormat => TextureFormat.B8G8R8A8Srgb;

    public ITexture? ViewTexture => _viewTarget is null ? null : TryGetGraphTexture(_viewTarget);

    public TextureFormat ViewFormat => _viewTarget?.Description.Format ?? TextureFormat.B8G8R8A8Srgb;

    public ITexture? GameTexture => _gameTarget is null ? ViewTexture : TryGetGraphTexture(_gameTarget);

    public TextureFormat GameFormat => _gameTarget?.Description.Format ?? ViewFormat;

    public Action<ICommandBuffer>? OverlayRender { get; set; }

    public Action<int>? PrepareEditorResources { get; set; }

    public Action<ICommandBuffer, int>? RenderEditorResources { get; set; }

    public ImageBasedLightingResources? ImageBasedLighting => _iblResources;

    public ITexture? DebugHdrTexture => _hdrTarget is null ? null : TryGetGraphTexture(_hdrTarget);

    public ImageLayout DebugHdrLayout => _hdrTarget?.CurrentLayout ?? ImageLayout.Undefined;

    public ITexture? DebugPresentTexture => _tonemapTarget is null ? null : TryGetGraphTexture(_tonemapTarget);

    public ImageLayout DebugPresentLayout => _tonemapTarget?.CurrentLayout ?? ImageLayout.Undefined;

    public ITexture? DebugShadowTexture => _shadowDepth is null ? null : TryGetGraphTexture(_shadowDepth);

    public ImageLayout DebugShadowLayout => _shadowDepth?.CurrentLayout ?? ImageLayout.Undefined;

    public ITexture? DebugRtVisibilityTexture => _rtVisibility is null ? null : TryGetGraphTexture(_rtVisibility);

    public ImageLayout DebugRtVisibilityLayout => _rtVisibility?.CurrentLayout ?? ImageLayout.Undefined;

    public ITexture? DebugRtRawReflectionTexture => _rtRawReflection is null ? null : TryGetGraphTexture(_rtRawReflection);

    public ImageLayout DebugRtRawReflectionLayout => _rtRawReflection?.CurrentLayout ?? ImageLayout.Undefined;

    public ITexture? DebugRtDepthTexture => _rtDepth is null ? null : TryGetGraphTexture(_rtDepth);

    public ImageLayout DebugRtDepthLayout => _rtDepth?.CurrentLayout ?? ImageLayout.Undefined;

    public ITexture? DebugRtNormalRoughnessTexture => _rtNormalRoughness is null ? null : TryGetGraphTexture(_rtNormalRoughness);

    public ImageLayout DebugRtNormalRoughnessLayout => _rtNormalRoughness?.CurrentLayout ?? ImageLayout.Undefined;

    public int DebugRenderItemCount => _sceneItems.Count;

    public ITexture? SwapchainTargetTexture => _swapchainTarget?.Texture;

    public Func<string, Mesh>? MeshResolver { get; set; }

    public Func<string, Mesh?>? MeshRequestResolver { get; set; }

    public Func<string, string, Mesh?>? ModelPartRequestResolver { get; set; }

    public int LoadingMeshCount { get; private set; }

    public int FailedMeshCount { get; private set; }

    public Func<string, bool>? MeshImportFailedResolver { get; set; }

    public long SceneSourceTriangleCount => _sceneItems.Values.Sum(item => (long)item.Mesh.IndexCount / 3);

    public long SceneSourceVertexCount => _sceneItems.Values.Sum(item => (long)item.Mesh.VertexCount);

    public long SceneDrawnTriangleCount { get; private set; }

    public long GameDrawnTriangleCount { get; private set; }

    public long ShadowDrawnTriangleCount { get; private set; }

    public int SceneLodLevel { get; private set; }

    public int GameLodLevel { get; private set; }

    public int SceneDrawnObjects { get; private set; }

    public int GameDrawnObjects { get; private set; }

    public int ShadowDrawnObjects { get; private set; }

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

    public void RefreshTargets()
    {
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

    public void SetGameViewExtent(Size2D extent)
    {
        if (extent.Width < 8 || extent.Height < 8 || extent == _gameViewExtent)
            return;
        _gameViewExtent = extent;
        if (_initialized && _editorMode)
            RebuildTargets();
    }

    public void SetViewExtents(Size2D viewExtent, Size2D gameViewExtent)
    {
        bool viewChanged = viewExtent.Width >= 8 && viewExtent.Height >= 8 && viewExtent != _viewExtent;
        bool gameChanged = gameViewExtent.Width >= 8 && gameViewExtent.Height >= 8 && gameViewExtent != _gameViewExtent;
        if (!viewChanged && !gameChanged)
            return;
        if (viewChanged)
            _viewExtent = viewExtent;
        if (gameChanged)
            _gameViewExtent = gameViewExtent;
        if (_initialized)
            RebuildTargets();
        NotifyCameraCut();
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
        _activeLights = [];
        SkyColor = scene.SkyColor;
        _skyboxMode = scene.SkyboxMode;
        _cloudWindPhase = 0f;
        ResetTemporalHistory();
        _scenePreviousModels.Clear();
        _gamePreviousModels.Clear();
    }

    public void NotifyCameraCut() => ResetTemporalHistory();

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
        if (_rtPrepassProgram is not null)
        {
            material.RtPrepassSet = _rtPrepassProgram.CreateDescriptorSet(1);
            material.RtPrepassSet.WriteUniformBuffer(0, material.ParamsBuffer);
            material.RtPrepassSet.WriteTexture(3, textures.Roughness);
            material.RtPrepassSet.WriteTexture(4, textures.Normal);
            material.RtPrepassSet.WriteSampler(6, _linearSampler!);
        }
        if (_pbrRtProgram is not null)
        {
            material.RtSet = _pbrRtProgram.CreateDescriptorSet(1);
            material.RtSet.WriteUniformBuffer(0, material.ParamsBuffer);
            material.RtSet.WriteTexture(1, textures.Albedo);
            material.RtSet.WriteTexture(2, textures.Metallic);
            material.RtSet.WriteTexture(3, textures.Roughness);
            material.RtSet.WriteTexture(4, textures.Normal);
            material.RtSet.WriteTexture(5, textures.AmbientOcclusion);
            material.RtSet.WriteSampler(6, _linearSampler!);
            material.RtSet.WriteTexture(7, textures[MaterialTextureSlot.Emission]);
        }
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
        _rayTracingEnabled = RayTracingPolicy.ShouldEnable(_rhiModule.RayTracingMode, _device.Features);
        if (_rhiModule.RayTracingMode != RayTracingMode.Off && !_rayTracingEnabled)
        {
            _log.Warning(
                $"Ray tracing is unavailable; retaining raster shadows. {_device.Features.RayTracingUnavailableReason ?? "The device lacks acceleration structures or ray-tracing pipelines."}");
        }
        _compiler = new SlangShaderCompiler(_log, cacheDirectory: ShaderCacheDirectory);
        _graph = new RenderGraph(_device, _log);
        int framesInFlight = (int)_rhi.FramesInFlight;
        _uniformRing = new FrameUniformRing(_device, framesInFlight, 256 * 1024);
        _frameSets = Enumerable.Range(0, framesInFlight).Select(_ => new List<IDescriptorSet>()).ToArray();
        if (_rayTracingEnabled)
        {
            _sceneRtReadiness = new RayTracingFrameSlotReadiness(framesInFlight);
            _gameRtReadiness = new RayTracingFrameSlotReadiness(framesInFlight);
        }
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
        if (_rayTracingEnabled)
        {
            _rtReflectionTextureArraySize = RayTracingTextureCapacity.Calculate(_device.Features);
            _sceneRayTracingScene = new RayTracingScene(_device, framesInFlight, _defaultMaterialTextures, _rtReflectionTextureArraySize);
            _gameRayTracingScene = new RayTracingScene(_device, framesInFlight, _defaultMaterialTextures, _rtReflectionTextureArraySize);
            _log.Information($"RT material texture array capacity: {_rtReflectionTextureArraySize / RayTracingTextureCapacity.TexturesPerMaterial} material sets ({_rtReflectionTextureArraySize} sampled images)");
        }
        _bloomFallbackTexture = _device.CreateTexture(new TextureDescription
        {
            Extent = new Size2D(1, 1),
            Format = TextureFormat.R16G16B16A16Float,
            Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
        });
        _device.UploadTextureData(_bloomFallbackTexture, BlackHalfPixel());
        _temporalEnabled = _device.Features.SupportsCompute;
        CompilePipelines();
        _iblResources = ImageBasedLightingResources.Create(
            _device,
            _compiler,
            Path.Combine(_shaderDirectory, "IBLBake.slang"));
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
        float interpolationAlpha = Math.Clamp(frameTime.FixedInterpolationAlpha, 0f, 1f);
        foreach (RenderItem item in _sceneItems.Values)
            item.RenderInterpolationAlpha = interpolationAlpha;
        RenderInterpolationAlpha = interpolationAlpha;
        _skipOverlayThisFrame = false;
        DrawnObjects = 0;
        CulledObjects = 0;
        SceneDrawnTriangleCount = 0;
        GameDrawnTriangleCount = 0;
        ShadowDrawnTriangleCount = 0;
        SceneLodLevel = 0;
        GameLodLevel = 0;
        SceneDrawnObjects = 0;
        GameDrawnObjects = 0;
        ShadowDrawnObjects = 0;
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        _cloudWindPhase += frameTime.DeltaTime * CloudWindSpeed;
        _cloudJitterSeed = frameTime.FrameIndex % 1024 * 0.618034f;
        SyncScene();
        UpdateLights();
        ColliderGizmoSource?.Invoke();
        if (DebugDraw.HasLines)
            UploadDebugLines();
        else
            _debugLinesVertexCount = 0;
        SceneSyncMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        if (_rhi!.LatestGpuFrameTiming is { } completedTiming && completedTiming.FrameNumber != _lastReportedTimingFrame)
        {
            _rtQualityController.Update(completedTiming, _rhi.SupportsGpuTimestamps);
            _lastReportedTimingFrame = completedTiming.FrameNumber;
            for (int cameraIndex = 0; cameraIndex < 2; cameraIndex++)
            for (int effectIndex = 0; effectIndex < 3; effectIndex++)
                _rtTimingMilliseconds[cameraIndex * 3 + effectIndex] = (float)_rtQualityController.GetLastMilliseconds((RayTracingQualityCamera)cameraIndex, (RayTracingQualityEffect)effectIndex);
        }
        else if (!_rhi.SupportsGpuTimestamps)
            _rtQualityController.Update(null, false);
        start = System.Diagnostics.Stopwatch.GetTimestamp();
        FrameStatus status = _rhi.BeginFrame();
        FrameWaitMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        if (status is FrameStatus.Skipped)
            return;
        start = System.Diagnostics.Stopwatch.GetTimestamp();
        if (!_editorMode || _rhi.Swapchain.Extent != _presentExtent)
        {
            _skipOverlayThisFrame = _editorMode;
            RebuildTargets();
        }

        _activeFrameSlot = (int)_rhi.CurrentFrameSlot;
        PrepareEditorResources?.Invoke(_activeFrameSlot);
        foreach (IDescriptorSet frameSet in _frameSets[_activeFrameSlot])
            frameSet.Dispose();
        _frameSets[_activeFrameSlot].Clear();
        _uniformRing!.BeginFrame(_activeFrameSlot);
        if (_editorMode && !SceneViewVisible)
        {
            _sceneHistory.Invalidate();
            _sceneGiHistory.Invalidate();
            _scenePreviousModels.Clear();
        }
        if (_editorMode && !GameViewVisible)
        {
            _gameHistory.Invalidate();
            _gameGiHistory.Invalidate();
            _gamePreviousModels.Clear();
        }
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

        _sceneViewRendered = _temporalEnabled && (!_editorMode || SceneViewVisible);
        _gameViewRendered = _temporalEnabled && _editorMode && GameViewVisible && _gameCameraPresent;
        _sceneDrawnItems.Clear();
        _gameDrawnItems.Clear();
        _scenePreparedDrawItems.Clear();
        _gamePreparedDrawItems.Clear();
        _sceneRtDrawItems.Clear();
        _gameRtDrawItems.Clear();
        if (!_editorMode || SceneViewVisible)
            PrepareViewDrawItems(_scenePreparedDrawItems, _sceneRtDrawItems, _editorMode ? EditorCamera : Camera, _lastTargetExtent);
        if (_editorMode && GameViewVisible && _gameCameraPresent)
            PrepareViewDrawItems(_gamePreparedDrawItems, _gameRtDrawItems, Camera, _gameRenderExtent.Width > 8 ? _gameRenderExtent : _lastTargetExtent);
        foreach (RayTracingBuildGraphSlot buildSlot in _rtBuildGraphSlots)
        {
            buildSlot.Enabled = false;
            buildSlot.Recorded = false;
        }
        _sceneRtCanTraceThisFrame = false;
        _gameRtCanTraceThisFrame = false;
        if (_lastRtMasterEnabled && (!_editorMode || SceneViewVisible))
            PrepareRayTracingView(_sceneRayTracingScene, _sceneRtReadiness, _sceneRtDrawItems, gameView: false);
        if (_lastRtMasterEnabled && _editorMode && GameViewVisible && _gameCameraPresent)
            PrepareRayTracingView(_gameRayTracingScene, _gameRtReadiness, _gameRtDrawItems, gameView: true);
        if (_rayTracingEnabled)
        {
            Size2D sceneControlExtent = _rtVisibility?.Description.Extent ?? _lastTargetExtent;
            Size2D gameControlExtent = _gameRtVisibility?.Description.Extent ?? _lastTargetExtent;
            WriteRtShadowControl(_rtSceneControlAllocation, _sceneRtCanTraceThisFrame, _currentShadowData, sceneControlExtent);
            WriteRtShadowControl(_rtGameControlAllocation, _gameRtCanTraceThisFrame, _currentGameShadowData, gameControlExtent);
            _uniformRing!.FlushFrame();
        }
        _device!.FlushUploads();
        ICommandBuffer commandBuffer = _rhi.GetFrameCommandBuffer();
        commandBuffer.Begin();
        RenderEditorResources?.Invoke(commandBuffer, _activeFrameSlot);
        _graph.Execute(commandBuffer);
        commandBuffer.End();
        _rhi.EndFrame(commandBuffer);
        foreach (RayTracingBuildGraphSlot buildSlot in _rtBuildGraphSlots)
        {
            if (!buildSlot.Recorded)
                continue;
            IAccelerationStructure topLevel = buildSlot.Scene.GetFrame(buildSlot.FrameSlot).TopLevel!;
            buildSlot.Readiness.MarkSubmitted(buildSlot.FrameSlot, topLevel);
        }
        CommitTemporalFrame();
        RenderCpuMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    private void AllocateFrameUniforms()
    {
        Size2D extent = _lastTargetExtent;
        Camera sceneCamera = _editorMode ? EditorCamera : Camera;
        _sceneJitter = _temporalEnabled ? TemporalJitter.Sample(_sceneJitterIndex) : Vector2.Zero;
        _gameJitter = _temporalEnabled ? TemporalJitter.Sample(_gameJitterIndex) : Vector2.Zero;
        Matrix4x4 sceneViewProjection = sceneCamera.CreateJitteredViewProjection(_sceneJitter, extent);
        Size2D gameProjectionExtent = _editorMode && _gameRenderExtent.Width > 8 && _gameRenderExtent.Height > 8 ? _gameRenderExtent : extent;
        if (_editorMode && _gameCameraPresent)
            Camera.AspectRatio = (float)gameProjectionExtent.Width / Math.Max(1, gameProjectionExtent.Height);
        else
            Camera.AspectRatio = (float)extent.Width / Math.Max(1, extent.Height);
        sceneCamera.AspectRatio = (float)extent.Width / Math.Max(1, extent.Height);
        Matrix4x4 gameViewProjection = Camera.CreateJitteredViewProjection(_gameJitter, gameProjectionExtent);
        _sceneCurrentViewProjection = sceneViewProjection;
        _gameCurrentViewProjection = gameViewProjection;
        if (!_sceneHistory.IsValid)
        {
            _scenePreviousViewProjection = sceneViewProjection;
            _scenePreviousJitter = _sceneJitter;
        }
        if (!_gameHistory.IsValid)
        {
            _gamePreviousViewProjection = gameViewProjection;
            _gamePreviousJitter = _gameJitter;
        }
        CameraData cameraData = new()
        {
            ViewProjection = sceneViewProjection,
            PreviousViewProjection = _scenePreviousViewProjection,
            CameraPosition = sceneCamera.Position,
            Jitter = _sceneJitter,
            PreviousJitter = _scenePreviousJitter,
        };
        CameraData gameCameraData = new()
        {
            ViewProjection = gameViewProjection,
            PreviousViewProjection = _gamePreviousViewProjection,
            CameraPosition = Camera.Position,
            Jitter = _gameJitter,
            PreviousJitter = _gameJitter,
        };
        _sceneSkyCameraPosition = sceneCamera.Position;
        _gameSkyCameraPosition = Camera.Position;
        ShadowCasterBounds[] casterBounds = _sceneItems.Values.Where(item => item.RenderingMode.CastsShadows()).Select(item => CascadedShadows.TransformBounds(item.Mesh.BoundsMin, item.Mesh.BoundsMax, item.ModelMatrix)).ToArray();
        _currentShadowData = BuildShadowData(sceneCamera, casterBounds);
        _currentGameShadowData = BuildShadowData(Camera, casterBounds);
        _cameraAllocation = _uniformRing!.Allocate(CameraData.SizeInBytes);
        _uniformRing.Write(_cameraAllocation, StructuredBytes(cameraData));
        _gameCameraAllocation = _uniformRing.Allocate(CameraData.SizeInBytes);
        _uniformRing.Write(_gameCameraAllocation, StructuredBytes(gameCameraData));
        if (_temporalEnabled)
        {
            SsgiData sceneSsgiData = new()
            {
                ViewProjection = sceneViewProjection,
                InverseViewProjection = sceneViewProjection.Invert(),
                CameraPositionAndRadius = new Vector4(sceneCamera.Position, ScreenSpaceGIRadius),
                ExtentAndFrame = new Vector4(extent.Width, extent.Height, _sceneJitterIndex, SkyDepthThreshold),
            };
            _ssgiSceneAllocation = _uniformRing.Allocate(SsgiData.SizeInBytes);
            _uniformRing.Write(_ssgiSceneAllocation, StructuredBytes(sceneSsgiData));
            SsgiData gameSsgiData = new()
            {
                ViewProjection = gameViewProjection,
                InverseViewProjection = gameViewProjection.Invert(),
                CameraPositionAndRadius = new Vector4(Camera.Position, ScreenSpaceGIRadius),
                ExtentAndFrame = new Vector4(gameProjectionExtent.Width, gameProjectionExtent.Height, _gameJitterIndex, SkyDepthThreshold),
            };
            _ssgiGameAllocation = _uniformRing.Allocate(SsgiData.SizeInBytes);
            _uniformRing.Write(_ssgiGameAllocation, StructuredBytes(gameSsgiData));
        }
        _shadowAllocation = _uniformRing.Allocate(Marshal.SizeOf<ShadowData>());
        _uniformRing.Write(_shadowAllocation, StructuredBytes(_currentShadowData));
        _gameShadowAllocation = _uniformRing.Allocate(Marshal.SizeOf<ShadowData>());
        _uniformRing.Write(_gameShadowAllocation, StructuredBytes(_currentGameShadowData));
        _lightsAllocation = _uniformRing.Allocate(Marshal.SizeOf<LightsData>());
        _uniformRing.Write(_lightsAllocation, StructuredBytes(_lightsData));
        if (_rayTracingEnabled)
        {
            _rtPointLightIndex = FindRtPointLightIndex();
            _rtSceneViewAllocation = _uniformRing.Allocate(RtViewData.SizeInBytes);
            _uniformRing.Write(_rtSceneViewAllocation, StructuredBytes(BuildRtViewData(sceneCamera, sceneViewProjection, _scenePreviousViewProjection, _sceneJitterIndex, _currentShadowData, _rtPointLightIndex, gameView: false)));
            _rtGameViewAllocation = _uniformRing.Allocate(RtViewData.SizeInBytes);
            _uniformRing.Write(_rtGameViewAllocation, StructuredBytes(BuildRtViewData(Camera, gameViewProjection, _gamePreviousViewProjection, _gameJitterIndex, _currentGameShadowData, _rtPointLightIndex, gameView: true)));
            _rtSceneControlAllocation = _uniformRing.Allocate(RtShadowControl.SizeInBytes);
            _uniformRing.Write(_rtSceneControlAllocation, StructuredBytes(new RtShadowControl()));
            _rtGameControlAllocation = _uniformRing.Allocate(RtShadowControl.SizeInBytes);
            _uniformRing.Write(_rtGameControlAllocation, StructuredBytes(new RtShadowControl()));
        }
    }

    private int FindRtPointLightIndex()
    {
        int best = -1;
        float bestIntensity = 0f;
        for (int index = 0; index < _activeLights.Count; index++)
        {
            Light light = _activeLights[index];
            if (light.Type == LightType.Point && light.Intensity > bestIntensity)
            {
                best = index;
                bestIntensity = light.Intensity;
            }
        }
        return best;
    }

    private void UpdateGraphDescriptors()
    {
        if (!_temporalEnabled)
            return;
        _graph!.RebindImportedTexture(
            _historyRead!,
            _sceneHistory.ReadTexture,
            _sceneHistory.ReadInitialized ? ImageLayout.ShaderReadOnlyOptimal : ImageLayout.Undefined);
        _graph.RebindImportedTexture(
            _historyWrite!,
            _sceneHistory.WriteTexture,
            _sceneHistory.WriteInitialized ? ImageLayout.ShaderReadOnlyOptimal : ImageLayout.Undefined,
            ImageLayout.ShaderReadOnlyOptimal);
        if (_editorMode)
        {
            _graph.RebindImportedTexture(
                _gameHistoryRead!,
                _gameHistory.ReadTexture,
                _gameHistory.ReadInitialized ? ImageLayout.ShaderReadOnlyOptimal : ImageLayout.Undefined);
            _graph.RebindImportedTexture(
                _gameHistoryWrite!,
                _gameHistory.WriteTexture,
                _gameHistory.WriteInitialized ? ImageLayout.ShaderReadOnlyOptimal : ImageLayout.Undefined,
                ImageLayout.ShaderReadOnlyOptimal);
        }
        if (_sceneGiHistoryRead is not null && _sceneGiHistoryWrite is not null)
        {
            _graph.RebindImportedTexture(
                _sceneGiHistoryRead,
                _sceneGiHistory.ReadTexture,
                _sceneGiHistory.ReadInitialized ? ImageLayout.ShaderReadOnlyOptimal : ImageLayout.Undefined);
            _graph.RebindImportedTexture(
                _sceneGiHistoryWrite,
                _sceneGiHistory.WriteTexture,
                _sceneGiHistory.WriteInitialized ? ImageLayout.ShaderReadOnlyOptimal : ImageLayout.Undefined,
                ImageLayout.ShaderReadOnlyOptimal);
        }
        if (_gameGiHistoryRead is not null && _gameGiHistoryWrite is not null)
        {
            _graph.RebindImportedTexture(
                _gameGiHistoryRead,
                _gameGiHistory.ReadTexture,
                _gameGiHistory.ReadInitialized ? ImageLayout.ShaderReadOnlyOptimal : ImageLayout.Undefined);
            _graph.RebindImportedTexture(
                _gameGiHistoryWrite,
                _gameGiHistory.WriteTexture,
                _gameGiHistory.WriteInitialized ? ImageLayout.ShaderReadOnlyOptimal : ImageLayout.Undefined,
                ImageLayout.ShaderReadOnlyOptimal);
        }
        if (_sceneReflectionHistoryRead is not null && _sceneReflectionHistoryWrite is not null)
        {
            _graph.RebindImportedTexture(
                _sceneReflectionHistoryRead,
                _sceneReflectionHistory.ReadTexture,
                _sceneReflectionHistory.ReadInitialized ? ImageLayout.ShaderReadOnlyOptimal : ImageLayout.Undefined);
            _graph.RebindImportedTexture(
                _sceneReflectionHistoryWrite,
                _sceneReflectionHistory.WriteTexture,
                _sceneReflectionHistory.WriteInitialized ? ImageLayout.ShaderReadOnlyOptimal : ImageLayout.Undefined,
                ImageLayout.ShaderReadOnlyOptimal);
        }
        if (_gameReflectionHistoryRead is not null && _gameReflectionHistoryWrite is not null)
        {
            _graph.RebindImportedTexture(
                _gameReflectionHistoryRead,
                _gameReflectionHistory.ReadTexture,
                _gameReflectionHistory.ReadInitialized ? ImageLayout.ShaderReadOnlyOptimal : ImageLayout.Undefined);
            _graph.RebindImportedTexture(
                _gameReflectionHistoryWrite,
                _gameReflectionHistory.WriteTexture,
                _gameReflectionHistory.WriteInitialized ? ImageLayout.ShaderReadOnlyOptimal : ImageLayout.Undefined,
                ImageLayout.ShaderReadOnlyOptimal);
        }
    }

    public bool SceneViewVisible { get; set; } = true;
    public bool GameViewVisible { get; set; } = true;

    private void BuildGraphTopology(Size2D sceneExtent)
    {
        _forwardPass = null;
        _tonemapPass = null;
        _gameForwardPass = null;
        _gameTonemapPass = null;
        _ssgiPass = null;
        _giTemporalPass = null;
        _giCompositePass = null;
        _gameSsgiPass = null;
        _gameGiTemporalPass = null;
        _gameGiCompositePass = null;
        _rtScenePrepassPass = null;
        _rtSceneTracePass = null;
        _rtGamePrepassPass = null;
        _rtGameTracePass = null;
        _rtSceneReflectionTracePass = null;
        _rtSceneFilterPass = null;
        _rtSceneCompositePass = null;
        _rtGameReflectionTracePass = null;
        _rtGameFilterPass = null;
        _rtGameCompositePass = null;
        _rtSceneGiPass = null;
        _rtGameGiPass = null;
        RenderGraphPass shadowPass = _graph!.AddGraphicsPass("SceneShadows", (commandBuffer, _) => ExecuteShadowPass(commandBuffer, _currentShadowData));
        _graph.WritesDepth(shadowPass, _shadowDepth!, clear: true, 1f);

        _forwardPass = _graph.AddGraphicsPass("ForwardPass", (commandBuffer, _) => ExecuteForwardPass(commandBuffer, sceneExtent));
        _graph.WritesColor(_forwardPass, _hdrTarget!, clear: true, SkyColor);
        if (_temporalEnabled)
            _graph.WritesColor(_forwardPass, _motionTarget!, clear: true, Vector4.Zero);
        if (_rayTracingEnabled && _rtSpecular is not null)
        {
            _graph.WritesColor(_forwardPass, _rtSpecular!, clear: true, Vector4.Zero);
            _graph.WritesColor(_forwardPass, _rtSpecularResponse!, clear: true, Vector4.Zero);
        }
        _graph.WritesDepth(_forwardPass, _mainDepth!, clear: true, 1f);
        _graph.ReadsShader(_forwardPass, _shadowDepth!, PipelineStages.FragmentShader);
        if (_rayTracingEnabled && _rtNormalRoughness is not null)
            AddRtShadowChain(gameView: false, sceneExtent, _forwardPass);

        RenderGraphPass? temporalPass = null;
        RenderGraphPass? bloomPass = null;
        RenderGraphResource sceneColor = _hdrTarget!;
        RenderGraphResource sceneBloom = _bloomFallback!;
        if (_temporalEnabled)
        {
            temporalPass = _graph.AddComputePass("TemporalResolve", (commandBuffer, graph) =>
                ExecuteTemporalPass(commandBuffer, graph, _hdrTarget!, _motionTarget!, _mainDepth!, _historyRead!, _historyWrite!, _sceneHistory));
            _graph.ReadsShader(temporalPass, _hdrTarget!, PipelineStages.ComputeShader);
            _graph.ReadsShader(temporalPass, _motionTarget!, PipelineStages.ComputeShader);
            _graph.ReadsShader(temporalPass, _mainDepth!, PipelineStages.ComputeShader);
            _graph.ReadsShader(temporalPass, _historyRead!, PipelineStages.ComputeShader);
            _graph.WritesStorage(temporalPass, _historyWrite!, PipelineStages.ComputeShader);
            sceneColor = _historyWrite!;

            RenderGraphPass? giCompositePass = null;
            if (_giTarget is not null)
            {
                bool giViaRt = _lastGiViaRt;
                if (giViaRt)
                {
                    Func<bool> sceneVisible = () => _forwardPass!.ShouldExecute?.Invoke() ?? true;
                    _rtSceneGiPass = _graph.AddRayTracingPassBefore(temporalPass!, "RtSceneGi", (commandBuffer, graph) => ExecuteRtGiPass(commandBuffer, graph, gameView: false));
                    RenderGraphPass clearGi = _graph.AddGraphicsPassBefore(_rtSceneGiPass, "RtSceneGiClear", (_, _) => { });
                    _graph.WritesColor(clearGi, _giTarget!, clear: true, Vector4.Zero);
                    _graph.WritesColor(clearGi, _rtGiValidity!, clear: true, Vector4.One);
                    clearGi.ShouldExecute = sceneVisible;
                    _graph.ReadsSampled(_rtSceneGiPass, _rtDepth!);
                    _graph.ReadsSampled(_rtSceneGiPass, _rtNormalRoughness!);
                    _graph.ReadsShaderBindingTable(_rtSceneGiPass, _rtGiSbtResource!);
                    _graph.WritesStorage(_rtSceneGiPass, _giTarget!);
                    _graph.WritesStorage(_rtSceneGiPass, _rtGiValidity!);
                    _rtSceneGiPass.ShouldExecute = () => sceneVisible()
                        && _rtGiPipeline is not null
                        && _lastRtGiActive
                        && _sceneRtCanTraceThisFrame;
                }
                else
                {
                    _ssgiPass = _graph.AddComputePass("ScreenSpaceGI", (commandBuffer, graph) =>
                        ExecuteSsgiPass(commandBuffer, graph, _hdrTarget!, _mainDepth!, _giTarget!, _ssgiSceneAllocation));
                    _graph.ReadsShader(_ssgiPass, _hdrTarget!, PipelineStages.ComputeShader);
                    _graph.ReadsShader(_ssgiPass, _mainDepth!, PipelineStages.ComputeShader);
                    _graph.WritesStorage(_ssgiPass, _giTarget!, PipelineStages.ComputeShader);
                }

                RenderGraphResource giInput = giViaRt ? _rtGiFiltered! : _giTarget!;
                _giTemporalPass = _graph.AddComputePass("GiTemporalResolve", (commandBuffer, graph) =>
                    ExecuteTemporalPass(commandBuffer, graph, giInput, _motionTarget!, _mainDepth!, _sceneGiHistoryRead!, _sceneGiHistoryWrite!, _sceneGiHistory, ScreenSpaceGIHistoryWeight, denoiseGi: giViaRt));
                if (giViaRt)
                    AddRtGiFilterChain(gameView: false, _giTarget!, _rtGiFilterScratch!, _rtGiFiltered!, _giTemporalPass);
                _graph.ReadsShader(_giTemporalPass, giInput, PipelineStages.ComputeShader);
                _graph.ReadsShader(_giTemporalPass, _motionTarget!, PipelineStages.ComputeShader);
                _graph.ReadsShader(_giTemporalPass, _mainDepth!, PipelineStages.ComputeShader);
                _graph.ReadsShader(_giTemporalPass, _sceneGiHistoryRead!, PipelineStages.ComputeShader);
                _graph.WritesStorage(_giTemporalPass, _sceneGiHistoryWrite!, PipelineStages.ComputeShader);

                _giCompositePass = giCompositePass = _graph.AddComputePass("GiComposite", (commandBuffer, graph) =>
                    ExecuteGiCompositePass(commandBuffer, graph, _historyWrite!, _sceneGiHistoryWrite!, _compositeTarget!));
                _graph.ReadsShader(_giCompositePass, _historyWrite!, PipelineStages.ComputeShader);
                _graph.ReadsShader(_giCompositePass, _sceneGiHistoryWrite!, PipelineStages.ComputeShader);
                _graph.WritesStorage(_giCompositePass, _compositeTarget!, PipelineStages.ComputeShader);
                sceneColor = _compositeTarget!;
            }

            RenderGraphResource bloomSource = sceneColor;
            bloomPass = _graph.AddComputePass("Bloom", (commandBuffer, graph) => ExecuteBloomPass(commandBuffer, graph, bloomSource, _bloomTarget!));
            _graph.ReadsShader(bloomPass, sceneColor, PipelineStages.ComputeShader);
            _graph.WritesStorage(bloomPass, _bloomTarget!, PipelineStages.ComputeShader);
            if (_rayTracingEnabled && _rtRawReflection is not null)
            {
                AddRtReflectionChain(gameView: false, sceneColor, bloomPass);
                sceneColor = bloomSource = _rtCompositeTarget!;
                _graph.ReadsShader(bloomPass, _rtCompositeTarget!, PipelineStages.ComputeShader);
            }
            sceneBloom = _bloomTarget!;
        }

        RenderGraphResource tonemapOutput = _viewTarget ?? _tonemapTarget!;
        _tonemapPass = _graph.AddGraphicsPass("TonemapPass", (commandBuffer, _) => ExecuteTonemapPass(commandBuffer, tonemapOutput.Description.Extent, sceneColor, sceneBloom));
        _graph.WritesColor(_tonemapPass, tonemapOutput, clear: true, SkyColor);
        _graph.ReadsShader(_tonemapPass, sceneColor, PipelineStages.FragmentShader);
        _graph.ReadsShader(_tonemapPass, sceneBloom, PipelineStages.FragmentShader);

        Func<bool> sceneSkyVisible = () => (_forwardPass!.ShouldExecute?.Invoke() ?? true)
            && _skyPipeline is not null
            && _skyboxMode == SkyboxMode.Procedural;
        RenderGraphPass sceneSkyPass = _graph.AddGraphicsPassBefore(temporalPass ?? _tonemapPass, "ProceduralSky",
            (commandBuffer, _) => ExecuteSkyPass(commandBuffer, sceneExtent, gameView: false));
        sceneSkyPass.ShouldExecute = sceneSkyVisible;
        _graph.WritesColor(sceneSkyPass, _hdrTarget!, clear: false);
        _graph.ReadsDepth(sceneSkyPass, _mainDepth!);

        RenderGraphPass gizmoPass = _graph.AddGraphicsPassBefore(_tonemapPass, "ColliderGizmos",
            (commandBuffer, _) => ExecuteColliderGizmoPass(commandBuffer, sceneExtent));
        gizmoPass.ShouldExecute = () => DebugDraw.HasLines;
        _graph.WritesColor(gizmoPass, sceneColor, clear: false);
        _graph.ReadsDepth(gizmoPass, _mainDepth!);

        Func<bool> sceneCloudsVisible = () => (_forwardPass!.ShouldExecute?.Invoke() ?? true)
            && CloudsEnabled
            && _cloudShapeTexture is not null;
        RenderGraphPass sceneCloudsPass = _graph.AddGraphicsPassBefore(temporalPass ?? _tonemapPass, "VolumetricClouds",
            (commandBuffer, _) => ExecuteCloudsPass(commandBuffer, sceneExtent, gameView: false));
        sceneCloudsPass.ShouldExecute = sceneCloudsVisible;
        _graph.WritesColor(sceneCloudsPass, _hdrTarget!, clear: false);
        _graph.ReadsDepth(sceneCloudsPass, _mainDepth!);

        if (_editorMode)
        {
            RenderGraphPass gameShadowPass = _graph.AddGraphicsPass("GameShadows", (commandBuffer, _) => ExecuteShadowPass(commandBuffer, _currentGameShadowData));
            _graph.WritesDepth(gameShadowPass, _gameShadowDepth!, clear: true, 1f);
            gameShadowPass.ShouldExecute = () => GameViewVisible && _gameCameraPresent && EffectiveShadowMode == ShadowMode.Cascaded;
            _gameForwardPass = _graph.AddGraphicsPass("GameForward", (commandBuffer, _) => ExecuteForwardPass(commandBuffer, _gameRenderExtent.Width > 8 ? _gameRenderExtent : sceneExtent, _gameCameraAllocation));
            _graph.WritesColor(_gameForwardPass, _gameHdrTarget!, clear: true, SkyColor);
            if (_temporalEnabled)
                _graph.WritesColor(_gameForwardPass, _gameMotionTarget!, clear: true, Vector4.Zero);
            if (_rayTracingEnabled && _gameRtSpecular is not null)
            {
                _graph.WritesColor(_gameForwardPass, _gameRtSpecular!, clear: true, Vector4.Zero);
                _graph.WritesColor(_gameForwardPass, _gameRtSpecularResponse!, clear: true, Vector4.Zero);
            }
            _graph.WritesDepth(_gameForwardPass, _gameDepth!, clear: true, 1f);
            _graph.ReadsShader(_gameForwardPass, _gameShadowDepth!, PipelineStages.FragmentShader);
            if (_rayTracingEnabled && _gameRtNormalRoughness is not null)
                AddRtShadowChain(gameView: true, _gameDepth!.Description.Extent, _gameForwardPass);

            RenderGraphPass? gameTemporalPass = null;
            RenderGraphPass? gameBloomPass = null;
            RenderGraphResource gameColor = _gameHdrTarget!;
            RenderGraphResource gameBloom = _bloomFallback!;
            if (_temporalEnabled)
            {
                gameTemporalPass = _graph.AddComputePass("GameTemporal", (commandBuffer, graph) =>
                    ExecuteTemporalPass(commandBuffer, graph, _gameHdrTarget!, _gameMotionTarget!, _gameDepth!, _gameHistoryRead!, _gameHistoryWrite!, _gameHistory));
                _graph.ReadsShader(gameTemporalPass, _gameHdrTarget!, PipelineStages.ComputeShader);
                _graph.ReadsShader(gameTemporalPass, _gameMotionTarget!, PipelineStages.ComputeShader);
                _graph.ReadsShader(gameTemporalPass, _gameDepth!, PipelineStages.ComputeShader);
                _graph.ReadsShader(gameTemporalPass, _gameHistoryRead!, PipelineStages.ComputeShader);
                _graph.WritesStorage(gameTemporalPass, _gameHistoryWrite!, PipelineStages.ComputeShader);
                gameColor = _gameHistoryWrite!;

                if (_gameGiTarget is not null)
                {
                    bool giViaRt = _lastGiViaRt;
                    if (giViaRt)
                    {
                        Func<bool> gameVisible = () => _gameForwardPass!.ShouldExecute?.Invoke() ?? true;
                        _rtGameGiPass = _graph.AddRayTracingPassBefore(gameTemporalPass!, "RtGameGi", (commandBuffer, graph) => ExecuteRtGiPass(commandBuffer, graph, gameView: true));
                        RenderGraphPass clearGi = _graph.AddGraphicsPassBefore(_rtGameGiPass, "RtGameGiClear", (_, _) => { });
                        _graph.WritesColor(clearGi, _gameGiTarget!, clear: true, Vector4.Zero);
                        _graph.WritesColor(clearGi, _gameRtGiValidity!, clear: true, Vector4.One);
                        clearGi.ShouldExecute = gameVisible;
                        _graph.ReadsSampled(_rtGameGiPass, _gameRtDepth!);
                        _graph.ReadsSampled(_rtGameGiPass, _gameRtNormalRoughness!);
                        _graph.ReadsShaderBindingTable(_rtGameGiPass, _rtGiSbtResource!);
                        _graph.WritesStorage(_rtGameGiPass, _gameGiTarget!);
                        _graph.WritesStorage(_rtGameGiPass, _gameRtGiValidity!);
                        _rtGameGiPass.ShouldExecute = () => gameVisible()
                            && _rtGiPipeline is not null
                            && _lastRtGiActive
                            && _gameRtCanTraceThisFrame;
                    }
                    else
                    {
                        _gameSsgiPass = _graph.AddComputePass("GameScreenSpaceGI", (commandBuffer, graph) =>
                            ExecuteSsgiPass(commandBuffer, graph, _gameHdrTarget!, _gameDepth!, _gameGiTarget!, _ssgiGameAllocation));
                        _graph.ReadsShader(_gameSsgiPass, _gameHdrTarget!, PipelineStages.ComputeShader);
                        _graph.ReadsShader(_gameSsgiPass, _gameDepth!, PipelineStages.ComputeShader);
                        _graph.WritesStorage(_gameSsgiPass, _gameGiTarget!, PipelineStages.ComputeShader);
                    }

                    RenderGraphResource giInput = giViaRt ? _gameRtGiFiltered! : _gameGiTarget!;
                    _gameGiTemporalPass = _graph.AddComputePass("GameGiTemporalResolve", (commandBuffer, graph) =>
                        ExecuteTemporalPass(commandBuffer, graph, giInput, _gameMotionTarget!, _gameDepth!, _gameGiHistoryRead!, _gameGiHistoryWrite!, _gameGiHistory, ScreenSpaceGIHistoryWeight, denoiseGi: giViaRt));
                    if (giViaRt)
                        AddRtGiFilterChain(gameView: true, _gameGiTarget!, _gameRtGiFilterScratch!, _gameRtGiFiltered!, _gameGiTemporalPass);
                    _graph.ReadsShader(_gameGiTemporalPass, giInput, PipelineStages.ComputeShader);
                    _graph.ReadsShader(_gameGiTemporalPass, _gameMotionTarget!, PipelineStages.ComputeShader);
                    _graph.ReadsShader(_gameGiTemporalPass, _gameDepth!, PipelineStages.ComputeShader);
                    _graph.ReadsShader(_gameGiTemporalPass, _gameGiHistoryRead!, PipelineStages.ComputeShader);
                    _graph.WritesStorage(_gameGiTemporalPass, _gameGiHistoryWrite!, PipelineStages.ComputeShader);

                    _gameGiCompositePass = _graph.AddComputePass("GameGiComposite", (commandBuffer, graph) =>
                        ExecuteGiCompositePass(commandBuffer, graph, _gameHistoryWrite!, _gameGiHistoryWrite!, _gameCompositeTarget!));
                    _graph.ReadsShader(_gameGiCompositePass, _gameHistoryWrite!, PipelineStages.ComputeShader);
                    _graph.ReadsShader(_gameGiCompositePass, _gameGiHistoryWrite!, PipelineStages.ComputeShader);
                    _graph.WritesStorage(_gameGiCompositePass, _gameCompositeTarget!, PipelineStages.ComputeShader);
                    gameColor = _gameCompositeTarget!;
                }

                RenderGraphResource gameBloomSource = gameColor;
                gameBloomPass = _graph.AddComputePass("GameBloom", (commandBuffer, graph) => ExecuteBloomPass(commandBuffer, graph, gameBloomSource, _gameBloomTarget!));
                _graph.ReadsShader(gameBloomPass, gameColor, PipelineStages.ComputeShader);
                _graph.WritesStorage(gameBloomPass, _gameBloomTarget!, PipelineStages.ComputeShader);
                if (_rayTracingEnabled && _gameRtRawReflection is not null)
                {
                    AddRtReflectionChain(gameView: true, gameColor, gameBloomPass);
                    gameColor = gameBloomSource = _gameRtCompositeTarget!;
                    _graph.ReadsShader(gameBloomPass, _gameRtCompositeTarget!, PipelineStages.ComputeShader);
                }
                gameBloom = _gameBloomTarget!;
            }

            _gameTonemapPass = _graph.AddGraphicsPass("GameTonemap", (commandBuffer, _) => ExecuteTonemapPass(commandBuffer, _gameTarget!.Description.Extent, gameColor, gameBloom, renderContent: _gameCameraPresent));
            _graph.WritesColor(_gameTonemapPass, _gameTarget!, clear: true, Vector4.Zero);
            _graph.ReadsShader(_gameTonemapPass, gameColor, PipelineStages.FragmentShader);
            _graph.ReadsShader(_gameTonemapPass, gameBloom, PipelineStages.FragmentShader);

            Func<bool> gameSkyVisible = () => (_gameForwardPass!.ShouldExecute?.Invoke() ?? true)
                && _skyPipeline is not null
                && _skyboxMode == SkyboxMode.Procedural;
            RenderGraphPass gameSkyPass = _graph.AddGraphicsPassBefore(gameTemporalPass ?? _gameTonemapPass, "GameProceduralSky",
                (commandBuffer, _) => ExecuteSkyPass(commandBuffer, _gameRenderExtent.Width > 8 ? _gameRenderExtent : sceneExtent, gameView: true));
            gameSkyPass.ShouldExecute = gameSkyVisible;
            _graph.WritesColor(gameSkyPass, _gameHdrTarget!, clear: false);
            _graph.ReadsDepth(gameSkyPass, _gameDepth!);

            RenderGraphPass gameCloudsPass = _graph.AddGraphicsPassBefore(gameTemporalPass ?? _gameTonemapPass, "GameVolumetricClouds",
                (commandBuffer, _) => ExecuteCloudsPass(commandBuffer, _gameRenderExtent.Width > 8 ? _gameRenderExtent : sceneExtent, gameView: true));
            gameCloudsPass.ShouldExecute = () => gameSkyVisible() && CloudsEnabled && _cloudShapeTexture is not null;
            _graph.WritesColor(gameCloudsPass, _gameHdrTarget!, clear: false);
            _graph.ReadsDepth(gameCloudsPass, _gameDepth!);

            _gameForwardPass.ShouldExecute = () => GameViewVisible && _gameCameraPresent;
            if (gameTemporalPass is not null)
                gameTemporalPass.ShouldExecute = _gameForwardPass.ShouldExecute;
            if (_gameSsgiPass is not null)
                _gameSsgiPass.ShouldExecute = _gameForwardPass.ShouldExecute;
            if (_gameGiTemporalPass is not null)
                _gameGiTemporalPass.ShouldExecute = _gameForwardPass.ShouldExecute;
            if (_gameGiCompositePass is not null)
                _gameGiCompositePass.ShouldExecute = _gameForwardPass.ShouldExecute;
            if (gameBloomPass is not null)
                gameBloomPass.ShouldExecute = _gameForwardPass.ShouldExecute;
            _gameTonemapPass.ShouldExecute = () => GameViewVisible;

            RenderGraphPass overlayPass = _graph.AddGraphicsPass("EditorOverlay", (commandBuffer, _) =>
            {
                if (!_skipOverlayThisFrame)
                    OverlayRender?.Invoke(commandBuffer);
            });
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
        shadowPass.ShouldExecute = () => (!_editorMode || SceneViewVisible) && EffectiveShadowMode == ShadowMode.Cascaded;
        _forwardPass.ShouldExecute = () => !_editorMode || SceneViewVisible;
        if (temporalPass is not null)
            temporalPass.ShouldExecute = _forwardPass.ShouldExecute;
        if (_ssgiPass is not null)
            _ssgiPass.ShouldExecute = _forwardPass.ShouldExecute;
        if (_giTemporalPass is not null)
            _giTemporalPass.ShouldExecute = _forwardPass.ShouldExecute;
        if (_giCompositePass is not null)
            _giCompositePass.ShouldExecute = _forwardPass.ShouldExecute;
        if (bloomPass is not null)
            bloomPass.ShouldExecute = _forwardPass.ShouldExecute;
        _tonemapPass.ShouldExecute = _forwardPass.ShouldExecute;
        _graph.ReadsTransfer(presentPass, copySource);
        _graph.WritesTransfer(presentPass, _swapchainTarget!);
    }

    private void ExecuteShadowPass(ICommandBuffer commandBuffer, ShadowData shadow)
    {
        if (shadow.LightIndex < 0 || shadow.LightDirectionAndStrength.W <= 0f)
            return;
        commandBuffer.BindGraphicsPipeline(_shadowPipeline!);
        for (int cascade = 0; cascade < CascadedShadows.CascadeCount; cascade++)
        {
            Rect tile = CascadedShadows.Tile(cascade, ShadowMapSize);
            commandBuffer.SetViewport(new Viewport(tile.X, tile.Y, tile.Width, tile.Height, 0f, 1f));
            commandBuffer.SetScissor(tile);
            foreach (RenderItem renderItem in _sceneItems.Values)
            {
                if (!renderItem.RenderingMode.CastsShadows())
                    continue;
                Matrix4x4 matrix = shadow.LightViewProjections[cascade] * renderItem.ModelMatrix;
                if (!FrustumCulling.IsVisible(renderItem.Mesh.BoundsMin, renderItem.Mesh.BoundsMax, matrix))
                {
                    CulledObjects++;
                    continue;
                }
                DrawnObjects++;
                ShadowDrawnObjects++;
                ShadowDrawnTriangleCount += renderItem.Mesh.TriangleCount;
                ShadowModelConstants constants = new() { ModelViewProjection = matrix };
                commandBuffer.PushConstants(StructuredBytes(constants), _shadowPushStages);
                renderItem.Mesh.Draw(commandBuffer);
            }
        }
    }

    private void ExecuteForwardPass(ICommandBuffer commandBuffer, Size2D extent) =>
        ExecuteForwardPass(commandBuffer, extent, _cameraAllocation);

    private void ExecuteForwardPass(ICommandBuffer commandBuffer, Size2D extent, FrameAllocation cameraAllocation)
    {
        bool gameView = cameraAllocation == _gameCameraAllocation;
        bool rtActive = _lastRtMasterEnabled;
        commandBuffer.BindGraphicsPipeline(rtActive ? _pbrRtPipeline! : _pbrPipeline!);
        commandBuffer.SetViewport(new Viewport(0f, 0f, extent.Width, extent.Height, 0f, 1f));
        commandBuffer.SetScissor(new Rect(0, 0, extent.Width, extent.Height));

        FrameAllocation shadowAllocation = gameView ? _gameShadowAllocation : _shadowAllocation;
        RenderGraphResource shadowDepth = gameView ? _gameShadowDepth! : _shadowDepth!;
        IDescriptorSet frameSet = (rtActive ? _pbrRtProgram : _pbrProgram)!.CreateDescriptorSet(0);
        frameSet.WriteUniformBuffer(0, _uniformRing!.Buffer, cameraAllocation.Offset, (ulong)CameraData.SizeInBytes);
        frameSet.WriteUniformBuffer(1, _uniformRing.Buffer, shadowAllocation.Offset, (ulong)Marshal.SizeOf<ShadowData>());
        frameSet.WriteTexture(2, _graph!.GetTexture(shadowDepth));
        frameSet.WriteSampler(3, _shadowCompareSampler!);
        frameSet.WriteUniformBuffer(4, _uniformRing.Buffer, _lightsAllocation.Offset, (ulong)Marshal.SizeOf<LightsData>());
        frameSet.WriteTexture(5, _iblResources!.Irradiance);
        frameSet.WriteTexture(6, _iblResources.PrefilteredSpecular);
        frameSet.WriteTexture(7, _iblResources.BrdfLut);
        frameSet.WriteSampler(8, _iblResources.Sampler);
        if (rtActive)
        {
            frameSet.WriteTexture(9, _graph.GetTexture(gameView ? _gameRtVisibility! : _rtVisibility!));
            FrameAllocation control = gameView ? _rtGameControlAllocation : _rtSceneControlAllocation;
            frameSet.WriteUniformBuffer(10, _uniformRing.Buffer, control.Offset, (ulong)RtShadowControl.SizeInBytes);
        }
        _frameSets[_activeFrameSlot].Add(frameSet);
        commandBuffer.BindDescriptorSet(frameSet, 0);

        Dictionary<RenderItem, Matrix4x4> previousModels = gameView ? _gamePreviousModels : _scenePreviousModels;
        IReadOnlyList<RayTracingDrawItem> preparedDrawItems = gameView ? _gamePreparedDrawItems : _scenePreparedDrawItems;
        foreach (RayTracingDrawItem preparedDraw in preparedDrawItems)
        {
            RenderItem renderItem = preparedDraw.Item;
            Mesh drawMesh = preparedDraw.Mesh;
            DrawnObjects++;
            if (_editorMode && cameraAllocation != _gameCameraAllocation)
            {
                SceneDrawnObjects++;
                SceneDrawnTriangleCount += drawMesh.TriangleCount;
                SceneLodLevel = Math.Max(SceneLodLevel, FindLodLevel(renderItem.Mesh, drawMesh));
            }
            else
            {
                GameDrawnObjects++;
                GameDrawnTriangleCount += drawMesh.TriangleCount;
                GameLodLevel = Math.Max(GameLodLevel, FindLodLevel(renderItem.Mesh, drawMesh));
            }
            if (renderItem.Material.ParamsDirty)
            {
                _device!.UploadBufferData(renderItem.Material.ParamsBuffer, StructuredBytes(renderItem.Material.ToData()));
                renderItem.Material.ParamsDirty = false;
            }
            commandBuffer.BindDescriptorSet((rtActive ? renderItem.Material.RtSet : renderItem.Material.Set)!, 1);
            Matrix4x4 model = renderItem.ModelMatrix;
            Matrix4x4 previousModel = previousModels.TryGetValue(renderItem, out Matrix4x4 previous) ? previous : model;
            ModelConstants constants = ModelConstants.FromModel(model, previousModel);
            commandBuffer.PushConstants(StructuredBytes(constants), _pbrPushStages);
            drawMesh.Draw(commandBuffer);
            (gameView ? _gameDrawnItems : _sceneDrawnItems).Add(renderItem);
        }
    }

    private void PrepareRayTracingView(
        RayTracingScene? scene,
        RayTracingFrameSlotReadiness? readiness,
        IReadOnlyList<RayTracingDrawItem> drawItems,
        bool gameView)
    {
        if (scene is null || readiness is null)
            return;

        RayTracingScene.FrameResources frame = scene.GetFrame(_activeFrameSlot);
        IAccelerationStructure? previousTopLevel = frame.TopLevel;
        scene.Prepare(_activeFrameSlot, drawItems, prepareMaterials: _rtReflectionsEnabled || _lastRtGiActive);
        if (!ReferenceEquals(previousTopLevel, frame.TopLevel))
            readiness.Invalidate(_activeFrameSlot);

        RenderGraphPass? tracePass = gameView ? _rtGameTracePass : _rtSceneTracePass;
        if (tracePass is null)
            return;
        RenderGraphPass? reflectionTracePass = gameView ? _rtGameReflectionTracePass : _rtSceneReflectionTracePass;
        RenderGraphPass? giPass = gameView ? _rtGameGiPass : _rtSceneGiPass;
        RayTracingBuildGraphSlot buildSlot = EnsureRayTracingBuildGraphSlot(scene, readiness, _activeFrameSlot, tracePass, reflectionTracePass, giPass);
        buildSlot.Enabled = true;
        bool canTrace = readiness.CanTrace(_activeFrameSlot, frame.TopLevel);
        if (ReferenceEquals(scene, _sceneRayTracingScene))
            _sceneRtCanTraceThisFrame = canTrace;
        else if (ReferenceEquals(scene, _gameRayTracingScene))
            _gameRtCanTraceThisFrame = canTrace;
    }

    private void AddRtShadowChain(bool gameView, Size2D extent, RenderGraphPass forward)
    {
        RenderGraphResource normalRoughness = (gameView ? _gameRtNormalRoughness : _rtNormalRoughness)!;
        RenderGraphResource depth = (gameView ? _gameRtDepth : _rtDepth)!;
        RenderGraphResource visibility = (gameView ? _gameRtVisibility : _rtVisibility)!;
        RenderGraphResource validity = (gameView ? _gameRtVisibilityValidity : _rtVisibilityValidity)!;
        string prefix = gameView ? "Game" : "Scene";
        Func<bool> forwardVisible = () => forward.ShouldExecute?.Invoke() ?? true;
        Func<bool> traceReady = () => forwardVisible()
            && _lastRtMasterEnabled
            && (gameView ? _gameRtCanTraceThisFrame : _sceneRtCanTraceThisFrame)
            && (RayTracingReflectionsEnabled || _lastRtGiActive || (ShadowMode == ShadowMode.RayTraced && RtShadowLightActive(gameView ? _currentGameShadowData : _currentShadowData)));

        RenderGraphPass prepass = _graph!.AddGraphicsPassBefore(forward, $"Rt{prefix}Prepass", (commandBuffer, _) => ExecuteRtPrepassPass(commandBuffer, extent, gameView));
        _graph.WritesColor(prepass, normalRoughness, clear: true, Vector4.Zero);
        _graph.WritesDepth(prepass, depth, clear: true, 1f);
        prepass.ShouldExecute = traceReady;

        RenderGraphPass trace = _graph.AddRayTracingPassBefore(forward, $"Rt{prefix}Trace", (commandBuffer, graph) => ExecuteRtTracePass(commandBuffer, graph, gameView));
        _graph.ReadsSampled(trace, depth);
        _graph.ReadsSampled(trace, normalRoughness);
        _graph.ReadsShaderBindingTable(trace, _rtSbt!);
        _graph.WritesStorage(trace, visibility);
        _graph.WritesStorage(trace, validity);
        trace.ShouldExecute = () => traceReady() && ShadowMode == ShadowMode.RayTraced && RtShadowLightActive(gameView ? _currentGameShadowData : _currentShadowData);
        _graph.ReadsShader(forward, visibility);

        if (gameView)
        {
            _rtGamePrepassPass = prepass;
            _rtGameTracePass = trace;
        }
        else
        {
            _rtScenePrepassPass = prepass;
            _rtSceneTracePass = trace;
        }
    }

    private bool RtShadowLightActive(ShadowData shadow) =>
        (shadow.LightIndex >= 0 && shadow.LightDirectionAndStrength.W > 0f) || _rtPointLightIndex >= 0;

    private void AddRtReflectionChain(bool gameView, RenderGraphResource sceneColor, RenderGraphPass insertBefore)
    {
        RenderGraphPass forward = gameView ? _gameForwardPass! : _forwardPass!;
        RenderGraphResource rawReflection = (gameView ? _gameRtRawReflection : _rtRawReflection)!;
        RenderGraphResource filteredReflection = (gameView ? _gameRtFilteredReflection : _rtFilteredReflection)!;
        RenderGraphResource specular = (gameView ? _gameRtSpecular : _rtSpecular)!;
        RenderGraphResource compositeTarget = (gameView ? _gameRtCompositeTarget : _rtCompositeTarget)!;
        RenderGraphResource depth = (gameView ? _gameRtDepth : _rtDepth)!;
        RenderGraphResource normalRoughness = (gameView ? _gameRtNormalRoughness : _rtNormalRoughness)!;
        RenderGraphResource motion = (gameView ? _gameMotionTarget : _motionTarget) ?? normalRoughness;
        RenderGraphResource validity = (gameView ? _gameRtReflectionValidity : _rtReflectionValidity)!;
        string prefix = gameView ? "Game" : "Scene";
        Func<bool> visible = () => forward.ShouldExecute?.Invoke() ?? true;
        Func<bool> reflectionTraceReady = () => visible()
            && _lastRtMasterEnabled
            && RayTracingReflectionsEnabled
            && _rtReflectionPipeline is not null
            && (gameView ? _gameRtCanTraceThisFrame : _sceneRtCanTraceThisFrame);

        if (gameView)
            _rtGameReflectionTracePass = _graph!.AddRayTracingPassBefore(insertBefore, "RtGameReflections", (commandBuffer, graph) => ExecuteRtReflectionTracePass(commandBuffer, graph, gameView: true));
        else
            _rtSceneReflectionTracePass = _graph!.AddRayTracingPassBefore(insertBefore, "RtSceneReflections", (commandBuffer, graph) => ExecuteRtReflectionTracePass(commandBuffer, graph, gameView: false));
        RenderGraphPass reflectionTrace = gameView ? _rtGameReflectionTracePass! : _rtSceneReflectionTracePass!;
        _graph.ReadsSampled(reflectionTrace, depth);
        _graph.ReadsSampled(reflectionTrace, normalRoughness);
        _graph.ReadsSampled(reflectionTrace, (gameView ? _gameRtSpecularResponse : _rtSpecularResponse)!);
        _graph.ReadsShaderBindingTable(reflectionTrace, _rtReflectionSbtResource!);
        _graph.WritesStorage(reflectionTrace, rawReflection);
        _graph.WritesStorage(reflectionTrace, validity);
        reflectionTrace.ShouldExecute = reflectionTraceReady;

        if (gameView)
            _rtGameFilterPass = _graph.AddComputePassBefore(insertBefore, "RtGameFilter", (commandBuffer, graph) => ExecuteRtFilterPass(commandBuffer, graph, gameView: true));
        else
            _rtSceneFilterPass = _graph.AddComputePassBefore(insertBefore, "RtSceneFilter", (commandBuffer, graph) => ExecuteRtFilterPass(commandBuffer, graph, gameView: false));
        RenderGraphPass filter = gameView ? _rtGameFilterPass! : _rtSceneFilterPass!;
        _graph.ReadsSampled(filter, rawReflection);
        _graph.ReadsSampled(filter, depth);
        _graph.ReadsSampled(filter, normalRoughness);
        _graph.ReadsSampled(filter, motion);
        _graph.ReadsSampled(filter, validity);
        _graph.WritesStorage(filter, filteredReflection);
        filter.ShouldExecute = reflectionTraceReady;

        RenderGraphResource reflectionHistoryRead = (gameView ? _gameReflectionHistoryRead : _sceneReflectionHistoryRead)!;
        RenderGraphResource reflectionHistoryWrite = (gameView ? _gameReflectionHistoryWrite : _sceneReflectionHistoryWrite)!;
        RenderGraphPass reflectionTemporal = _graph!.AddComputePassBefore(insertBefore, $"Rt{prefix}ReflectionTemporal", (commandBuffer, graph) =>
            ExecuteTemporalPass(commandBuffer, graph, filteredReflection, motion, depth, reflectionHistoryRead, reflectionHistoryWrite, gameView ? _gameReflectionHistory : _sceneReflectionHistory, 0.85f));
        _graph.ReadsShader(reflectionTemporal, filteredReflection, PipelineStages.ComputeShader);
        _graph.ReadsShader(reflectionTemporal, motion, PipelineStages.ComputeShader);
        _graph.ReadsShader(reflectionTemporal, depth, PipelineStages.ComputeShader);
        _graph.ReadsShader(reflectionTemporal, reflectionHistoryRead, PipelineStages.ComputeShader);
        _graph.WritesStorage(reflectionTemporal, reflectionHistoryWrite, PipelineStages.ComputeShader);
        reflectionTemporal.ShouldExecute = visible;

        if (gameView)
            _rtGameCompositePass = _graph.AddComputePassBefore(insertBefore, "RtGameComposite", (commandBuffer, graph) =>
                ExecuteRtCompositePass(commandBuffer, graph, sceneColor, reflectionHistoryWrite, specular, rawReflection, compositeTarget, gameView: true));
        else
            _rtSceneCompositePass = _graph.AddComputePassBefore(insertBefore, "RtSceneComposite", (commandBuffer, graph) =>
                ExecuteRtCompositePass(commandBuffer, graph, sceneColor, reflectionHistoryWrite, specular, rawReflection, compositeTarget, gameView: false));
        RenderGraphPass composite = gameView ? _rtGameCompositePass! : _rtSceneCompositePass!;
        _graph.ReadsSampled(composite, sceneColor);
        _graph.ReadsSampled(composite, reflectionHistoryWrite);
        _graph.ReadsSampled(composite, specular);
        _graph.ReadsSampled(composite, rawReflection);
        _graph.WritesStorage(composite, compositeTarget);
        composite.ShouldExecute = visible;
    }

    private RayTracingBuildGraphSlot EnsureRayTracingBuildGraphSlot(
        RayTracingScene scene,
        RayTracingFrameSlotReadiness readiness,
        int frameSlot,
        RenderGraphPass tracePass,
        RenderGraphPass? reflectionTracePass,
        RenderGraphPass? giPass)
    {
        RayTracingBuildGraphSlot? buildSlot = _rtBuildGraphSlots.FirstOrDefault(item =>
            ReferenceEquals(item.Scene, scene) && item.FrameSlot == frameSlot);
        RayTracingScene.FrameResources frame = scene.GetFrame(frameSlot);
        IBuffer instances = frame.Instances ?? throw new InvalidOperationException("Ray tracing instance buffer was not prepared.");
        IBuffer scratch = frame.Scratch ?? throw new InvalidOperationException("Ray tracing scratch buffer was not prepared.");
        IAccelerationStructure topLevel = frame.TopLevel ?? throw new InvalidOperationException("Ray tracing TLAS was not prepared.");
        RenderGraphMemoryState instanceState = new(PipelineStages.Transfer, ResourceAccess.TransferWrite);
        RenderGraphMemoryState scratchState = new(PipelineStages.AccelerationStructureBuild,
            ResourceAccess.AccelerationStructureRead | ResourceAccess.AccelerationStructureWrite);
        RenderGraphMemoryState topLevelState = new(PipelineStages.AccelerationStructureBuild, ResourceAccess.AccelerationStructureWrite);

        if (buildSlot is null)
        {
            buildSlot = new RayTracingBuildGraphSlot(scene, readiness, frameSlot)
            {
                Instances = _graph!.ImportBuffer($"RtInstances.{_rtBuildGraphSlots.Count}", instances, instanceState),
                Scratch = _graph.ImportBuffer($"RtScratch.{_rtBuildGraphSlots.Count}", scratch, scratchState),
                TopLevel = _graph.ImportAccelerationStructure($"RtTopLevel.{_rtBuildGraphSlots.Count}", topLevel, topLevelState),
            };
            buildSlot.Pass = _graph.AddAccelerationStructureBuildPassBefore(tracePass, $"RtTopLevelBuild.{_rtBuildGraphSlots.Count}", (commandBuffer, _) =>
            {
                buildSlot.Scene.BuildTopLevel(commandBuffer, buildSlot.FrameSlot);
                buildSlot.Recorded = true;
            });
            _graph.ReadsAccelerationStructureBuildInput(buildSlot.Pass, buildSlot.Instances);
            _graph.UsesAccelerationStructureScratch(buildSlot.Pass, buildSlot.Scratch);
            _graph.WritesAccelerationStructure(buildSlot.Pass, buildSlot.TopLevel);
            _graph.ReadsAccelerationStructure(tracePass, buildSlot.TopLevel);
            if (reflectionTracePass is not null)
                _graph.ReadsAccelerationStructure(reflectionTracePass, buildSlot.TopLevel);
            if (giPass is not null)
                _graph.ReadsAccelerationStructure(giPass, buildSlot.TopLevel);
            buildSlot.Pass.ShouldExecute = () => buildSlot.Enabled && _activeFrameSlot == buildSlot.FrameSlot;
            _rtBuildGraphSlots.Add(buildSlot);
        }
        else
        {
            _graph!.RefreshImportedBufferShape(buildSlot.Instances, instances, instanceState);
            _graph.RefreshImportedBufferShape(buildSlot.Scratch, scratch, scratchState);
            _graph.RefreshImportedAccelerationStructureShape(buildSlot.TopLevel, topLevel, topLevelState);
        }
        return buildSlot;
    }

    private void ExecuteRtPrepassPass(ICommandBuffer commandBuffer, Size2D extent, bool gameView)
    {
        commandBuffer.BindGraphicsPipeline(_rtPrepassPipeline!);
        commandBuffer.SetViewport(new Viewport(0f, 0f, extent.Width, extent.Height, 0f, 1f));
        commandBuffer.SetScissor(new Rect(0, 0, extent.Width, extent.Height));
        FrameAllocation cameraAllocation = gameView ? _gameCameraAllocation : _cameraAllocation;
        IDescriptorSet cameraSet = _rtPrepassProgram!.CreateDescriptorSet(0);
        cameraSet.WriteUniformBuffer(0, _uniformRing!.Buffer, cameraAllocation.Offset, (ulong)CameraData.SizeInBytes);
        _frameSets[_activeFrameSlot].Add(cameraSet);
        commandBuffer.BindDescriptorSet(cameraSet, 0);
        IReadOnlyList<RayTracingDrawItem> preparedDrawItems = gameView ? _gamePreparedDrawItems : _scenePreparedDrawItems;
        foreach (RayTracingDrawItem preparedDraw in preparedDrawItems)
        {
            RenderItem renderItem = preparedDraw.Item;
            if (renderItem.Material.ParamsDirty)
            {
                _device!.UploadBufferData(renderItem.Material.ParamsBuffer, StructuredBytes(renderItem.Material.ToData()));
                renderItem.Material.ParamsDirty = false;
            }
            if (renderItem.Material.RtPrepassSet is not null)
                commandBuffer.BindDescriptorSet(renderItem.Material.RtPrepassSet, 1);
            commandBuffer.PushConstants(StructuredBytes(ModelConstants.FromModel(renderItem.ModelMatrix)), _rtPrepassPushStages);
            preparedDraw.Mesh.Draw(commandBuffer);
        }
    }

    private void ExecuteRtTracePass(ICommandBuffer commandBuffer, RenderGraph graph, bool gameView)
    {
        RayTracingScene? scene = gameView ? _gameRayTracingScene : _sceneRayTracingScene;
        if (scene is null || _rtShadowPipeline is null || _rtShadowSbt is null)
            throw new InvalidOperationException("RT shadow resources are not initialized.");
        RayTracingScene.FrameResources frame = scene.GetFrame(_activeFrameSlot);
        if (frame.TopLevel is null)
            throw new InvalidOperationException("Ray tracing pass has no prepared TLAS.");
        FrameAllocation viewAllocation = gameView ? _rtGameViewAllocation : _rtSceneViewAllocation;
        RenderGraphResource depth = (gameView ? _gameRtDepth : _rtDepth)!;
        RenderGraphResource normalRoughness = (gameView ? _gameRtNormalRoughness : _rtNormalRoughness)!;
        RenderGraphResource visibility = (gameView ? _gameRtVisibility : _rtVisibility)!;
        RenderGraphResource validity = (gameView ? _gameRtVisibilityValidity : _rtVisibilityValidity)!;
        IDescriptorSet set = _rtShadowProgram!.CreateDescriptorSet(0);
        set.WriteUniformBuffer(0, _uniformRing!.Buffer, viewAllocation.Offset, (ulong)RtViewData.SizeInBytes);
        set.WriteAccelerationStructure(1, frame.TopLevel);
        set.WriteTexture(2, graph.GetTexture(depth));
        set.WriteTexture(3, graph.GetTexture(normalRoughness));
        set.WriteStorageImage(5, graph.GetTexture(visibility));
        set.WriteStorageImage(6, graph.GetTexture(validity));
        _frameSets[_activeFrameSlot].Add(set);
        commandBuffer.BindRayTracingPipeline(_rtShadowPipeline);
        commandBuffer.BindDescriptorSet(set, 0);
        Size2D extent = RayTracingLogicalExtent(gameView, RayTracingQualityEffect.Shadows);
        commandBuffer.TraceRays(_rtShadowSbt, (uint)extent.Width, (uint)extent.Height, 1);
    }

    private void ExecuteRtReflectionTracePass(ICommandBuffer commandBuffer, RenderGraph graph, bool gameView)
    {
        if (_rtReflectionPipeline is null || _rtReflectionSbt is null)
            throw new InvalidOperationException("RT reflection resources are not initialized.");
        RayTracingScene? scene = gameView ? _gameRayTracingScene : _sceneRayTracingScene;
        if (scene is null)
            throw new InvalidOperationException("RT scene is not initialized.");
        RayTracingScene.FrameResources frame = scene.GetFrame(_activeFrameSlot);
        if (frame.TopLevel is null)
            throw new InvalidOperationException("Ray tracing pass has no prepared TLAS.");
        FrameAllocation viewAllocation = gameView ? _rtGameViewAllocation : _rtSceneViewAllocation;
        RenderGraphResource depth = (gameView ? _gameRtDepth : _rtDepth)!;
        RenderGraphResource normalRoughness = (gameView ? _gameRtNormalRoughness : _rtNormalRoughness)!;
        RenderGraphResource specular = (gameView ? _gameRtSpecular : _rtSpecular)!;
        RenderGraphResource rawReflection = (gameView ? _gameRtRawReflection : _rtRawReflection)!;
        RenderGraphResource validity = (gameView ? _gameRtReflectionValidity : _rtReflectionValidity)!;
        IDescriptorSet set = _rtReflectionProgram!.CreateDescriptorSet(0);
        set.WriteUniformBuffer(0, _uniformRing!.Buffer, viewAllocation.Offset, (ulong)RtViewData.SizeInBytes);
        set.WriteAccelerationStructure(1, frame.TopLevel);
        set.WriteTexture(2, graph.GetTexture(depth));
        set.WriteTexture(3, graph.GetTexture(normalRoughness));
        set.WriteStorageImage(4, graph.GetTexture(rawReflection));
        set.WriteUniformBuffer(5, _uniformRing.Buffer, _lightsAllocation.Offset, (ulong)Marshal.SizeOf<LightsData>());
        set.WriteTexture(6, _iblResources!.Irradiance);
        set.WriteTexture(7, _iblResources.PrefilteredSpecular);
        set.WriteTexture(8, _iblResources.BrdfLut);
        set.WriteSampler(9, _iblResources.Sampler);
        set.WriteTexture(10, graph.GetTexture((gameView ? _gameRtSpecularResponse : _rtSpecularResponse)!));
        set.WriteStorageImage(11, graph.GetTexture(validity));
        _frameSets[_activeFrameSlot].Add(set);
        if (frame.Surfaces is null || frame.Vertices is null || frame.Indices is null)
            throw new InvalidOperationException("Ray tracing shading buffers were not prepared for the active frame slot.");
        IDescriptorSet shadingSet = _rtReflectionProgram.CreateDescriptorSet(1);
        shadingSet.WriteStorageBuffer(0, frame.Surfaces);
        shadingSet.WriteStorageBuffer(1, frame.Vertices);
        shadingSet.WriteStorageBuffer(2, frame.Indices);
        List<ITexture> textureArray = [.. frame.Textures];
        if (textureArray.Count == 0)
            textureArray.Add(_defaultMaterialTextures!.WhiteLinear);
        shadingSet.WriteTextureArray(3, textureArray);
        shadingSet.WriteSampler(4, _linearSampler!);
        _frameSets[_activeFrameSlot].Add(shadingSet);
        commandBuffer.BindRayTracingPipeline(_rtReflectionPipeline);
        commandBuffer.BindDescriptorSet(set, 0);
        commandBuffer.BindDescriptorSet(shadingSet, 1);
        Size2D extent = RayTracingLogicalExtent(gameView, RayTracingQualityEffect.Reflections);
        commandBuffer.TraceRays(_rtReflectionSbt, (uint)extent.Width, (uint)extent.Height, 1);
    }

    private void ExecuteRtGiPass(ICommandBuffer commandBuffer, RenderGraph graph, bool gameView)
    {
        if (_rtGiPipeline is null || _rtGiSbt is null)
            throw new InvalidOperationException("RT GI resources are not initialized.");
        RayTracingScene? scene = gameView ? _gameRayTracingScene : _sceneRayTracingScene;
        if (scene is null)
            throw new InvalidOperationException("RT scene is not initialized.");
        RayTracingScene.FrameResources frame = scene.GetFrame(_activeFrameSlot);
        if (frame.TopLevel is null || frame.Surfaces is null || frame.Vertices is null || frame.Indices is null)
            throw new InvalidOperationException("RT GI scene buffers are not prepared.");
        FrameAllocation viewAllocation = gameView ? _rtGameViewAllocation : _rtSceneViewAllocation;
        RenderGraphResource depth = (gameView ? _gameRtDepth : _rtDepth)!;
        RenderGraphResource normalRoughness = (gameView ? _gameRtNormalRoughness : _rtNormalRoughness)!;
        RenderGraphResource giTarget = gameView ? _gameGiTarget! : _giTarget!;
        RenderGraphResource validity = (gameView ? _gameRtGiValidity : _rtGiValidity)!;
        IDescriptorSet set = _rtGiProgram!.CreateDescriptorSet(0);
        set.WriteUniformBuffer(0, _uniformRing!.Buffer, viewAllocation.Offset, (ulong)RtViewData.SizeInBytes);
        set.WriteAccelerationStructure(1, frame.TopLevel);
        set.WriteTexture(2, graph.GetTexture(depth));
        set.WriteTexture(3, graph.GetTexture(normalRoughness));
        set.WriteStorageImage(4, graph.GetTexture(giTarget));
        set.WriteStorageImage(7, graph.GetTexture(validity));
        set.WriteTexture(5, _iblResources!.Irradiance);
        set.WriteSampler(6, _iblResources.Sampler);
        _frameSets[_activeFrameSlot].Add(set);
        IDescriptorSet shadingSet = _rtGiProgram.CreateDescriptorSet(1);
        shadingSet.WriteStorageBuffer(0, frame.Surfaces);
        shadingSet.WriteStorageBuffer(1, frame.Vertices);
        shadingSet.WriteStorageBuffer(2, frame.Indices);
        List<ITexture> textureArray = [.. frame.Textures];
        if (textureArray.Count == 0)
            textureArray.Add(_defaultMaterialTextures!.WhiteLinear);
        shadingSet.WriteTextureArray(3, textureArray);
        shadingSet.WriteSampler(4, _linearSampler!);
        _frameSets[_activeFrameSlot].Add(shadingSet);
        commandBuffer.BindRayTracingPipeline(_rtGiPipeline);
        commandBuffer.BindDescriptorSet(set, 0);
        commandBuffer.BindDescriptorSet(shadingSet, 1);
        Size2D extent = RayTracingLogicalExtent(gameView, RayTracingQualityEffect.Gi);
        commandBuffer.TraceRays(_rtGiSbt, (uint)extent.Width, (uint)extent.Height, 1);
    }


    private void AddRtGiFilterChain(bool gameView, RenderGraphResource source, RenderGraphResource scratch, RenderGraphResource destination, RenderGraphPass insertBefore)
    {
        RenderGraphPass forward = (gameView ? _gameForwardPass : _forwardPass)!;
        RenderGraphResource depth = (gameView ? _gameRtDepth : _rtDepth)!;
        RenderGraphResource normalRoughness = (gameView ? _gameRtNormalRoughness : _rtNormalRoughness)!;
        RenderGraphResource motion = (gameView ? _gameMotionTarget : _motionTarget)!;
        RenderGraphResource giValidity = (gameView ? _gameRtGiValidity : _rtGiValidity)!;
        string prefix = gameView ? "Game" : "Scene";
        RenderGraphResource input = source;
        for (uint iteration = 0; iteration < 2; iteration++)
        {
            RenderGraphResource filterInput = input;
            RenderGraphResource filterOutput = iteration == 0 ? scratch : destination;
            uint stepWidth = 1u << (int)iteration;
            RenderGraphPass filter = _graph!.AddComputePassBefore(insertBefore, $"Rt{prefix}GiFilter.{iteration}", (commandBuffer, graph) =>
                ExecuteRtFilterPass(commandBuffer, graph, filterInput, filterOutput, depth, normalRoughness, motion, giValidity, mode: 2u, stepWidth, validityEnabled: true,
                    logicalSourceExtent: iteration == 0 ? RayTracingLogicalExtent(gameView, RayTracingQualityEffect.Gi) : null));
            _graph.ReadsSampled(filter, filterInput);
            _graph.ReadsSampled(filter, depth);
            _graph.ReadsSampled(filter, normalRoughness);
            _graph.ReadsSampled(filter, motion);
            _graph.ReadsSampled(filter, giValidity);
            _graph.WritesStorage(filter, filterOutput);
            filter.ShouldExecute = () => forward.ShouldExecute?.Invoke() ?? true;
            input = filterOutput;
        }
    }

    private void ExecuteRtFilterPass(ICommandBuffer commandBuffer, RenderGraph graph, bool gameView)
    {
        ExecuteRtFilterPass(commandBuffer, graph,
            (gameView ? _gameRtRawReflection : _rtRawReflection)!,
            (gameView ? _gameRtFilteredReflection : _rtFilteredReflection)!,
            (gameView ? _gameRtDepth : _rtDepth)!,
            (gameView ? _gameRtNormalRoughness : _rtNormalRoughness)!,
            (gameView ? _gameMotionTarget : _motionTarget) ?? (gameView ? _gameRtNormalRoughness : _rtNormalRoughness)!,
            (gameView ? _gameRtReflectionValidity : _rtReflectionValidity)!, mode: 1u, stepWidth: 1u, validityEnabled: true,
            logicalSourceExtent: RayTracingLogicalExtent(gameView, RayTracingQualityEffect.Reflections));
    }

    private void ExecuteRtFilterPass(
        ICommandBuffer commandBuffer,
        RenderGraph graph,
        RenderGraphResource source,
        RenderGraphResource destination,
        RenderGraphResource depth,
        RenderGraphResource normalRoughness,
        RenderGraphResource motion,
        RenderGraphResource validity,
        uint mode,
        uint stepWidth,
        bool validityEnabled,
        bool useSceneDepth = false,
        Size2D? logicalSourceExtent = null)
    {
        IDescriptorSet set = _rtFilterProgram!.CreateDescriptorSet(0);
        set.WriteTexture(0, graph.GetTexture(source));
        set.WriteTexture(1, graph.GetTexture(depth));
        set.WriteTexture(2, graph.GetTexture(normalRoughness));
        set.WriteTexture(3, graph.GetTexture(motion));
        set.WriteTexture(4, graph.GetTexture(normalRoughness));
        set.WriteTexture(5, graph.GetTexture(normalRoughness));
        set.WriteStorageImage(6, graph.GetTexture(destination));
        set.WriteTexture(7, graph.GetTexture(validity));
        _frameSets[_activeFrameSlot].Add(set);
        commandBuffer.BindComputePipeline(_rtFilterPipeline!);
        commandBuffer.BindDescriptorSet(set, 0);
        Size2D extent = destination.Description.Extent;
        Size2D sourceExtent = logicalSourceExtent ?? source.Description.Extent;
        RtFilterConstants constants = new()
        {
            ExtentWidth = (uint)extent.Width,
            ExtentHeight = (uint)extent.Height,
            SourceWidth = (uint)sourceExtent.Width,
            SourceHeight = (uint)sourceExtent.Height,
            HistoryValid = 0,
            Mode = mode,
            HistoryWeight = 0f,
            DepthThreshold = 0.01f,
            NormalThreshold = 0.85f,
            StepWidth = stepWidth,
            ValidityEnabled = validityEnabled ? 1u : 0u,
            UseSceneDepth = useSceneDepth ? 1u : 0u,
        };
        commandBuffer.PushConstants(StructuredBytes(constants), _rtFilterPushStages);
        commandBuffer.Dispatch((uint)(extent.Width + 7) / 8, (uint)(extent.Height + 7) / 8);
    }

    private void ExecuteRtCompositePass(
        ICommandBuffer commandBuffer,
        RenderGraph graph,
        RenderGraphResource sceneColor,
        RenderGraphResource filteredReflection,
        RenderGraphResource specular,
        RenderGraphResource rawReflection,
        RenderGraphResource destination,
        bool gameView)
    {
        bool reflectionsActive = RayTracingReflectionsEnabled
            && _lastRtMasterEnabled
            && _rtReflectionPipeline is not null
            && (gameView ? _gameRtCanTraceThisFrame : _sceneRtCanTraceThisFrame);
        IDescriptorSet set = _rtCompositeProgram!.CreateDescriptorSet(0);
        set.WriteTexture(0, graph.GetTexture(sceneColor));
        set.WriteTexture(1, graph.GetTexture(filteredReflection));
        set.WriteTexture(2, graph.GetTexture(specular));
        set.WriteTexture(3, graph.GetTexture(rawReflection));
        set.WriteStorageImage(4, graph.GetTexture(destination));
        _frameSets[_activeFrameSlot].Add(set);
        commandBuffer.BindComputePipeline(_rtCompositePipeline!);
        commandBuffer.BindDescriptorSet(set, 0);
        Size2D extent = destination.Description.Extent;
        Size2D reflectionExtent = RayTracingLogicalExtent(gameView, RayTracingQualityEffect.Reflections);
        RtCompositeConstants constants = new()
        {
            ExtentWidth = (uint)extent.Width,
            ExtentHeight = (uint)extent.Height,
            SourceWidth = (uint)reflectionExtent.Width,
            SourceHeight = (uint)reflectionExtent.Height,
            ReflectionsEnabled = reflectionsActive ? 1u : 0u,
            Strength = RayTracingReflectionStrength,
        };
        commandBuffer.PushConstants(StructuredBytes(constants), _rtCompositePushStages);
        commandBuffer.Dispatch((uint)(extent.Width + 7) / 8, (uint)(extent.Height + 7) / 8);
    }

    private RtViewData BuildRtViewData(Camera camera, Matrix4x4 viewProjection, Matrix4x4 previousViewProjection, uint frameIndex, ShadowData shadow, int pointLightIndex, bool gameView = false)
    {
        Vector3 lightDirection = Vector3.Zero;
        if (shadow.LightIndex >= 0 && shadow.LightIndex < _activeLights.Count)
            lightDirection = _activeLights[shadow.LightIndex].DirectionOrPosition;
        Vector4 pointLight = default;
        if (pointLightIndex >= 0 && pointLightIndex < _activeLights.Count)
        {
            Light light = _activeLights[pointLightIndex];
            pointLight = new Vector4(light.DirectionOrPosition, light.Range);
        }
        Size2D viewExtent = gameView && _gameRenderExtent.Width > 8 ? _gameRenderExtent : _lastTargetExtent;
        return new RtViewData
        {
            ViewProjection = viewProjection,
            InverseViewProjection = viewProjection.Invert(),
            PreviousViewProjection = previousViewProjection,
            CameraPositionAndMaxDistance = new Vector4(camera.Position, MathF.Min(camera.FarPlane, _rtMaxDistance)),
            ExtentAndFrame = new Vector4(viewExtent.Width, viewExtent.Height, frameIndex, SkyDepthThreshold),
            LightDirectionAndAngularRadius = new Vector4(lightDirection, _shadowSettings.AngularRadius),
            Settings = new Vector4(_rtRoughnessLimit, _rtRayBias, (uint)_rtSamples, _rtReflectionStrength),
            PointLightPositionAndRange = pointLight,
            ShadowQuality = RayTracingQualityVector(gameView ? RayTracingQualityCamera.Game : RayTracingQualityCamera.Scene, RayTracingQualityEffect.Shadows),
            ReflectionQuality = RayTracingQualityVector(gameView ? RayTracingQualityCamera.Game : RayTracingQualityCamera.Scene, RayTracingQualityEffect.Reflections),
            GiQuality = RayTracingQualityVector(gameView ? RayTracingQualityCamera.Game : RayTracingQualityCamera.Scene, RayTracingQualityEffect.Gi),
        };
    }

    private Vector4 RayTracingQualityVector(RayTracingQualityCamera camera, RayTracingQualityEffect effect)
    {
        (_, RayTracingQualityProfile profile, _) = GetRayTracingQuality(camera, effect);
        uint samples = Math.Clamp(profile.Samples, 1u, (uint)Math.Clamp(_rtSamples, 1, 8));
        return new Vector4(samples, _rtMaxDistance * 0.3f, profile.FarTraceRate, profile.ResolutionScale);
    }

    private Size2D RayTracingLogicalExtent(bool gameView, RayTracingQualityEffect effect)
    {
        Size2D full = effect switch
        {
            RayTracingQualityEffect.Shadows => (gameView ? _gameRtVisibility : _rtVisibility)!.Description.Extent,
            RayTracingQualityEffect.Reflections => (gameView ? _gameRtRawReflection : _rtRawReflection)!.Description.Extent,
            _ => (gameView ? _gameGiTarget : _giTarget)!.Description.Extent,
        };
        return full;
    }

    private void WriteRtShadowControl(FrameAllocation allocation, bool canTrace, ShadowData shadow, Size2D extent)
    {
        bool shadowsActive = canTrace && _lastRtMasterEnabled && ShadowMode == ShadowMode.RayTraced;
        RtShadowControl control = new()
        {
            Enabled = shadowsActive && shadow.LightIndex >= 0 && shadow.LightDirectionAndStrength.W > 0f ? 1u : 0u,
            ExtentWidth = (uint)extent.Width,
            ExtentHeight = (uint)extent.Height,
            PointLightEnabled = shadowsActive && _rtPointLightIndex >= 0 ? 1u : 0u,
            PointLightIndex = unchecked((uint)_rtPointLightIndex),
            RasterDisabled = _lastRtMasterEnabled && ShadowMode != ShadowMode.Cascaded ? 1u : 0u,
        };
        _uniformRing!.Write(allocation, StructuredBytes(control));
    }

    private void PrepareViewDrawItems(List<RayTracingDrawItem> drawItems, List<RayTracingDrawItem> rtItems, Camera camera, Size2D extent)
    {
        Matrix4x4 viewProjection = camera.ViewProjection;
        foreach (RenderItem renderItem in _sceneItems.Values)
        {
            if (renderItem.RenderingMode == MeshRenderingMode.Off)
                continue;
            MeshLodState lodState = ReferenceEquals(camera, Camera) && _editorMode
                ? renderItem.GameLodState
                : renderItem.SceneLodState;
            Mesh drawMesh = SelectLodMesh(renderItem, lodState, camera, extent);
            RayTracingDrawItem draw = new(renderItem, drawMesh);
            if (_lastRtMasterEnabled)
                rtItems.Add(draw);
            if (!renderItem.RenderingMode.RendersCamera())
                continue;
            if (!FrustumCulling.IsVisible(renderItem.Mesh.BoundsMin, renderItem.Mesh.BoundsMax, viewProjection * renderItem.ModelMatrix))
            {
                CulledObjects++;
                continue;
            }
            drawItems.Add(draw);
        }
    }

    private Mesh SelectLodMesh(RenderItem renderItem, MeshLodState state, Camera camera, Size2D extent)
    {
        Mesh mesh = renderItem.Mesh;
        if (mesh.LodCount <= 1)
            return mesh;
        Matrix4x4 modelMatrix = renderItem.ModelMatrix;
        float pixelsPerUnit = MeshLodProjection.PixelsPerUnit(camera, extent, mesh, modelMatrix);
        float pixelBudget = float.IsFinite(LodPixelBudget) && LodPixelBudget > 0f ? LodPixelBudget : 320f;
        int level = state.Select(mesh, renderItem.Lod, pixelsPerUnit, 1f, pixelBudget);
        return mesh.GetLod(level);
    }

    private static int FindLodLevel(Mesh source, Mesh selected)
    {
        for (int level = 0; level < source.LodCount; level++)
            if (ReferenceEquals(source.GetLod(level), selected))
                return level;
        return 0;
    }

    private void ExecuteTemporalPass(
        ICommandBuffer commandBuffer,
        RenderGraph graph,
        RenderGraphResource color,
        RenderGraphResource motion,
        RenderGraphResource depth,
        RenderGraphResource historyRead,
        RenderGraphResource historyWrite,
        TemporalHistory history,
        float historyWeight = 0.9f,
        bool denoiseGi = false)
    {
        IDescriptorSet set = (denoiseGi ? _rtGiTemporalProgram : _temporalProgram)!.CreateDescriptorSet(0);
        set.WriteTexture(0, graph.GetTexture(color));
        set.WriteTexture(1, graph.GetTexture(motion));
        set.WriteTexture(2, graph.GetTexture(depth));
        set.WriteTexture(3, graph.GetTexture(historyRead));
        set.WriteStorageImage(4, graph.GetTexture(historyWrite));
        set.WriteSampler(5, _linearSampler!);
        _frameSets[_activeFrameSlot].Add(set);
        commandBuffer.BindComputePipeline((denoiseGi ? _rtGiTemporalPipeline : _temporalPipeline)!);
        commandBuffer.BindDescriptorSet(set, 0);
        Size2D extent = historyWrite.Description.Extent;
        Size2D sourceExtent = color.Description.Extent;
        TemporalConstants constants = new()
        {
            Width = (uint)extent.Width,
            Height = (uint)extent.Height,
            SourceWidth = (uint)sourceExtent.Width,
            SourceHeight = (uint)sourceExtent.Height,
            HistoryValid = history.IsValid ? 1u : 0u,
            HistoryWeight = historyWeight,
            DepthThreshold = 0.02f,
        };
        commandBuffer.PushConstants(StructuredBytes(constants), ShaderStages.Compute);
        commandBuffer.Dispatch((uint)(extent.Width + 7) / 8, (uint)(extent.Height + 7) / 8);
    }

    private void ExecuteSsgiPass(
        ICommandBuffer commandBuffer,
        RenderGraph graph,
        RenderGraphResource color,
        RenderGraphResource depth,
        RenderGraphResource giOutput,
        FrameAllocation ssgiAllocation)
    {
        IDescriptorSet set = _ssgiProgram!.CreateDescriptorSet(0);
        set.WriteUniformBuffer(0, _uniformRing!.Buffer, ssgiAllocation.Offset, (ulong)SsgiData.SizeInBytes);
        set.WriteTexture(1, graph.GetTexture(color));
        set.WriteTexture(2, graph.GetTexture(depth));
        set.WriteStorageImage(3, graph.GetTexture(giOutput));
        _frameSets[_activeFrameSlot].Add(set);
        commandBuffer.BindComputePipeline(_ssgiPipeline!);
        commandBuffer.BindDescriptorSet(set, 0);
        Size2D extent = giOutput.Description.Extent;
        commandBuffer.Dispatch((uint)(extent.Width + 7) / 8, (uint)(extent.Height + 7) / 8);
    }

    private void ExecuteGiCompositePass(
        ICommandBuffer commandBuffer,
        RenderGraph graph,
        RenderGraphResource sceneColor,
        RenderGraphResource giColor,
        RenderGraphResource destination)
    {
        IDescriptorSet set = _giCompositeProgram!.CreateDescriptorSet(0);
        set.WriteTexture(0, graph.GetTexture(sceneColor));
        set.WriteTexture(1, graph.GetTexture(giColor));
        set.WriteStorageImage(2, graph.GetTexture(destination));
        _frameSets[_activeFrameSlot].Add(set);
        commandBuffer.BindComputePipeline(_giCompositePipeline!);
        commandBuffer.BindDescriptorSet(set, 0);
        Size2D extent = destination.Description.Extent;
        GiCompositeConstants constants = new()
        {
            Width = (uint)extent.Width,
            Height = (uint)extent.Height,
            Intensity = ScreenSpaceGIIntensity,
        };
        commandBuffer.PushConstants(StructuredBytes(constants), ShaderStages.Compute);
        commandBuffer.Dispatch((uint)(extent.Width + 7) / 8, (uint)(extent.Height + 7) / 8);
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

    private void UploadDebugLines()
    {
        Span<DebugLineVertex> span = DebugDraw.GetSpan();
        _debugLinesVertexCount = (uint)span.Length;
        ulong bytes = (ulong)(span.Length * Marshal.SizeOf<DebugLineVertex>());
        if (_debugLinesBuffer is null || _debugLinesBufferCapacity < bytes)
        {
            _debugLinesBuffer?.Dispose();
            ulong capacity = Math.Max(bytes, 262144UL);
            _debugLinesBuffer = _device!.CreateBuffer(new BufferDescription
            {
                Size = capacity,
                Usage = BufferUsage.Vertex | BufferUsage.TransferDestination,
                Memory = MemoryUsage.CpuToGpu,
            });
            _debugLinesBufferCapacity = capacity;
        }
        _device!.UploadBufferData(_debugLinesBuffer, MemoryMarshal.AsBytes(span), 0);
    }

    private void ExecuteColliderGizmoPass(ICommandBuffer commandBuffer, Size2D extent)
    {
        if (_debugLinesPipeline is null || _debugLinesVertexCount == 0 || _debugLinesBuffer is null)
            return;
        Matrix4x4 viewProjection = EditorCamera.ViewProjection;
        commandBuffer.BindGraphicsPipeline(_debugLinesPipeline);
        commandBuffer.SetViewport(new Viewport(0f, 0f, extent.Width, extent.Height, 0f, 1f));
        commandBuffer.SetScissor(new Rect(0, 0, (int)extent.Width, (int)extent.Height));
        commandBuffer.PushConstants(MemoryMarshal.AsBytes(new ReadOnlySpan<Matrix4x4>(ref viewProjection)), ShaderStages.Vertex);
        commandBuffer.BindVertexBuffer(0, _debugLinesBuffer);
        commandBuffer.Draw(_debugLinesVertexCount);
    }

    private void CreateCloudResources()
    {
        if (_rhi!.Backend != GraphicsBackend.Vulkan)
            return;
        byte[] shape;
        byte[] detail;
        byte[] weather;
        byte[]? cached = TryLoadCloudNoiseCache();
        if (cached is not null)
        {
            shape = cached[..(CloudShapeSize * CloudShapeSize * CloudShapeSize * 4)];
            int detailOffset = shape.Length;
            detail = cached[detailOffset..(detailOffset + CloudDetailSize * CloudDetailSize * CloudDetailSize * 4)];
            int weatherOffset = detailOffset + detail.Length;
            weather = cached[weatherOffset..];
        }
        else
        {
            _log?.Information("Generating volumetric cloud noise textures…");
            VolumetricCloudNoise.NoiseData noise = VolumetricCloudNoise.Generate(CloudShapeSize, CloudDetailSize, CloudWeatherSize);
            shape = noise.Shape;
            detail = noise.Detail;
            weather = noise.Weather;
            TrySaveCloudNoiseCache(shape, detail, weather);
        }

        _cloudShapeTexture = CreateCloudTexture3D(CloudShapeSize, shape, "CloudShapeNoise");
        _cloudDetailTexture = CreateCloudTexture3D(CloudDetailSize, detail, "CloudDetailNoise");
        _cloudWeatherTexture = _device!.CreateTexture(new TextureDescription
        {
            Extent = new Size2D(CloudWeatherSize, CloudWeatherSize),
            Format = TextureFormat.R8G8B8A8UNorm,
            Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
        });
        _device.UploadTextureSubresources(_cloudWeatherTexture,
        [
            new TextureSubresourceData { MipLevel = 0, ArrayLayer = 0, Data = weather },
        ]);
    }

    private ITexture CreateCloudTexture3D(int size, byte[] data, string name)
    {
        ITexture texture = _device!.CreateTexture(new TextureDescription
        {
            Extent = new Size2D(size, size),
            Format = TextureFormat.R8G8B8A8UNorm,
            Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
            Dimension = TextureDimension.Texture3D,
            Depth = (uint)size,
        });
        _device.UploadTextureSubresources(texture,
        [
            new TextureSubresourceData { MipLevel = 0, ArrayLayer = 0, Data = data },
        ]);
        return texture;
    }

    private byte[]? TryLoadCloudNoiseCache()
    {
        if (string.IsNullOrEmpty(CloudsCacheDirectory))
            return null;
        try
        {
            string path = Path.Combine(CloudsCacheDirectory, "clouds_noise.bin");
            if (!File.Exists(path))
                return null;
            byte[] bytes = File.ReadAllBytes(path);
            int expected = 16 + (CloudShapeSize * CloudShapeSize * CloudShapeSize * 4)
                + (CloudDetailSize * CloudDetailSize * CloudDetailSize * 4)
                + (CloudWeatherSize * CloudWeatherSize * 4);
            if (bytes.Length != expected
                || bytes[0] != (byte)'E' || bytes[1] != (byte)'C' || bytes[2] != (byte)'L' || bytes[3] != (byte)'D'
                || BitConverter.ToInt32(bytes, 4) != 10
                || BitConverter.ToInt32(bytes, 8) != CloudShapeSize
                || BitConverter.ToInt32(bytes, 12) != CloudDetailSize)
                return null;
            return bytes[16..];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void TrySaveCloudNoiseCache(byte[] shape, byte[] detail, byte[] weather)
    {
        if (string.IsNullOrEmpty(CloudsCacheDirectory))
            return;
        try
        {
            Directory.CreateDirectory(CloudsCacheDirectory);
            string path = Path.Combine(CloudsCacheDirectory, "clouds_noise.bin");
            using BinaryWriter writer = new(File.Create(path));
            writer.Write((byte)'E');
            writer.Write((byte)'C');
            writer.Write((byte)'L');
            writer.Write((byte)'D');
            writer.Write(10);
            writer.Write(CloudShapeSize);
            writer.Write(CloudDetailSize);
            writer.Write(CloudWeatherSize);
            writer.Write(shape);
            writer.Write(detail);
            writer.Write(weather);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void ExecuteSkyPass(ICommandBuffer commandBuffer, Size2D extent, bool gameView)
    {
        if (_skyPipeline is null || _skyboxMode != SkyboxMode.Procedural)
            return;
        Matrix4x4 viewProjection = gameView ? _gameCurrentViewProjection : _sceneCurrentViewProjection;
        Matrix4x4 inverseViewProjection = viewProjection.Invert();
        SkyConstants constants = new()
        {
            InverseViewProjection = inverseViewProjection,
            CameraPosition = gameView ? _gameSkyCameraPosition : _sceneSkyCameraPosition,
            SunDirection = _sunDirection,
            Extent = new Vector2(extent.Width, extent.Height),
            SunDiscIntensity = 1f,
        };
        commandBuffer.BindGraphicsPipeline(_skyPipeline);
        commandBuffer.SetViewport(new Viewport(0f, 0f, extent.Width, extent.Height, 0f, 1f));
        commandBuffer.SetScissor(new Rect(0, 0, (int)extent.Width, (int)extent.Height));
        commandBuffer.PushConstants(StructuredBytes(constants), ShaderStages.Fragment);
        commandBuffer.Draw(3);
    }

    private void ExecuteCloudsPass(ICommandBuffer commandBuffer, Size2D extent, bool gameView)
    {
        if (_cloudsPipeline is null || _cloudShapeTexture is null || _cloudDetailTexture is null || _cloudWeatherTexture is null || !CloudsEnabled)
            return;
        Matrix4x4 viewProjection = gameView ? _gameCurrentViewProjection : _sceneCurrentViewProjection;
        CloudsConstants constants = new()
        {
            InverseViewProjection = viewProjection.Invert(),
            CameraPosition = gameView ? _gameSkyCameraPosition : _sceneSkyCameraPosition,
            SunDirection = _sunDirection,
            Extent = new Vector2(extent.Width, extent.Height),
            JitterSeed = _cloudJitterSeed,
            Coverage = CloudCoverage,
            Density = CloudDensity,
            WindOffset = new Vector2(_cloudWindPhase, _cloudWindPhase * 0.6f),
            CloudBottom = CloudBottom,
            CloudTop = CloudTop,
        };
        IDescriptorSet set = _cloudsProgram!.CreateDescriptorSet(0);
        set.WriteTexture(0, _cloudShapeTexture);
        set.WriteTexture(1, _cloudDetailTexture);
        set.WriteTexture(2, _cloudWeatherTexture);
        set.WriteSampler(3, _cloudsSampler!);
        set.WriteTexture(4, _graph!.GetTexture(gameView ? _gameDepth! : _mainDepth!));
        _frameSets[_activeFrameSlot].Add(set);
        commandBuffer.BindGraphicsPipeline(_cloudsPipeline);
        commandBuffer.SetViewport(new Viewport(0f, 0f, extent.Width, extent.Height, 0f, 1f));
        commandBuffer.SetScissor(new Rect(0, 0, (int)extent.Width, (int)extent.Height));
        commandBuffer.BindDescriptorSet(set, 0);
        commandBuffer.PushConstants(StructuredBytes(constants), ShaderStages.Fragment);
        commandBuffer.Draw(3);
    }

    private void ExecuteTonemapPass(ICommandBuffer commandBuffer, Size2D extent) =>
        ExecuteTonemapPass(commandBuffer, extent, _historyWrite!, _bloomTarget!);

    private void ExecuteTonemapPass(
        ICommandBuffer commandBuffer,
        Size2D extent,
        RenderGraphResource hdrSource,
        RenderGraphResource bloomSource,
        bool renderContent = true)
    {
        if (!renderContent)
            return;
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

    private void CommitTemporalFrame()
    {
        if (_sceneViewRendered)
        {
            _sceneHistory.Commit();
            _sceneReflectionHistory.Commit();
            if (_sceneGiHistoryWrite is not null)
                _sceneGiHistory.Commit();
            _scenePreviousViewProjection = _sceneCurrentViewProjection;
            _scenePreviousJitter = _sceneJitter;
            _sceneJitterIndex++;
            CommitModelHistory(_scenePreviousModels, _sceneDrawnItems);
        }
        if (_gameViewRendered)
        {
            _gameHistory.Commit();
            _gameReflectionHistory.Commit();
            if (_gameGiHistoryWrite is not null)
                _gameGiHistory.Commit();
            _gamePreviousViewProjection = _gameCurrentViewProjection;
            _gamePreviousJitter = _gameJitter;
            _gameJitterIndex++;
            CommitModelHistory(_gamePreviousModels, _gameDrawnItems);
        }
    }

    private static void CommitModelHistory(Dictionary<RenderItem, Matrix4x4> history, HashSet<RenderItem> drawnItems)
    {
        history.Clear();
        foreach (RenderItem item in drawnItems)
            history[item] = item.ModelMatrix;
    }

    private void ResetTemporalHistory()
    {
        _sceneHistory.Invalidate();
        _gameHistory.Invalidate();
        _sceneGiHistory.Invalidate();
        _gameGiHistory.Invalidate();
        _sceneReflectionHistory.Invalidate();
        _gameReflectionHistory.Invalidate();
        _sceneJitterIndex = 0;
        _gameJitterIndex = 0;
        _scenePreviousModels.Clear();
        _gamePreviousModels.Clear();
    }

    private void RebuildTargets()
    {
        IRHI rhi = _rhi!;
        Size2D sceneExtent = _editorMode ? _viewExtent : rhi.Swapchain.Extent;
        if (sceneExtent.Width <= 0 || sceneExtent.Height <= 0)
            return;
        Size2D gameExtent = _editorMode && _gameViewExtent.Width > 8 && _gameViewExtent.Height > 8 ? _gameViewExtent : sceneExtent;
        if (gameExtent.Width <= 0 || gameExtent.Height <= 0)
            return;
        bool rtMaster = _rayTracingEnabled && _rtMasterEnabled;
        GlobalIlluminationMode effectiveGiMode = !_rayTracingEnabled || !_rtMasterEnabled
            ? (_giMode == GlobalIlluminationMode.RayTraced ? GlobalIlluminationMode.ScreenSpace : _giMode)
            : _giMode;
        bool rtGiActive = rtMaster && effectiveGiMode == GlobalIlluminationMode.RayTraced;
        bool giEnabled = _temporalEnabled && effectiveGiMode != GlobalIlluminationMode.Off;
        if (_swapchainTarget is not null && sceneExtent == _lastTargetExtent && gameExtent == _lastGameExtent && _editorMode == _lastEditorMode && _presentExtent == rhi.Swapchain.Extent && giEnabled == _lastGiEnabled && ShadowMapSize == _lastShadowResolution && rtMaster == _lastRtMasterEnabled && rtGiActive == _lastRtGiActive && _rtResolutionScale == _lastRtResolutionScale)
            return;

        rhi.WaitIdle();
        foreach (List<IDescriptorSet> sets in _frameSets)
        {
            foreach (IDescriptorSet set in sets)
                set.Dispose();
            sets.Clear();
        }
        _graph!.Dispose();
        Size2D rtExtent = new(Math.Max(1, (int)MathF.Ceiling(sceneExtent.Width * _rtResolutionScale)), Math.Max(1, (int)MathF.Ceiling(sceneExtent.Height * _rtResolutionScale)));
        Size2D gameRtExtent = new(Math.Max(1, (int)MathF.Ceiling(gameExtent.Width * _rtResolutionScale)), Math.Max(1, (int)MathF.Ceiling(gameExtent.Height * _rtResolutionScale)));
        _gameRenderExtent = gameExtent;
        bool giViaRt = rtGiActive;
        if (giViaRt != _lastGiViaRt)
        {
            _sceneGiHistory.Invalidate();
            _gameGiHistory.Invalidate();
            _lastGiViaRt = giViaRt;
        }
        if (_temporalEnabled)
        {
            _sceneHistory.Recreate(_device!, sceneExtent);
            _sceneReflectionHistory.Recreate(_device!, sceneExtent);
            if (_editorMode)
            {
                _gameHistory.Recreate(_device!, gameExtent);
                _gameReflectionHistory.Recreate(_device!, gameExtent);
            }
            else
            {
                _gameHistory.Invalidate();
                _gameReflectionHistory.Invalidate();
            }
            if (giEnabled)
            {
                _sceneGiHistory.Recreate(_device!, sceneExtent);
                if (_editorMode)
                    _gameGiHistory.Recreate(_device!, gameExtent);
                else
                    _gameGiHistory.Invalidate();
            }
            else
            {
                _sceneGiHistory.Invalidate();
                _gameGiHistory.Invalidate();
            }
        }
        else
        {
            _sceneHistory.Invalidate();
            _gameHistory.Invalidate();
            _sceneGiHistory.Invalidate();
            _gameGiHistory.Invalidate();
            _sceneReflectionHistory.Invalidate();
            _gameReflectionHistory.Invalidate();
        }
        _sceneJitterIndex = 0;
        _gameJitterIndex = 0;
        _scenePreviousModels.Clear();
        _gamePreviousModels.Clear();
        _rtBuildGraphSlots.Clear();
        _graph = new RenderGraph(_device!, _log!);
        _swapchainTarget = _graph.ImportTexture(
            "SwapchainTarget",
            rhi.Swapchain.GetRenderTarget(0),
            new RenderGraphResourceState(ImageLayout.Undefined, PipelineStages.Top, ResourceAccess.None),
            new RenderGraphResourceState(ImageLayout.PresentSrcKhr, PipelineStages.Bottom, ResourceAccess.None));
        _shadowDepth = _graph.DeclareTransientTexture("ShadowDepth", new TextureDescription
        {
            Extent = new Size2D(CascadedShadows.AtlasSize(ShadowMapSize), CascadedShadows.AtlasSize(ShadowMapSize)),
            Format = TextureFormat.D32Float,
            Usage = TextureUsage.TransferSource,
        });
        _gameShadowDepth = _editorMode
            ? _graph.DeclareTransientTexture("GameShadowDepth", _shadowDepth.Description)
            : null;
        _mainDepth = _graph.DeclareTransientTexture("MainDepth", new TextureDescription
        {
            Extent = sceneExtent,
            Format = TextureFormat.D32Float,
        });
        _hdrTarget = _graph.DeclareTransientTexture("HdrColor", new TextureDescription
        {
            Extent = sceneExtent,
            Format = TextureFormat.R16G16B16A16Float,
            Usage = TextureUsage.TransferSource,
        });
        _motionTarget = _temporalEnabled
            ? _graph.DeclareTransientTexture("MotionVectors", new TextureDescription
            {
                Extent = sceneExtent,
                Format = TextureFormat.R16G16B16A16Float,
            })
            : null;
        _historyRead = _temporalEnabled
            ? _graph.ImportTexture("HistoryRead", _sceneHistory.ReadTexture, ImageLayout.Undefined)
            : null;
        _historyWrite = _temporalEnabled
            ? _graph.ImportTexture("HistoryWrite", _sceneHistory.WriteTexture, ImageLayout.Undefined, ImageLayout.ShaderReadOnlyOptimal)
            : null;
        _sceneReflectionHistoryRead = _temporalEnabled
            ? _graph.ImportTexture("ReflectionHistoryRead", _sceneReflectionHistory.ReadTexture, ImageLayout.Undefined)
            : null;
        _sceneReflectionHistoryWrite = _temporalEnabled
            ? _graph.ImportTexture("ReflectionHistoryWrite", _sceneReflectionHistory.WriteTexture, ImageLayout.Undefined, ImageLayout.ShaderReadOnlyOptimal)
            : null;
        _giTarget = giEnabled
            ? _graph.DeclareTransientTexture("GiColor", new TextureDescription
            {
                Extent = giViaRt ? rtExtent : sceneExtent,
                Format = TextureFormat.R16G16B16A16Float,
            })
            : null;
        _rtGiFiltered = giViaRt
            ? _graph.DeclareTransientTexture("RtGiFiltered", _giTarget!.Description)
            : null;
        _rtGiFilterScratch = giViaRt
            ? _graph.DeclareTransientTexture("RtGiFilterScratch", _giTarget!.Description)
            : null;
        _rtGiValidity = giViaRt
            ? _graph.DeclareTransientTexture("RtGiValidity", _giTarget!.Description)
            : null;
        _sceneGiHistoryRead = giEnabled
            ? _graph.ImportTexture("GiHistoryRead", _sceneGiHistory.ReadTexture, ImageLayout.Undefined)
            : null;
        _sceneGiHistoryWrite = giEnabled
            ? _graph.ImportTexture("GiHistoryWrite", _sceneGiHistory.WriteTexture, ImageLayout.Undefined, ImageLayout.ShaderReadOnlyOptimal)
            : null;
        _compositeTarget = giEnabled
            ? _graph.DeclareTransientTexture("CompositeColor", new TextureDescription
            {
                Extent = sceneExtent,
                Format = TextureFormat.R16G16B16A16Float,
            })
            : null;
        _bloomTarget = _temporalEnabled
            ? _graph.DeclareTransientTexture("BloomColor", new TextureDescription
            {
                Extent = new Size2D(Math.Max(1, sceneExtent.Width / 2), Math.Max(1, sceneExtent.Height / 2)),
                Format = TextureFormat.R16G16B16A16Float,
            })
            : null;
        _bloomFallback = _graph.ImportTexture(
            "BloomFallback",
            _bloomFallbackTexture!,
            ImageLayout.ShaderReadOnlyOptimal,
            ImageLayout.ShaderReadOnlyOptimal);
        if (_rayTracingEnabled)
        {
            var rtColorUsage = TextureUsage.ColorAttachment | TextureUsage.Sampled | TextureUsage.Storage | TextureUsage.TransferSource;
            var rtDepthUsage = TextureUsage.DepthAttachment | TextureUsage.Sampled | TextureUsage.TransferSource;
            _rtNormalRoughness = _graph.DeclareTransientTexture("RtNormalRoughness", new TextureDescription { Extent = sceneExtent, Format = TextureFormat.R16G16B16A16Float, Usage = rtColorUsage });
            _rtDepth = _graph.DeclareTransientTexture("RtDepth", new TextureDescription { Extent = sceneExtent, Format = TextureFormat.D32Float, Usage = rtDepthUsage });
            _rtVisibility = _graph.DeclareTransientTexture("RtVisibility", new TextureDescription { Extent = rtExtent, Format = TextureFormat.R16G16B16A16Float, Usage = rtColorUsage });
            _rtVisibilityValidity = _graph.DeclareTransientTexture("RtVisibilityValidity", new TextureDescription { Extent = rtExtent, Format = TextureFormat.R8G8B8A8UNorm, Usage = rtColorUsage });
            _rtResolvedVisibility = _graph.DeclareTransientTexture("RtResolvedVisibility", new TextureDescription { Extent = sceneExtent, Format = TextureFormat.R16G16B16A16Float, Usage = rtColorUsage });
            _rtSbt = _graph.ImportBuffer(
                "RtSbt",
                _rtShadowSbt!.Buffer,
                new RenderGraphMemoryState(PipelineStages.Transfer, ResourceAccess.TransferWrite));
            _rtRawReflection = _graph.DeclareTransientTexture("RtRawReflection", new TextureDescription { Extent = rtExtent, Format = TextureFormat.R16G16B16A16Float, Usage = rtColorUsage });
            _rtReflectionValidity = _graph.DeclareTransientTexture("RtReflectionValidity", new TextureDescription { Extent = rtExtent, Format = TextureFormat.R8G8B8A8UNorm, Usage = rtColorUsage });
            _rtFilteredReflection = _graph.DeclareTransientTexture("RtFilteredReflection", new TextureDescription { Extent = rtExtent, Format = TextureFormat.R16G16B16A16Float, Usage = rtColorUsage });
            _rtSpecular = _graph.DeclareTransientTexture("RtSpecular", new TextureDescription { Extent = sceneExtent, Format = TextureFormat.R16G16B16A16Float, Usage = rtColorUsage });
            _rtSpecularResponse = _graph.DeclareTransientTexture("RtSpecularResponse", new TextureDescription { Extent = sceneExtent, Format = TextureFormat.R16G16B16A16Float, Usage = rtColorUsage });
            _rtCompositeTarget = _graph.DeclareTransientTexture("RtCompositeColor", new TextureDescription { Extent = sceneExtent, Format = TextureFormat.R16G16B16A16Float, Usage = rtColorUsage });
            _rtReflectionSbtResource = _graph.ImportBuffer(
                "RtReflectionSbt",
                _rtReflectionSbt!.Buffer,
                new RenderGraphMemoryState(PipelineStages.Transfer, ResourceAccess.TransferWrite));
            _rtGiSbtResource = _graph.ImportBuffer(
                "RtGiSbt",
                _rtGiSbt!.Buffer,
                new RenderGraphMemoryState(PipelineStages.Transfer, ResourceAccess.TransferWrite));
            if (_editorMode)
            {
                _gameRtNormalRoughness = _graph.DeclareTransientTexture("GameRtNormalRoughness", new TextureDescription { Extent = gameExtent, Format = TextureFormat.R16G16B16A16Float, Usage = rtColorUsage });
                _gameRtDepth = _graph.DeclareTransientTexture("GameRtDepth", new TextureDescription { Extent = gameExtent, Format = TextureFormat.D32Float, Usage = TextureUsage.DepthAttachment | TextureUsage.Sampled | TextureUsage.TransferSource });
                _gameRtVisibility = _graph.DeclareTransientTexture("GameRtVisibility", new TextureDescription { Extent = gameRtExtent, Format = TextureFormat.R16G16B16A16Float, Usage = rtColorUsage });
                _gameRtVisibilityValidity = _graph.DeclareTransientTexture("GameRtVisibilityValidity", new TextureDescription { Extent = gameRtExtent, Format = TextureFormat.R8G8B8A8UNorm, Usage = rtColorUsage });
                _gameRtResolvedVisibility = _graph.DeclareTransientTexture("GameRtResolvedVisibility", new TextureDescription { Extent = gameExtent, Format = TextureFormat.R16G16B16A16Float, Usage = rtColorUsage });
                _gameRtRawReflection = _graph.DeclareTransientTexture("GameRtRawReflection", new TextureDescription { Extent = gameRtExtent, Format = TextureFormat.R16G16B16A16Float, Usage = rtColorUsage });
                _gameRtReflectionValidity = _graph.DeclareTransientTexture("GameRtReflectionValidity", new TextureDescription { Extent = gameRtExtent, Format = TextureFormat.R8G8B8A8UNorm, Usage = rtColorUsage });
                _gameRtFilteredReflection = _graph.DeclareTransientTexture("GameRtFilteredReflection", new TextureDescription { Extent = gameRtExtent, Format = TextureFormat.R16G16B16A16Float, Usage = rtColorUsage });
                _gameRtSpecular = _graph.DeclareTransientTexture("GameRtSpecular", new TextureDescription { Extent = gameExtent, Format = TextureFormat.R16G16B16A16Float, Usage = rtColorUsage });
                _gameRtSpecularResponse = _graph.DeclareTransientTexture("GameRtSpecularResponse", new TextureDescription { Extent = gameExtent, Format = TextureFormat.R16G16B16A16Float, Usage = rtColorUsage });
                _gameRtCompositeTarget = _graph.DeclareTransientTexture("GameRtCompositeColor", new TextureDescription { Extent = gameExtent, Format = TextureFormat.R16G16B16A16Float, Usage = rtColorUsage });
            }
        }
        else
        {
            _rtNormalRoughness = null;
            _rtDepth = null;
            _rtVisibility = null;
            _rtVisibilityValidity = null;
            _rtResolvedVisibility = null;
            _gameRtNormalRoughness = null;
            _gameRtDepth = null;
            _gameRtVisibility = null;
            _gameRtVisibilityValidity = null;
            _gameRtResolvedVisibility = null;
            _rtSbt = null;
            _rtRawReflection = null;
            _rtReflectionValidity = null;
            _rtFilteredReflection = null;
            _rtSpecular = null;
            _rtSpecularResponse = null;
            _rtCompositeTarget = null;
            _gameRtRawReflection = null;
            _gameRtReflectionValidity = null;
            _gameRtFilteredReflection = null;
            _gameRtSpecular = null;
            _gameRtSpecularResponse = null;
            _gameRtCompositeTarget = null;
            _rtReflectionSbtResource = null;
            _rtGiSbtResource = null;
        }
        _tonemapTarget = _graph.DeclareTransientTexture("PresentColor", new TextureDescription
        {
            Extent = rhi.Swapchain.Extent,
            Format = TextureFormat.B8G8R8A8Srgb,
            Usage = TextureUsage.TransferSource,
        });
        _viewTarget = _editorMode
            ? _graph.DeclareTransientTexture("ViewColor", new TextureDescription
            {
                Extent = sceneExtent,
                Format = TextureFormat.B8G8R8A8Srgb,
                Usage = TextureUsage.Sampled | TextureUsage.TransferSource,
            })
            : null;
        _gameTarget = _editorMode
            ? _graph.DeclareTransientTexture("GameColor", new TextureDescription
            {
                Extent = gameExtent,
                Format = TextureFormat.B8G8R8A8Srgb,
                Usage = TextureUsage.Sampled | TextureUsage.TransferSource,
            })
            : null;
        if (_editorMode)
        {
            _gameDepth = _graph.DeclareTransientTexture("GameDepth", new TextureDescription
            {
                Extent = gameExtent,
                Format = TextureFormat.D32Float,
            });
            _gameHdrTarget = _graph.DeclareTransientTexture("GameHdr", new TextureDescription
            {
                Extent = gameExtent,
                Format = TextureFormat.R16G16B16A16Float,
            });
            _gameMotionTarget = _temporalEnabled
                ? _graph.DeclareTransientTexture("GameMotion", new TextureDescription
                {
                    Extent = gameExtent,
                    Format = TextureFormat.R16G16B16A16Float,
                })
                : null;
            _gameHistoryRead = _temporalEnabled
                ? _graph.ImportTexture("GameHistoryRead", _gameHistory.ReadTexture, ImageLayout.Undefined)
                : null;
            _gameHistoryWrite = _temporalEnabled
                ? _graph.ImportTexture("GameHistoryWrite", _gameHistory.WriteTexture, ImageLayout.Undefined, ImageLayout.ShaderReadOnlyOptimal)
                : null;
            _gameReflectionHistoryRead = _temporalEnabled
                ? _graph.ImportTexture("GameReflectionHistoryRead", _gameReflectionHistory.ReadTexture, ImageLayout.Undefined)
                : null;
            _gameReflectionHistoryWrite = _temporalEnabled
                ? _graph.ImportTexture("GameReflectionHistoryWrite", _gameReflectionHistory.WriteTexture, ImageLayout.Undefined, ImageLayout.ShaderReadOnlyOptimal)
                : null;
            _gameGiTarget = giEnabled
                ? _graph.DeclareTransientTexture("GameGiColor", new TextureDescription
                {
                    Extent = giViaRt ? gameRtExtent : gameExtent,
                    Format = TextureFormat.R16G16B16A16Float,
                })
                : null;
            _gameRtGiFiltered = giViaRt
                ? _graph.DeclareTransientTexture("GameRtGiFiltered", _gameGiTarget!.Description)
                : null;
            _gameRtGiFilterScratch = giViaRt
                ? _graph.DeclareTransientTexture("GameRtGiFilterScratch", _gameGiTarget!.Description)
                : null;
            _gameRtGiValidity = giViaRt
                ? _graph.DeclareTransientTexture("GameRtGiValidity", _gameGiTarget!.Description)
                : null;
            _gameGiHistoryRead = giEnabled
                ? _graph.ImportTexture("GameGiHistoryRead", _gameGiHistory.ReadTexture, ImageLayout.Undefined)
                : null;
            _gameGiHistoryWrite = giEnabled
                ? _graph.ImportTexture("GameGiHistoryWrite", _gameGiHistory.WriteTexture, ImageLayout.Undefined, ImageLayout.ShaderReadOnlyOptimal)
                : null;
            _gameCompositeTarget = giEnabled
                ? _graph.DeclareTransientTexture("GameCompositeColor", new TextureDescription
                {
                    Extent = gameExtent,
                    Format = TextureFormat.R16G16B16A16Float,
                })
                : null;
            _gameBloomTarget = _temporalEnabled
                ? _graph.DeclareTransientTexture("GameBloom", new TextureDescription
                {
                    Extent = new Size2D(Math.Max(1, gameExtent.Width / 2), Math.Max(1, gameExtent.Height / 2)),
                    Format = TextureFormat.R16G16B16A16Float,
                })
                : null;
        }
        else
        {
            _gameDepth = null;
            _gameHdrTarget = null;
            _gameMotionTarget = null;
            _gameHistoryRead = null;
            _gameHistoryWrite = null;
            _gameGiTarget = null;
            _gameRtGiFiltered = null;
            _gameRtGiFilterScratch = null;
            _gameRtGiValidity = null;
            _gameGiHistoryRead = null;
            _gameGiHistoryWrite = null;
            _gameCompositeTarget = null;
            _gameBloomTarget = null;
            _gameRtNormalRoughness = null;
            _gameRtDepth = null;
            _gameRtVisibility = null;
            _gameRtRawReflection = null;
            _gameRtFilteredReflection = null;
            _gameRtSpecular = null;
            _gameRtSpecularResponse = null;
            _gameRtCompositeTarget = null;
        }
        BuildGraphTopology(sceneExtent);
        _graph.Compile();
        if (_viewTarget is not null)
            _graph.Realize(_viewTarget);
        if (_gameTarget is not null)
            _graph.Realize(_gameTarget);
        _lastTargetExtent = sceneExtent;
        _lastGameExtent = gameExtent;
        _lastEditorMode = _editorMode;
        _lastGiEnabled = giEnabled;
        _lastRtMasterEnabled = rtMaster;
        _lastRtResolutionScale = _rtResolutionScale;
        _lastRtGiActive = rtGiActive;
        _lastShadowResolution = ShadowMapSize;
        _presentExtent = rhi.Swapchain.Extent;
    }

    private void CompilePipelines()
    {
        ShaderProgramDescription shadowProgram = _compiler!.CompileProgram(
            Path.Combine(_shaderDirectory, "Shadow.slang"),
            [(ShaderStage.Vertex, "vsMain"), (ShaderStage.Fragment, "psMain")]);
        ShaderProgramDescription pbrProgram = _compiler.CompileProgram(
            Path.Combine(_shaderDirectory, "PBR.slang"),
            [(ShaderStage.Vertex, "vsMain"), (ShaderStage.Fragment, "psMain")],
            PbrDefines());
        ShaderProgramDescription tonemapProgram = _compiler.CompileProgram(
            Path.Combine(_shaderDirectory, "Tonemap.slang"),
            [(ShaderStage.Vertex, "vsMain"), (ShaderStage.Fragment, "psMain")]);
        ShaderProgramDescription skyProgram = _compiler.CompileProgram(
            Path.Combine(_shaderDirectory, "Sky.slang"),
            [(ShaderStage.Vertex, "vsMain"), (ShaderStage.Fragment, "psMain")]);
        ShaderProgramDescription cloudsProgram = _compiler.CompileProgram(
            Path.Combine(_shaderDirectory, "Clouds.slang"),
            [(ShaderStage.Vertex, "vsMain"), (ShaderStage.Fragment, "psMain")]);
        ShaderProgramDescription bloomProgram = _temporalEnabled
            ? _compiler.CompileProgram(
                Path.Combine(_shaderDirectory, "Bloom.slang"),
                [(ShaderStage.Compute, "csMain")])
            : new ShaderProgramDescription { Name = "BloomDisabled", Stages = [] };
        ShaderProgramDescription temporalProgram = _temporalEnabled
            ? _compiler.CompileProgram(
                Path.Combine(_shaderDirectory, "TemporalResolve.slang"),
                [(ShaderStage.Compute, "csMain")])
            : new ShaderProgramDescription { Name = "TemporalDisabled", Stages = [] };
        ShaderProgramDescription rtGiTemporalProgram = _rayTracingEnabled
            ? _compiler.CompileProgram(
                Path.Combine(_shaderDirectory, "TemporalResolve.slang"),
                [(ShaderStage.Compute, "csMain")],
                new Dictionary<string, string?> { ["RT_GI_DENOISE"] = "1" })
            : new ShaderProgramDescription { Name = "RtGiTemporalDisabled", Stages = [] };
        ShaderProgramDescription ssgiProgram = _temporalEnabled
            ? _compiler.CompileProgram(
                Path.Combine(_shaderDirectory, "SSGI.slang"),
                [(ShaderStage.Compute, "csMain")])
            : new ShaderProgramDescription { Name = "SsgiDisabled", Stages = [] };
        ShaderProgramDescription giCompositeProgram = _temporalEnabled
            ? _compiler.CompileProgram(
                Path.Combine(_shaderDirectory, "GIComposite.slang"),
                [(ShaderStage.Compute, "csMain")])
            : new ShaderProgramDescription { Name = "GiCompositeDisabled", Stages = [] };

        if (_temporalEnabled)
        {
            uint temporalPushSize = temporalProgram.Reflection.PushConstants.Count > 0
                ? temporalProgram.Reflection.PushConstants[0].Size
                : 0;
            if (temporalPushSize != (uint)Marshal.SizeOf<TemporalConstants>())
            {
                throw new InvalidOperationException(
                    $"Temporal push constant ABI mismatch: shader reports {temporalPushSize} bytes, host uses {Marshal.SizeOf<TemporalConstants>()} bytes.");
            }
            uint giCompositePushSize = giCompositeProgram.Reflection.PushConstants.Count > 0
                ? giCompositeProgram.Reflection.PushConstants[0].Size
                : 0;
            if (giCompositePushSize != (uint)Marshal.SizeOf<GiCompositeConstants>())
            {
                throw new InvalidOperationException(
                    $"GI composite push constant ABI mismatch: shader reports {giCompositePushSize} bytes, host uses {Marshal.SizeOf<GiCompositeConstants>()} bytes.");
            }
        }

        _shadowProgram = _device!.CreateShaderProgram(shadowProgram);
        _pbrProgram = _device.CreateShaderProgram(pbrProgram);
        _tonemapProgram = _device.CreateShaderProgram(tonemapProgram);
        if (_temporalEnabled)
        {
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
            _temporalProgram = _device.CreateShaderProgram(temporalProgram);
            if (_rayTracingEnabled)
            {
                ValidateRtPushAbi(rtGiTemporalProgram.Reflection, Marshal.SizeOf<TemporalConstants>(), "RT GI temporal");
                _rtGiTemporalProgram = _device.CreateShaderProgram(rtGiTemporalProgram);
                _rtGiTemporalPipeline = _device.CreateComputePipeline(new ComputePipelineDescription
                {
                    Name = "RtGiTemporalResolve",
                    ShaderProgram = _rtGiTemporalProgram,
                });
            }
            _ssgiProgram = _device.CreateShaderProgram(ssgiProgram);
            _giCompositeProgram = _device.CreateShaderProgram(giCompositeProgram);
        }
        CreateShadowPipeline();
        _pbrPipeline = _device.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            Name = "PBRForward",
            ShaderProgram = _pbrProgram,
            VertexLayouts = [Vertex.Layout],
            CullMode = CullMode.None,
            DepthTest = true,
            DepthWrite = true,
            ColorAttachmentFormats = PbrColorFormats(),
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
        if (skyProgram.Reflection.PushConstants.Count > 0
            && skyProgram.Reflection.PushConstants[0].Size != (uint)Marshal.SizeOf<SkyConstants>())
        {
            throw new InvalidOperationException(
                $"Sky push constant ABI mismatch: shader reports {skyProgram.Reflection.PushConstants[0].Size} bytes, host uses {Marshal.SizeOf<SkyConstants>()} bytes.");
        }
        _skyProgram = _device.CreateShaderProgram(skyProgram);
        _skyPipeline = _device.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            Name = "ProceduralSky",
            ShaderProgram = _skyProgram,
            VertexLayouts = [],
            CullMode = CullMode.None,
            DepthTest = true,
            DepthWrite = false,
            DepthCompare = CompareOp.LessOrEqual,
            ColorAttachmentFormats = [TextureFormat.R16G16B16A16Float],
            DepthAttachmentFormat = TextureFormat.D32Float,
        });
        if (cloudsProgram.Reflection.PushConstants.Count > 0
            && cloudsProgram.Reflection.PushConstants[0].Size != (uint)Marshal.SizeOf<CloudsConstants>())
        {
            throw new InvalidOperationException(
                $"Clouds push constant ABI mismatch: shader reports {cloudsProgram.Reflection.PushConstants[0].Size} bytes, host uses {Marshal.SizeOf<CloudsConstants>()} bytes.");
        }
        _cloudsProgram = _device.CreateShaderProgram(cloudsProgram);
        _cloudsPipeline = _device.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            Name = "VolumetricClouds",
            ShaderProgram = _cloudsProgram,
            VertexLayouts = [],
            CullMode = CullMode.None,
            DepthTest = true,
            DepthWrite = false,
            DepthCompare = CompareOp.LessOrEqual,
            BlendMode = BlendMode.Alpha,
            ColorAttachmentFormats = [TextureFormat.R16G16B16A16Float],
            DepthAttachmentFormat = TextureFormat.D32Float,
        });
        _cloudsSampler = _device.CreateSampler(new SamplerDescription
        {
            MagFilter = FilterMode.Linear,
            MinFilter = FilterMode.Linear,
            AddressMode = AddressMode.Repeat,
        });
        CreateCloudResources();
        ShaderProgramDescription debugLinesProgram = _compiler!.CompileProgram(
            Path.Combine(_shaderDirectory, "DebugLines.slang"),
            [(ShaderStage.Vertex, "vsMain"), (ShaderStage.Fragment, "psMain")]);
        _debugLinesProgram = _device.CreateShaderProgram(debugLinesProgram);
        _debugLinesPipeline = _device.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            Name = "ColliderGizmoLines",
            ShaderProgram = _debugLinesProgram,
            VertexLayouts = [VertexLayoutDescription.FromStruct<DebugLineVertex>(
                new VertexAttributeDescription { Offset = 0, Format = VertexFormat.Float3 },
                new VertexAttributeDescription { Offset = 12, Format = VertexFormat.Float4 })],
            Topology = PrimitiveTopology.LineList,
            CullMode = CullMode.None,
            DepthTest = true,
            DepthWrite = false,
            BlendMode = BlendMode.None,
            ColorAttachmentFormats = [TextureFormat.R16G16B16A16Float],
            DepthAttachmentFormat = TextureFormat.D32Float,
        });
        if (_temporalEnabled)
        {
            _bloomPipeline = _device.CreateComputePipeline(new ComputePipelineDescription
            {
                Name = "Bloom",
                ShaderProgram = _bloomProgram!,
            });
            _temporalPipeline = _device.CreateComputePipeline(new ComputePipelineDescription
            {
                Name = "TemporalResolve",
                ShaderProgram = _temporalProgram!,
            });
            _ssgiPipeline = _device.CreateComputePipeline(new ComputePipelineDescription
            {
                Name = "ScreenSpaceGI",
                ShaderProgram = _ssgiProgram!,
            });
            _giCompositePipeline = _device.CreateComputePipeline(new ComputePipelineDescription
            {
                Name = "GiComposite",
                ShaderProgram = _giCompositeProgram!,
            });
        }
        if (_rayTracingEnabled)
            CreateRayTracingPipelines();
        _shadowPushStages = shadowProgram.Reflection.PushConstants.Count > 0 ? shadowProgram.Reflection.PushConstants[0].Stages : ShaderStages.Vertex;
        _pbrPushStages = pbrProgram.Reflection.PushConstants.Count > 0 ? pbrProgram.Reflection.PushConstants[0].Stages : ShaderStages.Vertex;
        _tonemapPushStages = tonemapProgram.Reflection.PushConstants.Count > 0 ? tonemapProgram.Reflection.PushConstants[0].Stages : ShaderStages.Fragment;
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
        _skyboxMode = _attachedScene.SkyboxMode;
        CloudsEnabled = _attachedScene.CloudsEnabled;
        CloudCoverage = _attachedScene.CloudCoverage;
        CloudDensity = _attachedScene.CloudDensity;
        CloudWindSpeed = _attachedScene.CloudWindSpeed;
        if (CloudPresets.TryGet(_attachedScene.CloudPreset, out CloudPresets.Settings preset))
        {
            CloudBottom = preset.Bottom;
            CloudTop = preset.Top;
        }
        bool sunFound = false;
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
                if (light.Kind == LightKind.Directional && !sunFound)
                {
                    _sunDirection = -gameObject.Transform.Forward;
                    sunFound = true;
                }
                sceneLights.Add(light.Kind == LightKind.Directional
                    ? Light.Directional(gameObject.Transform.Forward, light.Color, light.Intensity)
                    : Light.Point(gameObject.Transform.Position, light.Color, light.Intensity, light.Range));
            }
            MeshRendererComponent? meshRenderer = gameObject.GetComponent<MeshRendererComponent>();
            if (meshRenderer is { Enabled: true } && meshRenderer.RenderingMode != MeshRenderingMode.Off)
                activeComponents.Add(meshRenderer);
        }
        if (!sunFound)
        {
            Vector3 fallback = _attachedScene.SunDirection;
            _sunDirection = fallback.LengthSquared > MathUtil.Epsilon * MathUtil.Epsilon
                ? fallback.Normalized
                : new Vector3(0.35f, 0.45f, -0.55f);
        }
        if (sceneCamera is not null)
        {
            Camera.FieldOfViewY = sceneCamera.FieldOfViewY;
            Camera.NearPlane = sceneCamera.NearPlane;
            Camera.FarPlane = sceneCamera.FarPlane;
            Matrix4x4 cameraTransform = sceneCamera.Transform.GetInterpolatedWorldMatrix(RenderInterpolationAlpha);
            cameraTransform.Decompose(out _, out Quaternion cameraRotation, out Vector3 cameraPosition);
            Camera.Position = cameraPosition;
            Camera.Rotation = cameraRotation;
        }
        _gameCameraPresent = sceneCamera is not null;
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
                ResetTemporalHistory();
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
            if (item.RenderingMode != component.RenderingMode)
                ResetTemporalHistory();
            item.RenderingMode = component.RenderingMode;
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
        if (material.RtPrepassSet is not null)
        {
            material.RtPrepassSet.WriteTexture(3, textures.Roughness);
            material.RtPrepassSet.WriteTexture(4, textures.Normal);
        }
        if (material.RtSet is not null)
        {
            material.RtSet.WriteTexture(1, textures.Albedo);
            material.RtSet.WriteTexture(2, textures.Metallic);
            material.RtSet.WriteTexture(3, textures.Roughness);
            material.RtSet.WriteTexture(4, textures.Normal);
            material.RtSet.WriteTexture(5, textures.AmbientOcclusion);
            material.RtSet.WriteTexture(7, textures[MaterialTextureSlot.Emission]);
        }
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
        Vector3 samplePosition = _editorMode ? EditorCamera.Position : Camera.Position;
        Vector3[] samplePositions = _items.Count == 0
            ? [samplePosition]
            : _items.Select(item => item.ModelMatrix.Translation).ToArray();
        _activeLights = LightSelection.SelectImportant(_lights, samplePositions, samplePosition, LightsData.MaxLights);
        _lightsData = default;
        for (int index = 0; index < _activeLights.Count; index++)
        {
            Light light = _activeLights[index];
            _lightsData.Lights[index] = new LightData
            {
                DirectionOrPosition = light.DirectionOrPosition,
                Type = light.Type == LightType.Directional ? LightData.Directional : LightData.Point,
                Color = light.Color,
                Intensity = light.Intensity,
                Range = light.Range,
            };
        }
        _lightsData.Count = (uint)_activeLights.Count;
    }

    private void CreateShadowPipeline()
    {
        _shadowPipeline = _device!.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            Name = "ShadowDepth",
            ShaderProgram = _shadowProgram!,
            VertexLayouts = [Vertex.Layout],
            CullMode = CullMode.None,
            DepthTest = true,
            DepthWrite = true,
            DepthBiasEnabled = true,
            DepthBiasConstant = _shadowConstantBias,
            DepthBiasSlope = _shadowSlopeBias,
            DepthAttachmentFormat = TextureFormat.D32Float,
        });
    }

    private void CreateRayTracingPipelines()
    {
        Dictionary<string, string?> rtDefines = PbrDefines();
        rtDefines["RT_SHADOWS"] = "1";
        List<TextureFormat> pbrColorFormats = PbrColorFormats();
        ShaderProgramDescription rtPrepassProgram = _compiler!.CompileProgram(
            Path.Combine(_shaderDirectory, "RtPrepass.slang"),
            [(ShaderStage.Vertex, "vsMain"), (ShaderStage.Fragment, "psMain")]);
        ShaderProgramDescription rtShadowProgram = _compiler.CompileProgram(
            Path.Combine(_shaderDirectory, "RtShadows.slang"),
            [(ShaderStage.RayGeneration, "rayGen"), (ShaderStage.Miss, "missMain"), (ShaderStage.ClosestHit, "closestHitMain")]);
        Dictionary<string, string?> rtMaterialDefines = new()
        {
            ["RT_MATERIAL_TEXTURE_COUNT"] = _rtReflectionTextureArraySize.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        ShaderProgramDescription rtReflectionProgram = _compiler.CompileProgram(
            Path.Combine(_shaderDirectory, "RtReflections.slang"),
            [(ShaderStage.RayGeneration, "rayGen"), (ShaderStage.Miss, "missMain"), (ShaderStage.ClosestHit, "closestHitMain")],
            rtMaterialDefines);
        ShaderProgramDescription pbrRtProgram = _compiler.CompileProgram(
            Path.Combine(_shaderDirectory, "PBR.slang"),
            [(ShaderStage.Vertex, "vsMain"), (ShaderStage.Fragment, "psMain")],
            rtDefines);
        _rtPrepassProgram = _device!.CreateShaderProgram(rtPrepassProgram);
        _rtShadowProgram = _device.CreateShaderProgram(rtShadowProgram);
        _rtReflectionProgram = _device.CreateShaderProgram(rtReflectionProgram);
        _pbrRtProgram = _device.CreateShaderProgram(pbrRtProgram);
        _rtPrepassPipeline = _device.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            Name = "RtPrepass",
            ShaderProgram = _rtPrepassProgram,
            VertexLayouts = [Vertex.Layout],
            CullMode = CullMode.None,
            DepthTest = true,
            DepthWrite = true,
            ColorAttachmentFormats = [TextureFormat.R16G16B16A16Float],
            DepthAttachmentFormat = TextureFormat.D32Float,
        });
        _rtShadowPipeline = _device.CreateRayTracingPipeline(new RayTracingPipelineDescription
        {
            Name = "RtShadows",
            ShaderProgram = _rtShadowProgram,
            Groups =
            [
                new RayTracingShaderGroup { Type = RayTracingShaderGroupType.General, GeneralShader = 0 },
                new RayTracingShaderGroup { Type = RayTracingShaderGroupType.General, GeneralShader = 1 },
                new RayTracingShaderGroup { Type = RayTracingShaderGroupType.TrianglesHit, ClosestHitShader = 2 },
            ],
            MaxRecursionDepth = 1,
        });
        _rtShadowSbt = new ShaderBindingTable(_device, _rtShadowPipeline, 0, [1], [2]);
        _rtReflectionPipeline = _device.CreateRayTracingPipeline(new RayTracingPipelineDescription
        {
            Name = "RtReflections",
            ShaderProgram = _rtReflectionProgram,
            Groups =
            [
                new RayTracingShaderGroup { Type = RayTracingShaderGroupType.General, GeneralShader = 0 },
                new RayTracingShaderGroup { Type = RayTracingShaderGroupType.General, GeneralShader = 1 },
                new RayTracingShaderGroup { Type = RayTracingShaderGroupType.TrianglesHit, ClosestHitShader = 2 },
            ],
            MaxRecursionDepth = 1,
        });
        _rtReflectionSbt = new ShaderBindingTable(_device, _rtReflectionPipeline, 0, [1], [2]);
        ShaderProgramDescription rtGiProgram = _compiler.CompileProgram(
            Path.Combine(_shaderDirectory, "RtGi.slang"),
            [(ShaderStage.RayGeneration, "rayGen"), (ShaderStage.Miss, "missMain"), (ShaderStage.ClosestHit, "closestHitMain")],
            rtMaterialDefines);
        _rtGiProgram = _device.CreateShaderProgram(rtGiProgram);
        _rtGiPipeline = _device.CreateRayTracingPipeline(new RayTracingPipelineDescription
        {
            Name = "RtGi",
            ShaderProgram = _rtGiProgram,
            Groups =
            [
                new RayTracingShaderGroup { Type = RayTracingShaderGroupType.General, GeneralShader = 0 },
                new RayTracingShaderGroup { Type = RayTracingShaderGroupType.General, GeneralShader = 1 },
                new RayTracingShaderGroup { Type = RayTracingShaderGroupType.TrianglesHit, ClosestHitShader = 2 },
            ],
            MaxRecursionDepth = 1,
        });
        _rtGiSbt = new ShaderBindingTable(_device, _rtGiPipeline, 0, [1], [2]);
        _pbrRtPipeline = _device.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            Name = "PBRForwardRt",
            ShaderProgram = _pbrRtProgram,
            VertexLayouts = [Vertex.Layout],
            CullMode = CullMode.None,
            DepthTest = true,
            DepthWrite = true,
            ColorAttachmentFormats = pbrColorFormats,
            DepthAttachmentFormat = TextureFormat.D32Float,
        });
        ShaderProgramDescription rtFilterProgram = _compiler.CompileProgram(
            Path.Combine(_shaderDirectory, "RtFilter.slang"),
            [(ShaderStage.Compute, "csMain")]);
        ShaderProgramDescription rtCompositeProgram = _compiler.CompileProgram(
            Path.Combine(_shaderDirectory, "RtComposite.slang"),
            [(ShaderStage.Compute, "csMain")]);
        _rtFilterProgram = _device.CreateShaderProgram(rtFilterProgram);
        _rtCompositeProgram = _device.CreateShaderProgram(rtCompositeProgram);
        _rtFilterPipeline = _device.CreateComputePipeline(new ComputePipelineDescription
        {
            Name = "RtFilter",
            ShaderProgram = _rtFilterProgram,
        });
        _rtCompositePipeline = _device.CreateComputePipeline(new ComputePipelineDescription
        {
            Name = "RtComposite",
            ShaderProgram = _rtCompositeProgram,
        });
        ValidateRtPushAbi(rtFilterProgram.Reflection, RtFilterConstants.SizeInBytes, "RT filter");
        ValidateRtPushAbi(rtCompositeProgram.Reflection, RtCompositeConstants.SizeInBytes, "RT composite");
        _rtFilterPushStages = rtFilterProgram.Reflection.PushConstants.Count > 0
            ? rtFilterProgram.Reflection.PushConstants[0].Stages
            : ShaderStages.Compute;
        _rtCompositePushStages = rtCompositeProgram.Reflection.PushConstants.Count > 0
            ? rtCompositeProgram.Reflection.PushConstants[0].Stages
            : ShaderStages.Compute;
        _rtPrepassPushStages = rtPrepassProgram.Reflection.PushConstants.Count > 0
            ? rtPrepassProgram.Reflection.PushConstants[0].Stages
            : ShaderStages.Vertex;

    }

    private Dictionary<string, string?> PbrDefines()
    {
        Dictionary<string, string?> defines = _temporalEnabled
            ? new Dictionary<string, string?> { ["TEMPORAL_MRT"] = "1" }
            : [];
        if (_rayTracingEnabled)
            defines["SPECULAR_MRT"] = "1";
        return defines;
    }

    private List<TextureFormat> PbrColorFormats()
    {
        List<TextureFormat> formats = [TextureFormat.R16G16B16A16Float];
        if (_temporalEnabled)
            formats.Add(TextureFormat.R16G16B16A16Float);
        if (_rayTracingEnabled)
        {
            formats.Add(TextureFormat.R16G16B16A16Float);
            formats.Add(TextureFormat.R16G16B16A16Float);
        }
        return formats;
    }

    private static void ValidateRtPushAbi(PipelineReflection reflection, int expectedSize, string name)
    {
        uint pushSize = reflection.PushConstants.Count > 0 ? reflection.PushConstants[0].Size : 0;
        if (pushSize != (uint)expectedSize)
            throw new InvalidOperationException(
                $"{name} push constant ABI mismatch: shader reports {pushSize} bytes, host uses {expectedSize} bytes.");
    }

    private ShadowMode EffectiveShadowMode => !_rayTracingEnabled || !_rtMasterEnabled
        ? (_shadowMode == ShadowMode.RayTraced ? ShadowMode.Cascaded : _shadowMode)
        : _shadowMode;

    private ShadowData BuildShadowData(Camera camera, IReadOnlyList<ShadowCasterBounds> casterBounds)
    {
        int lightIndex = LightSelection.FindShadowLightIndex(_activeLights);
        Light? light = lightIndex >= 0 ? _activeLights[lightIndex] : null;
        CascadedShadowSettings settings = EffectiveShadowMode == ShadowMode.Off
            ? _shadowSettings with { Strength = 0f }
            : _shadowSettings with { Strength = Math.Clamp(_shadowSettings.Strength * ShadowIntensity, 0f, 3f) };
        return CascadedShadows.Build(
            camera,
            light,
            lightIndex,
            settings,
            casterBounds);
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

    private sealed class RayTracingBuildGraphSlot(RayTracingScene scene, RayTracingFrameSlotReadiness readiness, int frameSlot)
    {
        public RayTracingScene Scene { get; } = scene;
        public RayTracingFrameSlotReadiness Readiness { get; } = readiness;
        public int FrameSlot { get; } = frameSlot;
        public RenderGraphBufferResource Instances { get; init; } = null!;
        public RenderGraphBufferResource Scratch { get; init; } = null!;
        public RenderGraphAccelerationStructureResource TopLevel { get; init; } = null!;
        public RenderGraphPass Pass { get; set; } = null!;
        public bool Enabled { get; set; }
        public bool Recorded { get; set; }
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
            if (Engine.Core.VirtualFileSystem.Exists(Path.Combine(candidate, "PBR.slang")))
                return Path.GetFullPath(candidate);
        }
        throw new DirectoryNotFoundException("Shader directory not found. Searched: " + string.Join(", ", candidates.Select(Path.GetFullPath)));
    }

    private static byte[] BlackHalfPixel() => new byte[8];

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
        _scenePreviousModels.Clear();
        _gamePreviousModels.Clear();
        _attachedScene = null;
        foreach (Material material in _materials)
            material.Dispose();
        _materials.Clear();
        _defaultMaterialTextures?.Dispose();
        _bloomFallbackTexture?.Dispose();
        _bloomFallbackTexture = null;
        _iblResources?.Dispose();
        _iblResources = null;
        _sceneHistory.Dispose();
        _gameHistory.Dispose();
        _sceneGiHistory.Dispose();
        _gameGiHistory.Dispose();
        _shadowCompareSampler?.Dispose();
        _linearSampler?.Dispose();
        _uniformRing?.Dispose();
        _graph?.Dispose();
        _rtShadowSbt?.Dispose();
        _rtShadowSbt = null;
        _rtReflectionSbt?.Dispose();
        _rtReflectionSbt = null;
        _rtGiSbt?.Dispose();
        _rtGiSbt = null;
        _rtShadowPipeline?.Dispose();
        _rtShadowPipeline = null;
        _rtReflectionPipeline?.Dispose();
        _rtReflectionPipeline = null;
        _rtGiPipeline?.Dispose();
        _rtGiPipeline = null;
        _rtPrepassPipeline?.Dispose();
        _rtPrepassPipeline = null;
        _rtFilterPipeline?.Dispose();
        _rtFilterPipeline = null;
        _rtCompositePipeline?.Dispose();
        _rtCompositePipeline = null;
        _pbrRtPipeline?.Dispose();
        _pbrRtPipeline = null;
        _rtShadowProgram?.Dispose();
        _rtShadowProgram = null;
        _rtReflectionProgram?.Dispose();
        _rtReflectionProgram = null;
        _rtGiProgram?.Dispose();
        _rtGiProgram = null;
        _rtFilterProgram?.Dispose();
        _rtFilterProgram = null;
        _rtCompositeProgram?.Dispose();
        _rtCompositeProgram = null;
        _rtPrepassProgram?.Dispose();
        _rtPrepassProgram = null;
        _pbrRtProgram?.Dispose();
        _pbrRtProgram = null;
        _sceneRayTracingScene?.Dispose();
        _sceneRayTracingScene = null;
        _gameRayTracingScene?.Dispose();
        _gameRayTracingScene = null;
        _shadowPipeline?.Dispose();
        _pbrPipeline?.Dispose();
        _tonemapPipeline?.Dispose();
        _skyPipeline?.Dispose();
        _cloudsPipeline?.Dispose();
        _debugLinesPipeline?.Dispose();
        _bloomPipeline?.Dispose();
        _temporalPipeline?.Dispose();
        _rtGiTemporalPipeline?.Dispose();
        _rtGiTemporalPipeline = null;
        _ssgiPipeline?.Dispose();
        _giCompositePipeline?.Dispose();
        _shadowProgram?.Dispose();
        _pbrProgram?.Dispose();
        _tonemapProgram?.Dispose();
        _skyProgram?.Dispose();
        _cloudsProgram?.Dispose();
        _debugLinesProgram?.Dispose();
        _cloudShapeTexture?.Dispose();
        _cloudShapeTexture = null;
        _cloudDetailTexture?.Dispose();
        _cloudDetailTexture = null;
        _cloudWeatherTexture?.Dispose();
        _cloudWeatherTexture = null;
        _cloudsSampler?.Dispose();
        _cloudsSampler = null;
        _bloomProgram?.Dispose();
        _temporalProgram?.Dispose();
        _rtGiTemporalProgram?.Dispose();
        _rtGiTemporalProgram = null;
        _ssgiProgram?.Dispose();
        _giCompositeProgram?.Dispose();
        _initialized = false;
        _log?.Information("Renderer shut down");
    }
}
