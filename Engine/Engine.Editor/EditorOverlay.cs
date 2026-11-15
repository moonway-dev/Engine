using Engine.Core;
using Engine.Editor.Input;
using Engine.Editor.Commands;
using Engine.Editor.DragDrop;
using Engine.Editor.Models;
using Engine.Editor.Panels;
using Engine.Mathematics;
using Engine.Platform;
using Engine.RHI;
using Engine.Rendering;
using Engine.Scene;
using ImGuiNET;

namespace Engine.Editor;

public sealed class EditorOverlay : IDisposable
{
    private readonly EditorContext _context;
    private readonly ToolbarPanel _toolbar;
    private readonly HierarchyPanel _hierarchy;
    private readonly InspectorPanel _inspector;
    private readonly ProjectBrowserPanel _projectBrowser;
    private readonly ConsolePanel _console;
    private readonly StatsPanel _stats;
    private readonly ViewportPanel _sceneView;
    private readonly ViewportPanel _gameView;
    private readonly List<IEditorPanel> _panels;
    private readonly EditorHotkeyService _hotkeys = new();
    private readonly RendererModule _renderer;
    private readonly TransformGizmo _gizmo;
    private readonly List<(Engine.Scene.Scene Scene, string Path, Vector3 Position)> _pendingModels = [];
    private bool _showDemo;
    private bool _disposed;

    public event Action<bool>? VSyncChanged;

    public EditorContext Context => _context;

    public bool SceneViewFocused => _sceneView.IsHovered;

    public Size2D DesiredViewExtent => _sceneView.DesiredExtent;

    public bool ViewportHovered => _sceneView.IsHovered;

    public bool WantCaptureMouse => ImGui.GetIO().WantCaptureMouse;

    public GameObject? SelectedObject => _context.Document.SelectedObject;

    public EditorOverlay(
        RendererModule renderer,
        EditorContext context,
        MemoryLogSink logSink,
        bool vsync)
    {
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _gizmo = new TransformGizmo(context, renderer);
        _toolbar = new ToolbarPanel(context);
        _hierarchy = new HierarchyPanel(context);
        _inspector = new InspectorPanel(context);
        _projectBrowser = new ProjectBrowserPanel(context);
        _console = new ConsolePanel(logSink);
        _stats = new StatsPanel(context, renderer, vsync);
        _sceneView = new ViewportPanel("Scene View", tracksDesiredExtent: true);
        _gameView = new ViewportPanel("Game View", tracksDesiredExtent: false);
        _panels = [_hierarchy, _inspector, _projectBrowser, _console, _stats, _sceneView, _gameView];
        _stats.VSyncChanged += OnVSyncChanged;
        RegisterDefaultHotkeys();
    }

    public bool HandleHotkey(KeyCode key, KeyModifiers modifiers, bool textInputActive) =>
        _hotkeys.TryHandle(key, modifiers, textInputActive);

    public void Draw()
    {
        try { _context.RefreshMaterials(); }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException)
        {
            _context.Error = exception.Message;
        }
        ProcessPendingModels();
        _context.ModelAssets?.PumpLodUploads();
        ImGui.DockSpaceOverViewport(0, ImGui.GetMainViewport(), ImGuiDockNodeFlags.None);
        _toolbar.Draw(DrawWindowMenu);
        foreach (IEditorPanel panel in _panels)
            panel.Draw();
        _renderer.SceneViewVisible = _sceneView.IsRenderVisible;
        _renderer.GameViewVisible = _gameView.IsRenderVisible;
        HandleSceneViewInteraction();
        if (_context.Error is { } error)
        {
            ImGui.Begin("Material Error");
            ImGui.TextWrapped(error);
            if (ImGui.Button("Dismiss"))
                _context.Error = null;
            ImGui.End();
        }
        if (_showDemo)
            ImGui.ShowDemoWindow(ref _showDemo);
    }

    public void SetViewTexture(ITexture texture, IntPtr textureId) =>
        _sceneView.SetTexture(texture, textureId);

    public void SetGameTexture(ITexture texture, IntPtr textureId) =>
        _gameView.SetTexture(texture, textureId);

    private void HandleSceneViewInteraction()
    {
        HandleSceneViewAssetDrop();
        if (_sceneView.ImageClicked)
            _context.SelectScene();
        bool gizmoConsumed = _context.SelectedMaterial is null && _gizmo.Draw(_sceneView);
        if (_sceneView.ImageClicked && !gizmoConsumed)
        {
            Vector2 normalized = new(_sceneView.MouseNormalized.X, _sceneView.MouseNormalized.Y);
            PickingHit? hit = _renderer.PickEditorView(normalized);
            _context.Document.Select(hit?.GameObject);
        }
        GameObject? selected = _context.Document.SelectedObject;
        if (selected is null || _context.SelectedMaterial is not null || !_sceneView.IsHovered)
            return;
        Transform transform = selected.Transform;
        float grid = 0.25f;
        Vector3 position = transform.LocalPosition;
        Vector3 scale = transform.LocalScale;
        Quaternion rotation = transform.LocalRotation;
        switch (_context.TransformMode)
        {
            case TransformMode.Translate when ImGui.IsKeyPressed(ImGuiKey.UpArrow):
                ExecuteTransform("Move", (selected.Id, "position"), () => transform.LocalPosition, value => transform.LocalPosition = value, position + Vector3.Up * grid);
                break;
            case TransformMode.Translate when ImGui.IsKeyPressed(ImGuiKey.DownArrow):
                ExecuteTransform("Move", (selected.Id, "position"), () => transform.LocalPosition, value => transform.LocalPosition = value, position - Vector3.Up * grid);
                break;
            case TransformMode.Translate when ImGui.IsKeyPressed(ImGuiKey.RightArrow):
                ExecuteTransform("Move", (selected.Id, "position"), () => transform.LocalPosition, value => transform.LocalPosition = value, position + Vector3.Right * grid);
                break;
            case TransformMode.Translate when ImGui.IsKeyPressed(ImGuiKey.LeftArrow):
                ExecuteTransform("Move", (selected.Id, "position"), () => transform.LocalPosition, value => transform.LocalPosition = value, position - Vector3.Right * grid);
                break;
            case TransformMode.Rotate when ImGui.IsKeyPressed(ImGuiKey.RightArrow):
                ExecuteTransform("Rotate", (selected.Id, "rotation"), () => transform.LocalRotation, value => transform.LocalRotation = value, Quaternion.FromAxisAngle(Vector3.Up, MathUtil.DegToRad * 5f) * rotation);
                break;
            case TransformMode.Rotate when ImGui.IsKeyPressed(ImGuiKey.LeftArrow):
                ExecuteTransform("Rotate", (selected.Id, "rotation"), () => transform.LocalRotation, value => transform.LocalRotation = value, Quaternion.FromAxisAngle(Vector3.Up, -MathUtil.DegToRad * 5f) * rotation);
                break;
            case TransformMode.Scale when ImGui.IsKeyPressed(ImGuiKey.UpArrow):
                ExecuteTransform("Scale", (selected.Id, "scale"), () => transform.LocalScale, value => transform.LocalScale = value, scale + Vector3.One * 0.1f);
                break;
            case TransformMode.Scale when ImGui.IsKeyPressed(ImGuiKey.DownArrow):
                ExecuteTransform("Scale", (selected.Id, "scale"), () => transform.LocalScale, value => transform.LocalScale = value, Vector3.Max(scale - Vector3.One * 0.1f, Vector3.One * 0.01f));
                break;
        }
    }

    private void HandleSceneViewAssetDrop()
    {
        if (_sceneView.DroppedAsset is not AssetDragPayload payload)
            return;
        switch (payload.Kind)
        {
            case AssetKind.Prefab:
                InstantiateDroppedPrefab(payload);
                break;
            case AssetKind.Mesh:
                CreateDroppedMesh(payload);
                break;
            case AssetKind.Texture:
                AssignDroppedTexture(payload);
                break;
            case AssetKind.Material:
                if (_context.Document.SelectedObject?.GetComponent<MeshRendererComponent>() is { } renderer)
                {
                    try { _context.AssignMaterial(renderer.Id, payload.RelativePath); }
                    catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
                    {
                        _context.Error = exception.Message;
                    }
                }
                break;
        }
    }

    private void InstantiateDroppedPrefab(AssetDragPayload payload)
    {
        string path = ResolveAssetPath(payload.RelativePath);
        Vector3 position = GetDropPosition();
        Guid createdId = Guid.Empty;
        _context.Document.Execute(new SnapshotCommand("Instantiate Prefab", _context.Document, () =>
        {
            PrefabAsset prefab = PrefabAsset.Load(path, payload.RelativePath);
            GameObject created = prefab.Instantiate(_context.Document.Scene);
            created.Transform.Position = position;
            createdId = created.Id;
            _context.Document.Select(created);
        }), false);
        _context.Document.Select(createdId);
    }

    private void CreateDroppedMesh(AssetDragPayload payload)
    {
        Vector3 position = GetDropPosition();
        if (_context.ModelAssets is not null)
        {
            _pendingModels.Add((_context.Document.Scene, payload.RelativePath, position));
            return;
        }
        Guid createdId = Guid.Empty;
        _context.Document.Execute(new SnapshotCommand("Create Mesh", _context.Document, () =>
        {
            GameObject created = _context.Document.Scene.CreateObject(Path.GetFileNameWithoutExtension(payload.RelativePath));
            created.Transform.Position = position;
            created.AddComponent<MeshRendererComponent>().ResourceKey = payload.RelativePath;
            createdId = created.Id;
            _context.Document.Select(created);
        }), false);
        _context.Document.Select(createdId);
    }

    private void ProcessPendingModels()
    {
        if (_context.ModelAssets is not { } assets)
            return;
        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
            _pendingModels.Clear();
        for (int i = _pendingModels.Count - 1; i >= 0; i--)
        {
            var pending = _pendingModels[i];
            if (!ReferenceEquals(pending.Scene, _context.Document.Scene))
            {
                _pendingModels.RemoveAt(i);
                continue;
            }
            Engine.Assets.ModelData? model;
            try
            {
                model = assets.RequestModel(pending.Path);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
            {
                _context.Error = exception.Message;
                _pendingModels.RemoveAt(i);
                continue;
            }
            if (model is null)
            {
                if (assets.IsMeshImportFailed(pending.Path))
                {
                    _context.Error = $"Model import failed: {pending.Path}";
                    _pendingModels.RemoveAt(i);
                }
                continue;
            }
            _pendingModels.RemoveAt(i);
            Guid createdId = Guid.Empty;
            _context.Document.Execute(new SnapshotCommand("Create Model", _context.Document, () =>
            {
                GameObject root = _context.Document.Scene.CreateObject(Path.GetFileNameWithoutExtension(pending.Path));
                root.Transform.Position = pending.Position;
                if (model.Parts.Length is > 0 and <= 256)
                {
                    foreach (var part in model.Parts)
                    {
                        GameObject child = _context.Document.Scene.CreateObject(part.Name);
                        child.SetParent(root);
                        MeshRendererComponent renderer = child.AddComponent<MeshRendererComponent>();
                        renderer.ResourceKey = pending.Path;
                        renderer.PartId = part.Id;
                    }
                }
                else
                {
                    root.AddComponent<MeshRendererComponent>().ResourceKey = pending.Path;
                    if (model.Parts.Length > 256)
                        _context.Error = "Model has more than 256 parts; kept combined to avoid excessive draw calls.";
                }
                createdId = root.Id;
                _context.Document.Select(root);
            }), false);
            _context.Document.Select(createdId);
        }
    }

    private void AssignDroppedTexture(AssetDragPayload payload)
    {
        GameObject? selected = _context.Document.SelectedObject;
        MeshRendererComponent? component = selected?.GetComponent<MeshRendererComponent>();
        if (component is null)
            return;
        if (!component.MaterialAsset.IsEmpty)
        {
            _context.SelectMaterial(component.MaterialAsset);
            _context.Error = "This object uses a shared material. Drop the texture onto the desired slot in its Inspector.";
            return;
        }
        AssetReference before = component.AlbedoTexture;
        AssetReference after = payload.RelativePath;
        Guid componentId = component.Id;
        _context.Document.Execute(new PropertyCommand<AssetReference>(
            "Set Albedo Texture",
            (componentId, nameof(MeshRendererComponent.AlbedoTexture)),
            () => ResolveMeshRenderer(componentId).AlbedoTexture,
            value => ResolveMeshRenderer(componentId).AlbedoTexture = value,
            before,
            after), false);
    }

    private MeshRendererComponent ResolveMeshRenderer(Guid id) =>
        _context.Document.Scene.FindComponentById(id) as MeshRendererComponent
        ?? throw new InvalidOperationException("The MeshRendererComponent no longer exists.");

    private Vector3 GetDropPosition()
    {
        PickingRay ray = ScenePicking.CreateRay(
            _renderer.EditorCamera,
            new Vector2(_sceneView.MouseNormalized.X, _sceneView.MouseNormalized.Y));
        if (MathF.Abs(ray.Direction.Y) > MathUtil.Epsilon)
        {
            float distance = -ray.Origin.Y / ray.Direction.Y;
            if (distance > 0f)
                return ray.Origin + ray.Direction * distance;
        }
        return ray.Origin + ray.Direction * 5f;
    }

    private string ResolveAssetPath(string relativePath)
    {
        string root = Path.GetFullPath(_context.AssetsRoot);
        string path = Path.GetFullPath(Path.Combine(root, relativePath));
        string prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Asset path must remain inside the project Assets directory.");
        return path;
    }

    private void ExecuteTransform<T>(string name, object key, Func<T> read, Action<T> write, T value) =>
        _context.Document.Execute(new PropertyCommand<T>(name, key, read, write, value));

    private void DrawWindowMenu()
    {
        if (ImGui.BeginMenu("Window"))
        {
            foreach (IEditorPanel panel in _panels)
            {
                bool open = panel.IsOpen;
                if (ImGui.MenuItem(panel.Title, null, ref open))
                    panel.IsOpen = open;
            }
            ImGui.Separator();
            ImGui.MenuItem("ImGui Demo", null, ref _showDemo);
            ImGui.EndMenu();
        }
    }

    private void RegisterDefaultHotkeys()
    {
        _hotkeys.Register(new EditorHotkeyBinding("scene.new", "New Scene", new EditorHotkey(KeyCode.N, KeyModifiers.Control), () => _context.Document.New()));
        _hotkeys.Register(new EditorHotkeyBinding("scene.open", "Open Scene", new EditorHotkey(KeyCode.O, KeyModifiers.Control), _toolbar.RequestOpen));
        _hotkeys.Register(new EditorHotkeyBinding("scene.save", "Save Scene", new EditorHotkey(KeyCode.S, KeyModifiers.Control), Save));
        _hotkeys.Register(new EditorHotkeyBinding("scene.saveAs", "Save Scene As", new EditorHotkey(KeyCode.S, KeyModifiers.Control | KeyModifiers.Shift), _toolbar.RequestSaveAs));
        _hotkeys.Register(new EditorHotkeyBinding("edit.undo", "Undo", new EditorHotkey(KeyCode.Z, KeyModifiers.Control), () => _context.Undo())
        {
            CanExecute = () => _context.History.CanUndo,
        });
        _hotkeys.Register(new EditorHotkeyBinding("edit.redo", "Redo", new EditorHotkey(KeyCode.Y, KeyModifiers.Control), () => _context.Redo())
        {
            CanExecute = () => _context.History.CanRedo,
        });
        _hotkeys.Register(new EditorHotkeyBinding("edit.duplicate", "Duplicate", new EditorHotkey(KeyCode.D, KeyModifiers.Control), () => _context.Hierarchy.Duplicate(_context.Document.SelectedObject))
        {
            CanExecute = () => _context.SelectedMaterial is null && _context.Document.SelectedObject is not null,
        });
        _hotkeys.Register(new EditorHotkeyBinding("edit.delete", "Delete", new EditorHotkey(KeyCode.Delete), () => _context.Hierarchy.Delete(_context.Document.SelectedObject))
        {
            CanExecute = () => _context.SelectedMaterial is null && _context.Document.SelectedObject is not null,
        });
        _hotkeys.Register(new EditorHotkeyBinding("edit.rename", "Rename", new EditorHotkey(KeyCode.F2), _hierarchy.BeginRenameSelected)
        {
            CanExecute = () => _context.SelectedMaterial is null && _context.Document.SelectedObject is not null,
        });
        _hotkeys.Register(new EditorHotkeyBinding("transform.translate", "Translate Mode", new EditorHotkey(KeyCode.W), () => _context.TransformMode = TransformMode.Translate));
        _hotkeys.Register(new EditorHotkeyBinding("transform.rotate", "Rotate Mode", new EditorHotkey(KeyCode.E), () => _context.TransformMode = TransformMode.Rotate));
        _hotkeys.Register(new EditorHotkeyBinding("transform.scale", "Scale Mode", new EditorHotkey(KeyCode.R), () => _context.TransformMode = TransformMode.Scale));
    }

    private void Save()
    {
        try
        {
            _context.SaveActive();
        }
        catch (InvalidOperationException) when (_context.SelectedMaterial is null && _context.Document.Path is null)
        {
            _toolbar.RequestSaveAs();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            _context.Error = exception.Message;
        }
    }

    private void OnVSyncChanged(bool value) => VSyncChanged?.Invoke(value);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _stats.VSyncChanged -= OnVSyncChanged;
        _context.Dispose();
    }
}
