using Engine.Scene;

namespace Engine.Editor.Documents;

public interface ISceneHost
{
    Scene.Scene? ActiveScene { get; }

    event EventHandler<SceneChangedEventArgs>? ActiveSceneChanged;

    void SetActiveScene(Scene.Scene scene);
}

public sealed class SceneChangedEventArgs : EventArgs
{
    public Scene.Scene? Previous { get; }

    public Scene.Scene Current { get; }

    public SceneChangedEventArgs(Scene.Scene? previous, Scene.Scene current)
    {
        Previous = previous;
        Current = current;
    }
}

public sealed class SceneHostAdapter : ISceneHost, IDisposable
{
    private readonly SceneHost _host;

    public Scene.Scene? ActiveScene => _host.ActiveScene;

    public event EventHandler<SceneChangedEventArgs>? ActiveSceneChanged;

    public SceneHostAdapter(SceneHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _host.ActiveSceneChanged += OnActiveSceneChanged;
    }

    public void SetActiveScene(Scene.Scene scene) => _host.ActiveScene = scene;

    private void OnActiveSceneChanged(object? sender, ActiveSceneChangedEventArgs args)
    {
        if (args.Current is not null)
            ActiveSceneChanged?.Invoke(this, new SceneChangedEventArgs(args.Previous, args.Current));
    }

    public void Dispose() => _host.ActiveSceneChanged -= OnActiveSceneChanged;
}

public sealed class DelegatingSceneHost : ISceneHost
{
    private readonly Action<Scene.Scene>? _onSceneChanged;
    private Scene.Scene? _scene;

    public Scene.Scene? ActiveScene => _scene;

    public event EventHandler<SceneChangedEventArgs>? ActiveSceneChanged;

    public DelegatingSceneHost(Scene.Scene? initialScene = null, Action<Scene.Scene>? onSceneChanged = null)
    {
        _scene = initialScene;
        _onSceneChanged = onSceneChanged;
    }

    public void SetActiveScene(Scene.Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (ReferenceEquals(_scene, scene))
            return;
        Scene.Scene? previous = _scene;
        _scene = scene;
        _onSceneChanged?.Invoke(scene);
        ActiveSceneChanged?.Invoke(this, new SceneChangedEventArgs(previous, scene));
    }
}
