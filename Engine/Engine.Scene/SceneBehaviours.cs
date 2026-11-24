using Engine.Core;

namespace Engine.Scene;

public interface ISceneBehaviour
{
    void BehaviourUpdate(in FrameTime frameTime);
    void BehaviourFixedUpdate(in FrameTime frameTime);
}

public sealed class SceneBehavioursModule : IUpdatableModule, IFixedUpdateModule, IDisposable
{
    private readonly List<Scene> _scenes = [];
    private readonly List<GameObject> _destroyQueue = [];
    private bool _initialized;

    public string Name => "Scene Behaviours";

    public bool SimulationActive { get; set; } = true;

    public void Initialize(IEngineContext context)
    {
        ComponentRegistry.RegisterGlobal(() => new Rotator());
        ComponentRegistry.RegisterGlobal(() => new Oscillator());
        ComponentRegistry.RegisterGlobal(() => new LookAt());
        ComponentRegistry.RegisterGlobal(() => new Follow());
        ComponentRegistry.RegisterGlobal(() => new Lifetime());
        DestroyRequested = QueueDestroy;
        _initialized = true;
    }

    public void AttachScene(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (!_scenes.Contains(scene))
            _scenes.Add(scene);
    }

    public void DetachScene(Scene scene) => _scenes.Remove(scene);

    public void Shutdown()
    {
        DestroyRequested = null;
        _scenes.Clear();
        _destroyQueue.Clear();
        _initialized = false;
    }

    public void Dispose() => Shutdown();

    public void Update(in FrameTime frameTime)
    {
        if (!_initialized)
            return;
        foreach (Scene scene in _scenes)
        {
            foreach (GameObject gameObject in scene.EnumerateHierarchy())
            {
                if (!gameObject.IsActiveInHierarchy)
                    continue;
                foreach (Component component in gameObject.Components)
                {
                    if (component is not ISceneBehaviour behaviour || !component.Enabled)
                        continue;
                    behaviour.BehaviourUpdate(frameTime);
                }
            }
        }
        ProcessDestroyQueue();
    }

    public void FixedUpdate(in FrameTime frameTime)
    {
        if (!_initialized || !SimulationActive)
            return;
        foreach (Scene scene in _scenes)
        {
            foreach (GameObject gameObject in scene.EnumerateHierarchy())
            {
                if (!gameObject.IsActiveInHierarchy)
                    continue;
                foreach (Component component in gameObject.Components)
                {
                    if (component is not ISceneBehaviour behaviour || !component.Enabled)
                        continue;
                    behaviour.BehaviourFixedUpdate(frameTime);
                }
            }
        }
        ProcessDestroyQueue();
    }

    public static void RequestDestroy(GameObject gameObject) => DestroyRequested?.Invoke(gameObject);

    internal static Action<GameObject>? DestroyRequested { get; set; }

    internal void QueueDestroy(GameObject gameObject)
    {
        if (!_destroyQueue.Contains(gameObject))
            _destroyQueue.Add(gameObject);
    }

    private void ProcessDestroyQueue()
    {
        while (_destroyQueue.Count > 0)
        {
            GameObject gameObject = _destroyQueue[^1];
            _destroyQueue.RemoveAt(_destroyQueue.Count - 1);
            if (_scenes.Contains(gameObject.Scene))
                gameObject.Scene.Remove(gameObject);
        }
    }
}
