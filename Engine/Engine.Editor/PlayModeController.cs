namespace Engine.Editor;

using Engine.Editor.Documents;
using Engine.Scene;
using SceneModel = Engine.Scene.Scene;
using Engine.Core;
using Engine.Scripting;

public enum EditorPlayState
{
    Stopped,
    Playing,
    Paused,
}

public sealed class PlayModeController : IDisposable
{
    private readonly EditorContext _context;
    private readonly ScriptingModule? _scripts;
    private readonly Engine.Physics.PhysicsModule? _physics;
    private readonly Engine.Scene.SceneBehavioursModule? _behaviours;
    private SceneModel? _editScene;
    private SceneModel? _playScene;
    private bool _wasDirtyBeforePlay;
    private bool _suppressSceneWatch;

    public EditorPlayState State { get; private set; } = EditorPlayState.Stopped;

    public event EventHandler? StateChanged;

    public PlayModeController(EditorContext context, ScriptingModule? scripts, Engine.Physics.PhysicsModule? physics = null, Engine.Scene.SceneBehavioursModule? behaviours = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _scripts = scripts;
        _physics = physics;
        _behaviours = behaviours;
        _context.Document.SceneChanged += OnDocumentSceneChanged;
    }

    public void EnterPlay()
    {
        if (State != EditorPlayState.Stopped)
            return;
        SceneDocument document = _context.Document;
        _editScene = document.Scene;
        _wasDirtyBeforePlay = document.IsDirty;
        SceneModel playScene = _editScene.Clone(CloneIdPolicy.PreserveIds);
        _playScene = playScene;
        document.EnterPlayMode(playScene);
        _scripts?.BeginPlay(playScene);
        if (_physics is not null)
            _physics.SimulationActive = true;
        if (_behaviours is not null)
            _behaviours.SimulationActive = true;
        State = EditorPlayState.Playing;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ExitPlay()
    {
        if (State == EditorPlayState.Stopped)
            return;
        _suppressSceneWatch = true;
        try
        {
            _scripts?.EndPlay(_context.Document.Scene);
            _context.Document.ExitPlayMode(_editScene!);
            if (_physics is not null)
                _physics.SimulationActive = false;
            if (_behaviours is not null)
                _behaviours.SimulationActive = false;
        }
        finally
        {
            _suppressSceneWatch = false;
        }
        _playScene = null;
        _editScene = null;
        State = EditorPlayState.Stopped;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnDocumentSceneChanged(object? sender, SceneChangedEventArgs args)
    {
        if (_suppressSceneWatch || State == EditorPlayState.Stopped)
            return;
        if (_playScene is not null && ReferenceEquals(args.Current, _playScene))
            return;
        _scripts?.EndPlay(_playScene!);
        if (_physics is not null)
            _physics.SimulationActive = false;
        if (_behaviours is not null)
            _behaviours.SimulationActive = false;
        _playScene = null;
        _editScene = null;
        State = EditorPlayState.Stopped;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void TogglePlay()
    {
        if (State == EditorPlayState.Stopped)
            EnterPlay();
        else
            ExitPlay();
    }

    public void TogglePause()
    {
        if (State == EditorPlayState.Playing)
            Pause();
        else if (State == EditorPlayState.Paused)
            Resume();
    }

    public void Pause()
    {
        if (State != EditorPlayState.Playing)
            return;
        _scripts?.Pause();
        if (_physics is not null)
            _physics.SimulationActive = false;
        if (_behaviours is not null)
            _behaviours.SimulationActive = false;
        State = EditorPlayState.Paused;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Resume()
    {
        if (State != EditorPlayState.Paused)
            return;
        _scripts?.Resume();
        if (_physics is not null)
            _physics.SimulationActive = true;
        if (_behaviours is not null)
            _behaviours.SimulationActive = true;
        State = EditorPlayState.Playing;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Step()
    {
        if (State != EditorPlayState.Paused)
            return;
        FrameTime frameTime = _scripts?.LastFrameTime ?? new FrameTime(1f / 60f, 1f / 60f, 0f, 1f / 60f, 0);
        _physics?.SimulateOnce(frameTime);
        _scripts?.Step();
    }

    public void Dispose() => _context.Document.SceneChanged -= OnDocumentSceneChanged;
}
