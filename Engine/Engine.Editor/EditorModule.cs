using Camera = Engine.Rendering.Camera;
using Light = Engine.Rendering.Light;
using Engine.Core;
using Engine.Physics;
using Engine.Scene;
using Engine.Editor.Documents;
using Engine.Mathematics;
using Engine.Platform;
using Engine.RHI;
using Engine.Rendering;
using ImGuiNET;
using System.Text.Json;

namespace Engine.Editor;

public sealed class EditorModule : IUpdatableModule
{
    public const string LogCategory = "Editor";

    private readonly RhiModule _rhiModule;
    private readonly RendererModule _renderer;
    private readonly string _shaderDirectory;
    private string? _assetsDirectory;
    private readonly string? _layoutPath;
    private readonly Scene.SceneHost _sceneHost = new();
    private SceneHostAdapter? _sceneHostAdapter;
    private ImGuiBackend? _backend;
    private AssetPreviewCache? _previews;
    private GpuResourceRetirement? _resourceRetirement;
    private EditorOverlay? _overlay;
    private ILogger? _log;
    private IEngineContext? _engineContext;
    private MemoryLogSink? _memoryLogSink;
    private IWindow? _window;
    private Scene.Scene? _scene;
    private string? _pendingScenePath;
    private ITexture? _viewTexture;
    private ITexture? _gameTexture;
    private bool _initialized;
    private readonly Queue<(KeyCode Key, KeyModifiers Modifiers)> _pendingHotkeys = new();

    public string Name => "Editor";

    public Scene.SceneHost SceneHost => _sceneHost;

    public Scene.Scene? ActiveScene => _sceneHost.ActiveScene;

    public EditorContext? Context => _overlay?.Context;

    public Engine.Assets.MaterialAssetStore? MaterialStore { get; set; }

    public Engine.Assets.AssetDatabase? ModelAssets { get; set; }

    public Engine.Scripting.ScriptingModule? ScriptRuntime { get; set; }

    public Engine.Physics.PhysicsModule? Physics { get; set; }

    public Engine.Scene.SceneBehavioursModule? Behaviours { get; set; }

    public PlayModeController? PlayMode { get; private set; }

    public string EngineRoot { get; set; } = string.Empty;

    public event Action<string>? ProjectOpenRequested;

    public void RequestProjectOpen(string projectFilePath) => ProjectOpenRequested?.Invoke(projectFilePath);

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

    public void AttachScene(Scene.Scene scene, string? scenePath = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        Scene.Scene? previous = _sceneHost.ActiveScene;
        bool switching = previous is not null && !ReferenceEquals(previous, scene);
        _scene = scene;
        _pendingScenePath = scenePath;
        if (switching)
            _suppressActiveSceneChanged = true;
        try
        {
            _sceneHost.ActiveScene = scene;
        }
        finally
        {
            _suppressActiveSceneChanged = false;
        }
        if (_overlay is null)
            CreateOverlay();
        if (switching)
        {
            _renderer.AttachScene(scene);
            if (scenePath is not null)
            {
                try
                {
                    Engine.Editor.ProjectSettings.WriteLastScene(ResolveAssetsPath(), scenePath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private bool _suppressActiveSceneChanged;

    public void SetAssetsRoot(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _assetsDirectory = Path.GetFullPath(path);
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
        _window.FocusChanged += OnFocusChanged;
        Inspection.ScriptComponentInspector.Register();
        _backend = new ImGuiBackend(rhi.Device, _window, _log, _shaderDirectory, _renderer.OverlayFormat, ResolveLayoutPath(), Path.Combine(ResolveAssetsPath(), "Cache", "Shaders"));
        _resourceRetirement = new GpuResourceRetirement((int)rhi.FramesInFlight);
        _backend.RetireResource = _resourceRetirement.Retire;
        _renderer.PrepareEditorResources = PrepareEditorResources;
        _renderer.RenderEditorResources = RenderEditorResources;
        _renderer.EnableEditorMode(new Size2D(1280, 720));
        CreateOverlay();
        _initialized = true;
    }

    public bool ViewportHovered => _overlay?.ViewportHovered ?? false;

    public bool SceneViewFocused => _overlay?.SceneViewFocused ?? false;

    public System.Numerics.Vector2 SceneViewCenter => _overlay?.SceneViewCenter ?? System.Numerics.Vector2.Zero;

    public System.Numerics.Vector2 SceneViewWindowPosition => _overlay?.SceneViewWindowPosition ?? System.Numerics.Vector2.Zero;

    public System.Numerics.Vector2 GameViewCenter => _overlay?.GameViewCenter ?? System.Numerics.Vector2.Zero;

    public bool ApplicationFocused => _window?.IsFocused ?? false;

    public bool WantCaptureMouse => _overlay?.WantCaptureMouse ?? false;

    public bool WantCaptureKeyboard => _overlay?.WantCaptureKeyboard ?? false;

    public bool WantTextInput => _overlay?.WantTextInput ?? false;

    public bool SceneViewMouseCaptured => _overlay?.SceneViewMouseCaptured ?? false;

    public bool IsPlayModeActive => PlayMode is { State: not EditorPlayState.Stopped };

    private System.Numerics.Vector2? _mousePositionOverride;

    public void SetMousePositionOverride(System.Numerics.Vector2? position) => _mousePositionOverride = position;

    private void CreateOverlay()
    {
        if (_previews is not null)
        {
            _rhiModule.Instance?.WaitIdle();
            _previews.Dispose();
            _previews = null;
        }
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
        if (_scene is null || _memoryLogSink is null || _window is null || _backend is null)
            return;
        _sceneHost.ActiveScene = _scene;
        _sceneHostAdapter = new SceneHostAdapter(_sceneHost);
        _sceneHostAdapter.ActiveSceneChanged += OnActiveSceneChanged;
        SceneDocument document = new(_sceneHostAdapter, _scene, _pendingScenePath);
        _pendingScenePath = null;
        EditorContext editorContext = new(document, ResolveAssetsPath(), MaterialStore) { ModelAssets = ModelAssets };
        if (_resourceRetirement is not null && _rhiModule.Instance is { } rhi)
        {
            _previews = new AssetPreviewCache(rhi.Device, editorContext.Materials, editorContext.AssetsRoot,
                () => new AssetPreviewRenderer(rhi.Device, _log!, _shaderDirectory, Path.Combine(editorContext.AssetsRoot, "Cache", "Shaders"), _renderer.ImageBasedLighting!),
                _backend.GetTextureId, _backend.RemoveTexture, _resourceRetirement);
        }
        string projectSettingsPath = ResolveProjectSettingsPath();
        ProjectSettings projectSettings = LoadProjectSettings(projectSettingsPath);
        projectSettings.NormalizeLayers();
        Engine.Physics.PhysicsLayers.SetNames(projectSettings.PhysicsLayers);
        projectSettings.ApplyTo(_renderer);
        OnVSyncChanged(projectSettings.VSync);
        PlayMode?.Dispose();
        PlayMode = null;
        PlayMode = new PlayModeController(editorContext, ScriptRuntime, Physics, Behaviours,
            () => projectSettings.LayerCollisionMasks);
        _overlay = new EditorOverlay(
            _renderer,
            editorContext,
            _memoryLogSink,
            projectSettings,
            projectSettingsPath,
            _rhiModule.Instance?.Device.Features,
            _rhiModule.Instance,
            PlayMode,
            name => _backend.GetIconTexture(name),
            entry => _previews?.Request(entry) ?? default,
            path => RequestProjectOpen(path),
            EngineRoot);
        _renderer.ColliderGizmoSource = CollectColliderGizmos;
        _overlay.VSyncChanged += OnVSyncChanged;
    }

    private string? _lastWindowTitle;

    private void UpdateWindowTitle()
    {
        if (_overlay is null || _window is null)
            return;
        string title = $"Engine — {_overlay.Context.ActiveTitle}";
        if (title == _lastWindowTitle)
            return;
        _lastWindowTitle = title;
        _window.Title = title;
    }

    private void CollectColliderGizmos()
    {
        DebugDraw.Clear();
        if (_overlay is null || !_overlay.Context.ShowColliderGizmos)
            return;
        foreach (GameObject gameObject in _overlay.Context.Document.Scene.EnumerateHierarchy())
        {
            if (!gameObject.IsActiveInHierarchy)
                continue;
            foreach (Component component in gameObject.Components)
            {
                if (!component.IsActiveInHierarchy)
                    continue;
                component.OnDrawGizmos();
            }
        }
    }

    private string ResolveProjectSettingsPath()
    {
        string assetsRoot = ResolveAssetsPath();
        string? projectFile = FindProjectFile(assetsRoot);
        if (projectFile is not null)
            return projectFile;
        string? projectRoot = Path.GetDirectoryName(assetsRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrEmpty(projectRoot)
            ? Path.Combine(assetsRoot, "Project.project")
            : Path.Combine(projectRoot, "Project.project");
    }

    private static string? FindProjectFile(string directory)
    {
        string? current = directory;
        for (int depth = 0; depth < 3 && current is not null; depth++)
        {
            try
            {
                string? match = Directory.EnumerateFiles(current, "*.project", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (match is not null)
                    return Path.GetFullPath(match);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return null;
            }
            current = Path.GetDirectoryName(current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        return null;
    }

    private ProjectSettings LoadProjectSettings(string path)
    {
        try
        {
            return ProjectSettings.Load(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
        {
            _log?.Warning($"Failed to load project settings from '{path}': {exception.Message}");
            return new ProjectSettings();
        }
    }

    private void OnActiveSceneChanged(object? sender, SceneChangedEventArgs args)
    {
        if (_suppressActiveSceneChanged)
            return;
        _scene = args.Current;
        _renderer.AttachScene(args.Current);
        string? openedScene = _overlay?.Context.Document.Path;
        if (openedScene is not null)
        {
            try
            {
                Engine.Editor.ProjectSettings.WriteLastScene(ResolveAssetsPath(), openedScene);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private string ResolveAssetsPath() => string.IsNullOrWhiteSpace(_assetsDirectory)
        ? ResolveProjectPath(Path.Combine("Projects", "Showcase"))
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
        if (_overlay is not null && _pendingHotkeys.Count < 32)
            _pendingHotkeys.Enqueue((key, modifiers));
    }

    private void OnFocusChanged(bool focused)
    {
        if (!focused)
            _pendingHotkeys.Clear();
    }

    private void ProcessPendingHotkeys()
    {
        if (_overlay is null || _backend is null)
        {
            _pendingHotkeys.Clear();
            return;
        }
        bool textInputActive = ImGui.GetIO().WantTextInput || ImGui.IsAnyItemActive() || ImGui.IsAnyItemFocused();
        while (_pendingHotkeys.TryDequeue(out (KeyCode Key, KeyModifiers Modifiers) input))
            _overlay.HandleHotkey(input.Key, input.Modifiers, textInputActive);
    }

    private System.Numerics.Vector2? ResolveMousePositionOverride()
    {
        if (_mousePositionOverride is { } explicitOverride)
            return explicitOverride;
        if (IsPlayModeActive && _window is { CursorMode: CursorMode.Disabled })
            return _overlay?.GameViewCenter;
        return null;
    }

    private bool _playModeCursorLockDesired;

    private void TrackPlayModeCursorLock()
    {
        if (_window is null)
            return;
        if (!IsPlayModeActive)
        {
            _playModeCursorLockDesired = false;
            return;
        }
        if (_window.CursorMode == CursorMode.Disabled)
            _playModeCursorLockDesired = true;
        if (_window.Keyboard.IsPressed(KeyCode.Escape))
        {
            _playModeCursorLockDesired = true;
            if (_window.CursorMode != CursorMode.Normal)
                _window.CursorMode = CursorMode.Normal;
            return;
        }
        if (_overlay is { GameViewFocused: false, GameViewHovered: false })
        {
            if (_window.CursorMode != CursorMode.Normal)
                _window.CursorMode = CursorMode.Normal;
            return;
        }
        if (_playModeCursorLockDesired
            && _overlay is { GameViewHovered: true }
            && _window.Mouse.IsReleased(MouseButton.Left)
            && _window.CursorMode != CursorMode.Disabled)
            _window.CursorMode = CursorMode.Disabled;
    }

    private void OnVSyncChanged(bool value)
    {
        if (_rhiModule.Instance is not null)
            _rhiModule.Instance.VSync = value;
    }

    private void PrepareEditorResources(int frameSlot)
    {
        _resourceRetirement?.BeginFrame(frameSlot);
        _previews?.PrepareFrame();
    }

    private void RenderEditorResources(ICommandBuffer commands, int frameSlot) => _previews?.Record(commands);

    public void Update(in FrameTime frameTime)
    {
        if (!_initialized || _backend is null)
            return;
        if (_overlay is not null)
            _renderer.SetViewExtents(_overlay.DesiredViewExtent, _overlay.DesiredGameViewExtent);
        _renderer.RefreshTargets();
        _backend.MousePositionOverride = ResolveMousePositionOverride();
        if (_overlay is not null)
            _overlay.SuppressViewportClicks = ResolveMousePositionOverride() is not null;
        TrackPlayModeCursorLock();
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
        ProcessPendingHotkeys();
        _previews?.BeginRequests();
        _overlay?.Draw();
        UpdateWindowTitle();
        ImGui.Render();
        _renderer.OverlayRender = _backend.Render;
    }

    public void Shutdown()
    {
        _rhiModule.Instance?.WaitIdle();
        _renderer.PrepareEditorResources = null;
        _renderer.RenderEditorResources = null;
        _previews?.Dispose();
        _previews = null;
        _resourceRetirement?.Dispose();
        _resourceRetirement = null;
        if (_backend is not null)
            _backend.RetireResource = null;
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
        PlayMode?.Dispose();
        PlayMode = null;
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
        {
            _window.KeyPressed -= OnKeyPressed;
            _window.FocusChanged -= OnFocusChanged;
        }
        _pendingHotkeys.Clear();
        _window = null;
        _engineContext = null;
        _initialized = false;
        _log?.Information("Editor shut down");
    }
}
