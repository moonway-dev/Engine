using Engine.Core;
using Engine.Editor.Documents;
using Engine.Mathematics;
using Engine.Platform;
using Engine.RHI;
using Engine.Rendering;
using ImGuiNET;

namespace Engine.Editor;

public sealed class EditorModule : IUpdatableModule
{
    public const string LogCategory = "Editor";

    private readonly RhiModule _rhiModule;
    private readonly RendererModule _renderer;
    private readonly string _shaderDirectory;
    private readonly string? _assetsDirectory;
    private readonly string? _layoutPath;
    private readonly Scene.SceneHost _sceneHost = new();
    private SceneHostAdapter? _sceneHostAdapter;
    private ImGuiBackend? _backend;
    private EditorOverlay? _overlay;
    private ILogger? _log;
    private IEngineContext? _engineContext;
    private MemoryLogSink? _memoryLogSink;
    private IWindow? _window;
    private Scene.Scene? _scene;
    private ITexture? _viewTexture;
    private ITexture? _gameTexture;
    private bool _initialized;

    public string Name => "Editor";

    public Scene.SceneHost SceneHost => _sceneHost;

    public Scene.Scene? ActiveScene => _sceneHost.ActiveScene;

    public EditorContext? Context => _overlay?.Context;

    public Engine.Assets.MaterialAssetStore? MaterialStore { get; set; }

    public Engine.Assets.AssetDatabase? ModelAssets { get; set; }

    public EditorModule(
        RhiModule rhiModule,
        RendererModule renderer,
        string shaderDirectory,
        string? assetsDirectory = null,
        string? layoutPath = null)
    {
        _rhiModule = rhiModule;
        _renderer = renderer;
        _shaderDirectory = shaderDirectory;
        _assetsDirectory = assetsDirectory;
        _layoutPath = layoutPath;
    }

    public void AttachScene(Scene.Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        _scene = scene;
        _sceneHost.ActiveScene = scene;
        if (_overlay is null)
            CreateOverlay();
    }

    public void Initialize(IEngineContext context)
    {
        _engineContext = context;
        _log = context.Logging.CreateLogger(LogCategory);
        _memoryLogSink = new MemoryLogSink();
        context.Logging.AddSink(_memoryLogSink);
        if (_rhiModule.Instance is not IRHI rhi)
            throw new InvalidOperationException("Editor requires an initialized RHI module.");
        _window = context.Services.Resolve<IWindow>();
        _window.KeyPressed += OnKeyPressed;
        _backend = new ImGuiBackend(rhi.Device, _window, _log, _shaderDirectory, _renderer.OverlayFormat, ResolveLayoutPath());
        _renderer.EnableEditorMode(new Size2D(1280, 720));
        CreateOverlay();
        _initialized = true;
    }

    public bool ViewportHovered => _overlay?.ViewportHovered ?? false;

    public bool SceneViewFocused => _overlay?.SceneViewFocused ?? false;

    public bool WantCaptureMouse => _backend is not null && ImGui.GetIO().WantCaptureMouse;

    private void CreateOverlay()
    {
        if (_overlay is not null)
        {
            _overlay.VSyncChanged -= OnVSyncChanged;
            _overlay.Dispose();
        }
        _overlay = null;
        if (_sceneHostAdapter is not null)
        {
            _sceneHostAdapter.ActiveSceneChanged -= OnActiveSceneChanged;
            _sceneHostAdapter.Dispose();
            _sceneHostAdapter = null;
        }
        if (_scene is null || _memoryLogSink is null || _window is null)
            return;
        _sceneHost.ActiveScene = _scene;
        _sceneHostAdapter = new SceneHostAdapter(_sceneHost);
        _sceneHostAdapter.ActiveSceneChanged += OnActiveSceneChanged;
        SceneDocument document = new(_sceneHostAdapter, _scene);
        EditorContext editorContext = new(document, ResolveAssetsPath(), MaterialStore) { ModelAssets = ModelAssets };
        _overlay = new EditorOverlay(
            _renderer,
            editorContext,
            _memoryLogSink,
            _rhiModule.Instance?.VSync ?? true);
        _overlay.VSyncChanged += OnVSyncChanged;
    }

    private void OnActiveSceneChanged(object? sender, SceneChangedEventArgs args)
    {
        _scene = args.Current;
        _renderer.AttachScene(args.Current);
    }

    private string ResolveAssetsPath() => string.IsNullOrWhiteSpace(_assetsDirectory)
        ? ResolveProjectPath("Assets")
        : Path.GetFullPath(_assetsDirectory);

    private string ResolveLayoutPath() => string.IsNullOrWhiteSpace(_layoutPath)
        ? Path.Combine(ResolveProjectPath("Config"), "EditorLayout.ini")
        : Path.GetFullPath(_layoutPath);

    private static string ResolveProjectPath(string directory)
    {
        string? current = Directory.GetCurrentDirectory();
        for (int index = 0; index < 6 && current is not null; index++)
        {
            string engineDirectory = Path.Combine(current, "Engine");
            if (File.Exists(Path.Combine(current, "Engine.sln")) && Directory.Exists(engineDirectory))
                return Path.Combine(current, directory);
            current = Path.GetDirectoryName(current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        return Path.Combine(Directory.GetCurrentDirectory(), directory);
    }

    private void OnKeyPressed(KeyCode key, KeyModifiers modifiers)
    {
        if (_overlay is null || _backend is null)
            return;
        bool textInputActive = ImGui.GetIO().WantTextInput || ImGui.IsAnyItemActive();
        _overlay.HandleHotkey(key, modifiers, textInputActive);
    }

    private void OnVSyncChanged(bool value)
    {
        if (_rhiModule.Instance is not null)
            _rhiModule.Instance.VSync = value;
    }

    public void Update(in FrameTime frameTime)
    {
        if (!_initialized || _backend is null)
            return;
        _renderer.SetViewExtent(_overlay?.DesiredViewExtent ?? new Size2D(1280, 720));
        if (_overlay is not null && _renderer.ViewTexture is ITexture viewTexture)
        {
            if (_viewTexture is not null && !ReferenceEquals(_viewTexture, viewTexture))
                _backend.RemoveTexture(_viewTexture);
            _viewTexture = viewTexture;
            _overlay.SetViewTexture(viewTexture, _backend.GetTextureId(viewTexture));
        }
        if (_overlay is not null && _renderer.GameTexture is ITexture gameTexture)
        {
            if (_gameTexture is not null && !ReferenceEquals(_gameTexture, gameTexture))
                _backend.RemoveTexture(_gameTexture);
            _gameTexture = gameTexture;
            _overlay.SetGameTexture(gameTexture, _backend.GetTextureId(gameTexture));
        }
        _backend.NewFrame(frameTime);
        _overlay?.Draw();
        ImGui.Render();
        _renderer.OverlayRender = _backend.Render;
    }

    public void Shutdown()
    {
        if (_overlay is not null)
        {
            _overlay.VSyncChanged -= OnVSyncChanged;
            _overlay.Dispose();
        }
        _overlay = null;
        if (_sceneHostAdapter is not null)
        {
            _sceneHostAdapter.ActiveSceneChanged -= OnActiveSceneChanged;
            _sceneHostAdapter.Dispose();
            _sceneHostAdapter = null;
        }
        _rhiModule.Instance?.WaitIdle();
        if (_backend is not null && _viewTexture is not null)
            _backend.RemoveTexture(_viewTexture);
        if (_backend is not null && _gameTexture is not null)
            _backend.RemoveTexture(_gameTexture);
        _viewTexture = null;
        _gameTexture = null;
        _backend?.Dispose();
        _backend = null;
        if (_memoryLogSink is not null && _engineContext is not null)
            _engineContext.Logging.RemoveSink(_memoryLogSink);
        _memoryLogSink = null;
        if (_window is not null)
            _window.KeyPressed -= OnKeyPressed;
        _window = null;
        _engineContext = null;
        _initialized = false;
        _log?.Information("Editor shut down");
    }
}
