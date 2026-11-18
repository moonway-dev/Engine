using Engine.Editor.Commands;
using Engine.Scene;
using SceneModel = Engine.Scene.Scene;

namespace Engine.Editor.Documents;

public sealed class SceneDocument : IEditorSnapshotSource, IDisposable
{
    private readonly ISceneHost _host;
    private bool _disposed;
    private bool _suppressHostChange;
    private Guid? _selectedObjectId;
    private bool _dirtyBeforePlay;

    public event EventHandler? Changed;

    public event EventHandler? SelectionChanged;

    public event EventHandler<SceneChangedEventArgs>? SceneChanged;

    public CommandHistory History { get; }

    public Scene.Scene Scene => _host.ActiveScene
        ?? throw new InvalidOperationException("The scene document has no active scene.");

    public string? Path { get; private set; }

    public bool IsUntitled => Path is null;

    public bool IsDirty => !History.IsAtSavePoint;

    public string DisplayName => System.IO.Path.GetFileNameWithoutExtension(Path) is { Length: > 0 } name
        ? name
        : Scene.Name;

    public string Title => $"{DisplayName}{(IsDirty ? "*" : string.Empty)}";

    public Guid? SelectedObjectId => _selectedObjectId;

    public GameObject? SelectedObject => _selectedObjectId is Guid id ? Scene.FindById(id) : null;

    public SceneDocument(ISceneHost host, Scene.Scene? initialScene = null, string? path = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        History = new CommandHistory();
        History.Changed += OnHistoryChanged;
        _host.ActiveSceneChanged += OnHostSceneChanged;
        Path = NormalizePath(path);
        if (initialScene is not null)
            ReplaceScene(initialScene);
        else if (_host.ActiveScene is null)
            ReplaceScene(new Scene.Scene());
        History.Reset(markSaved: true);
    }

    public void Select(GameObject? gameObject)
    {
        if (gameObject is not null && !ReferenceEquals(gameObject.Scene, Scene))
            throw new InvalidOperationException("Cannot select an object from another scene.");
        SetSelection(gameObject?.Id);
    }

    public void Select(Guid? gameObjectId) =>
        SetSelection(gameObjectId is Guid id && Scene.FindById(id) is not null ? id : null);

    public void New(string name = "Untitled")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Path = null;
        SetSelection(null);
        ReplaceScene(new Scene.Scene(name, Scene.ComponentRegistry));
        History.Reset(markSaved: true);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Open(string path)
    {
        string normalized = RequirePath(path);
        SceneModel loaded = SceneModel.Load(normalized, Scene.ComponentRegistry);
        Path = normalized;
        SetSelection(null);
        ReplaceScene(loaded);
        History.Reset(markSaved: true);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Save()
    {
        if (Path is null)
            throw new InvalidOperationException("The scene does not have a path. Use SaveAs first.");
        SaveAs(Path);
    }

    public void SaveAs(string path)
    {
        string normalized = RequirePath(path);
        Scene.Save(normalized);
        Path = normalized;
        History.MarkSavePoint();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Execute(IEditorCommand command, bool allowMerge = true) =>
        History.Execute(command, allowMerge);

    public bool Undo() => History.Undo();

    public bool Redo() => History.Redo();

    public IEditorSnapshot CaptureSnapshot() =>
        new SceneDocumentSnapshot(this, Scene.CaptureSnapshot(), _selectedObjectId);

    private void RestoreSnapshot(SceneSnapshot snapshot, Guid? selection)
    {
        SceneModel restored = snapshot.Restore(CloneIdPolicy.PreserveIds, Scene.ComponentRegistry).Scene;
        ReplaceScene(restored);
        SetSelection(selection is Guid id && restored.FindById(id) is not null ? id : null);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void EnterPlayMode(Scene.Scene playScene)
    {
        ArgumentNullException.ThrowIfNull(playScene);
        _dirtyBeforePlay = IsDirty;
        ReplaceScene(playScene);
        SetSelection(_selectedObjectId is Guid playId && playScene.FindById(playId) is not null ? playId : null);
        History.Reset(markSaved: true);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ExitPlayMode(Scene.Scene editScene)
    {
        ArgumentNullException.ThrowIfNull(editScene);
        ReplaceScene(editScene);
        SetSelection(_selectedObjectId is Guid editId && editScene.FindById(editId) is not null ? editId : null);
        History.Reset(markSaved: !_dirtyBeforePlay);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ReplaceScene(Scene.Scene scene)
    {
        Scene.Scene? previous = _host.ActiveScene;
        _suppressHostChange = true;
        try
        {
            _host.SetActiveScene(scene);
        }
        finally
        {
            _suppressHostChange = false;
        }
        SceneChanged?.Invoke(this, new SceneChangedEventArgs(previous, scene));
    }

    private void OnHostSceneChanged(object? sender, SceneChangedEventArgs args)
    {
        if (_suppressHostChange)
            return;
        Path = null;
        SetSelection(null);
        History.Reset(markSaved: true);
        SceneChanged?.Invoke(this, args);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnHistoryChanged(object? sender, EventArgs args) => Changed?.Invoke(this, EventArgs.Empty);

    private void SetSelection(Guid? value)
    {
        if (_selectedObjectId == value)
            return;
        _selectedObjectId = value;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string RequirePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return System.IO.Path.GetFullPath(path);
    }

    private static string? NormalizePath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : System.IO.Path.GetFullPath(path);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        History.Changed -= OnHistoryChanged;
        _host.ActiveSceneChanged -= OnHostSceneChanged;
    }

    private sealed class SceneDocumentSnapshot(
        SceneDocument document,
        SceneSnapshot snapshot,
        Guid? selection) : IEditorSnapshot
    {
        public void Restore() => document.RestoreSnapshot(snapshot, selection);
    }
}
