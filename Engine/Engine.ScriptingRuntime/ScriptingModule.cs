namespace Engine.Scripting;

using System.Text.Json;
using BepuPhysics;
using Engine.Mathematics;
using Engine.Core;
using Engine.Platform;
using Engine.Scene;
using SceneModel = Engine.Scene.Scene;

public sealed class ScriptingModule : IUpdatableModule, IFixedUpdateModule, IDisposable
{
    public const string LogCategory = "Scripting";

    private const double ReloadDebounceSeconds = 0.4;

    private readonly string _scriptsRoot;
    private readonly List<SceneModel> _scenes = [];
    private readonly List<SceneModel> _playingScenes = [];
    private readonly List<(ScriptComponent Component, ScriptBehaviour Behaviour)> _activeInstances = [];
    private readonly HashSet<ScriptBehaviour> _started = [];
    private readonly List<ScriptJoint> _joints = [];
    private readonly List<GameObject> _pendingDestroy = [];
    private ScriptCompilationResult? _current;
    private ScriptLoadContext? _retiredContext;
    private ILogger? _log;
    private FileSystemWatcher? _watcher;
    private DateTime? _reloadQueuedAt;
    private FrameTime _lastFrameTime;
    private bool _paused;
    private bool _stepRequested;
    private bool _lastCompileSuccessful;
    private IReadOnlyList<string> _lastErrors = [];
    private DateTime _lastCompileTime = DateTime.MinValue;

    public string Name => "Scripting";

    public string ScriptsRoot => _scriptsRoot;

    private Engine.Physics.PhysicsModule? _physics;

    public Engine.Physics.PhysicsModule? Physics
    {
        get => _physics;
        set => _physics = value;
    }

    public bool AlwaysPlay { get; set; }

    public string? AssetsRoot { get; set; }

    public bool IsPaused => _paused;

    public bool HasScripts => _current is { Success: true } && _current.Scripts.Count > 0;

    public IReadOnlyList<ScriptTypeRegistration> Scripts =>
        _current is { Success: true } ? _current.Scripts : [];

    public bool LastCompileSuccessful => _lastCompileSuccessful;

    public IReadOnlyList<string> LastErrors => _lastErrors;

    public DateTime LastCompileTime => _lastCompileTime;

    public FrameTime LastFrameTime => _lastFrameTime;

    public event Action? ScriptsReloaded;

    public ScriptingModule(string scriptsRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptsRoot);
        _scriptsRoot = Path.GetFullPath(scriptsRoot);
    }

    public void Initialize(IEngineContext context)
    {
        _log = context.Logging.CreateLogger(LogCategory);
        ScriptLog.Sink = (severity, message) =>
        {
            switch (severity)
            {
                case "Warning":
                    _log?.Warning(message);
                    break;
                case "Error":
                    _log?.Error(message);
                    break;
                default:
                    _log?.Information(message);
                    break;
            }
        };
        try
        {
            ScriptInput.Window = context.Services.Resolve<IWindow>();
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException)
        {
            ScriptInput.Window = null;
        }
        ScriptHost.InstanceFactory = CreateInstance;
        ScriptHost.InstanceReleaser = ReleaseInstance;
        ScriptHost.ScriptsProvider = ListScripts;
        ConfigureScriptPhysics();
        Recompile();
        StartWatcher();
    }

    public void AttachScene(SceneModel scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (!_scenes.Contains(scene))
            _scenes.Add(scene);
        RegisterInto(scene.ComponentRegistry);
        foreach (ScriptComponent component in scene.AllComponents.OfType<ScriptComponent>().ToArray())
        {
            if (string.IsNullOrWhiteSpace(component.Script))
                continue;
            if (component.EnsureInstance())
                component.ApplyPending();
        }
    }

    public void DetachScene(SceneModel scene)
    {
        if (_playingScenes.Contains(scene))
            EndPlay(scene);
        _scenes.Remove(scene);
    }

    public void BeginPlay(SceneModel scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (_playingScenes.Contains(scene))
            return;
        _playingScenes.Add(scene);
        if (!_scenes.Contains(scene))
            _scenes.Add(scene);
        RegisterInto(scene.ComponentRegistry);
        foreach (ScriptComponent component in scene.AllComponents.OfType<ScriptComponent>().ToArray())
        {
            if (string.IsNullOrWhiteSpace(component.Script))
                continue;
            if (!component.EnsureInstance())
                continue;
            component.ApplyPending();
            if (component.Instance is not ScriptBehaviour behaviour)
                continue;
            _activeInstances.Add((component, behaviour));
            SafeRun(component, behaviour, behaviour.OnEnable);
        }
    }

    public void EndPlay(SceneModel scene)
    {
        if (!_playingScenes.Remove(scene))
            return;
        foreach (ScriptComponent component in scene.AllComponents.OfType<ScriptComponent>().ToArray())
        {
            if (component.Instance is ScriptBehaviour behaviour)
            {
                SafeRun(component, behaviour, behaviour.OnDisable);
                SafeRun(component, behaviour, behaviour.OnDestroy);
                _started.Remove(behaviour);
            }
            component.Instance = null;
            _activeInstances.RemoveAll(entry => ReferenceEquals(entry.Component, component));
        }
        ClearJoints();
    }

    public void Pause() => _paused = true;

    public void Resume() => _paused = false;

    public void Step()
    {
        if (_playingScenes.Count == 0)
            return;
        _paused = true;
        _stepRequested = true;
    }

    public void Recompile()
    {
        ScriptCompilationResult result = new ScriptCompiler().Compile(_scriptsRoot);
        _lastCompileTime = DateTime.Now;
        _lastErrors = result.Errors;
        _lastCompileSuccessful = result.Success;
        if (!result.Success)
        {
            foreach (string error in result.Errors)
                _log?.Error(error);
            return;
        }
        foreach (string error in result.Errors)
            _log?.Warning(error);

        ScriptCompilationResult? previous = _current;
        _current = result;
        foreach (ScriptTypeRegistration registration in result.Scripts)
        {
            try
            {
                ComponentRegistry.RegisterGlobal(registration.ComponentType, registration.CreateComponent, registration.Key, overwrite: true);
            }
            catch (InvalidOperationException exception)
            {
                _log?.Error($"Script '{registration.Key}' conflicts with an existing component key: {exception.Message}");
            }
        }
        foreach (SceneModel scene in _scenes)
            RegisterInto(scene.ComponentRegistry);
        foreach (SceneModel scene in _scenes)
            SwapSceneInstances(scene);
        if (previous is not null)
            RetireLoadContext(previous);
        _log?.Information($"Scripts compiled: {result.Scripts.Count} script(s)");
        ScriptsReloaded?.Invoke();
    }

    private void RetireLoadContext(ScriptCompilationResult previous)
    {
        if (_retiredContext is not null)
            return;
        _retiredContext = previous.LoadContext;
        previous.LoadContext?.Unload();
        _retiredContext = null;
    }

    private void RegisterInto(ComponentRegistry registry)
    {
        if (_current is not { Success: true } compilation)
            return;
        foreach (ScriptTypeRegistration registration in compilation.Scripts)
        {
            try
            {
                registry.Register(registration.ComponentType, registration.CreateComponent, registration.Key, overwrite: true);
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    private void SwapSceneInstances(SceneModel scene)
    {
        bool playing = _playingScenes.Contains(scene);
        foreach (ScriptComponent component in scene.AllComponents.OfType<ScriptComponent>().ToArray())
        {
            if (string.IsNullOrWhiteSpace(component.Script))
                continue;
            bool wasTicking = _activeInstances.Any(entry => ReferenceEquals(entry.Component, component));
            Dictionary<string, JsonElement>? captured = component.Instance is null
                ? null
                : SceneSerializer.CaptureProperties(component, out _);
            ScriptHost.DetachInstance(component);
            if (!component.EnsureInstance())
                continue;
            if (captured is not null)
                SceneSerializer.ApplyProperties(component, captured);
            if (!playing || !wasTicking || component.Instance is not ScriptBehaviour behaviour)
                continue;
            _activeInstances.Add((component, behaviour));
            SafeRun(component, behaviour, behaviour.OnEnable);
        }
    }

    private void ConfigureScriptPhysics()
    {
        ScriptPhysics.LinearVelocityGetter = gameObject => WithBody(gameObject, (world, handle) => world.GetBodyLinearVelocity(handle), Vector3.Zero);
        ScriptPhysics.LinearVelocitySetter = (gameObject, velocity) => WithBodyVoid(gameObject, (world, handle) => world.SetBodyVelocity(handle, velocity));
        ScriptPhysics.AngularVelocityGetter = gameObject => WithBody(gameObject, (world, handle) => world.GetBodyAngularVelocity(handle), Vector3.Zero);
        ScriptPhysics.AngularVelocitySetter = (gameObject, velocity) => WithBodyVoid(gameObject, (world, handle) => world.SetBodyVelocity(handle, world.GetBodyLinearVelocity(handle), velocity));
        ScriptPhysics.ImpulseApplier = (gameObject, impulse) => WithBodyVoid(gameObject, (world, handle) => world.ApplyImpulse(handle, impulse));
        ScriptPhysics.ImpulseAtPointApplier = (gameObject, impulse, point) => WithBodyVoid(gameObject, (world, handle) => world.ApplyImpulseAtPoint(handle, impulse, point));
        ScriptPhysics.AngularImpulseApplier = (gameObject, impulse) => WithBodyVoid(gameObject, (world, handle) => world.ApplyAngularImpulse(handle, impulse));
        ScriptPhysics.MassGetter = gameObject => WithBody(gameObject, (world, handle) => world.GetBodyMass(handle), 0f);
        ScriptPhysics.Waker = gameObject => WithBodyVoid(gameObject, (world, handle) => world.SetBodyAwake(handle, true));
        ScriptPhysics.Sleeper = gameObject => WithBodyVoid(gameObject, (world, handle) => world.SetBodyAwake(handle, false));
        ScriptPhysics.SleepingChecker = gameObject => WithBody(gameObject, (world, handle) => !world.IsBodyAwake(handle), false);
        ScriptPhysics.BodyChecker = gameObject => _physics is not null && _physics.TryGetBodyHandle(gameObject, out _);
        ScriptPhysics.GroundedChecker = (gameObject, distance) =>
        {
            Engine.Scene.ScriptRayHit? hit = RayCastInternal(gameObject.Transform.Position, Vector3.Down, distance, gameObject);
            return hit is not null;
        };
        ScriptPhysics.RayCaster = (origin, direction, maximumDistance, ignore) => RayCastInternal(origin, direction, maximumDistance, ignore);
        ScriptPhysics.InterpolatedPoseGetter = gameObject =>
            _physics is not null && _physics.TryGetRenderPose(gameObject, out Vector3 position, out Quaternion rotation)
                ? (position, rotation)
                : (gameObject.Transform.Position, gameObject.Transform.Rotation);
        ScriptPhysics.BallJointCreator = (a, b, anchorA, anchorB) => WithBodies(a, b, (world, handleA, handleB) => new ScriptJoint(world.AddBallJoint(Endpoint(handleA), Endpoint(handleB), anchorA, anchorB).Cast<object>().ToArray()));
        ScriptPhysics.HingeJointCreator = (a, b, anchor, axis) => WithBodies(a, b, (world, handleA, handleB) => new ScriptJoint(world.AddHingeJoint(Endpoint(handleA), Endpoint(handleB), anchor, anchor, axis).Cast<object>().ToArray()));
        ScriptPhysics.WeldJointCreator = (a, b) => WithBodies(a, b, (world, handleA, handleB) => new ScriptJoint(world.AddFixedJoint(Endpoint(handleA), Endpoint(handleB)).Cast<object>().ToArray()));
        ScriptPhysics.DistanceJointCreator = (a, b, minimum, maximum) => WithBodies(a, b, (world, handleA, handleB) => new ScriptJoint(world.AddLinearLimit(Endpoint(handleA), Endpoint(handleB), (b.Transform.Position - a.Transform.Position).Normalized, a.Transform.Position, a.Transform.Position, minimum, maximum).Cast<object>().ToArray()));
        ScriptPhysics.JointDestroyer = DestroyJointInternal;
        ConfigureScriptScene();
    }

    private static BodyHandle Endpoint(BodyHandle handle) => handle;

    private void ConfigureScriptScene()
    {
        ScriptScene.Finder = name => _scenes
            .Select(scene => scene.FindByName(name))
            .FirstOrDefault(match => match is not null);
        ScriptScene.Destroyer = gameObject =>
        {
            if (gameObject is not null && !_pendingDestroy.Contains(gameObject))
                _pendingDestroy.Add(gameObject);
        };
        ScriptScene.PrefabSpawner = SpawnPrefab;
    }

    private GameObject? SpawnPrefab(GameObject context, AssetReference prefab, Vector3? position, Quaternion? rotation)
    {
        if (string.IsNullOrWhiteSpace(AssetsRoot) || prefab.IsEmpty || context is null)
            return null;
        try
        {
            PrefabAsset asset = PrefabAsset.Load(Path.GetFullPath(Path.Combine(AssetsRoot, prefab.Key)), prefab.Key);
            GameObject created = asset.Instantiate(context.Scene, null);
            if (position is { } spawnPosition)
                created.Transform.Position = spawnPosition;
            if (rotation is { } spawnRotation)
                created.Transform.Rotation = spawnRotation;
            return created;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
        {
            _log?.Warning($"Failed to spawn prefab '{prefab.Key}': {exception.Message}");
            return null;
        }
    }

    private ScriptJoint? WithBodies(GameObject a, GameObject b, Func<Engine.Physics.PhysicsWorld, BodyHandle, BodyHandle, ScriptJoint> create)
    {
        if (_physics is null
            || !_physics.TryGetBodyHandle(a, out BodyHandle handleA)
            || !_physics.TryGetBodyHandle(b, out BodyHandle handleB))
            return null;
        try
        {
            ScriptJoint joint = create(_physics.World, handleA, handleB);
            _joints.Add(joint);
            return joint;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private void DestroyJointInternal(ScriptJoint joint)
    {
        if (!joint.IsValid)
            return;
        if (_physics is not null)
        {
            foreach (object constraint in joint.Constraint)
            {
                try
                {
                    _physics.World.RemoveJoint((ConstraintHandle)constraint);
                }
                catch (Exception exception) when (exception is InvalidOperationException or ArgumentOutOfRangeException or KeyNotFoundException)
                {
                }
            }
        }
        _joints.Remove(joint);
        joint.Invalidate();
    }

    private void ClearJoints()
    {
        foreach (ScriptJoint joint in _joints.ToArray())
            DestroyJointInternal(joint);
        _joints.Clear();
        _pendingDestroy.Clear();
    }

    private void ProcessDestroyed()
    {
        while (_pendingDestroy.Count > 0)
        {
            GameObject gameObject = _pendingDestroy[^1];
            _pendingDestroy.RemoveAt(_pendingDestroy.Count - 1);
            try
            {
                gameObject.Scene.Remove(gameObject);
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    private Engine.Scene.ScriptRayHit? RayCastInternal(Vector3 origin, Vector3 direction, float maximumDistance, GameObject? ignore)
    {
        if (_physics is null)
            return null;
        _physics.Synchronize();
        BodyHandle? ignored = null;
        if (ignore is not null && _physics.TryGetBodyHandle(ignore, out BodyHandle ignoreHandle))
            ignored = ignoreHandle;
        try
        {
            return _physics.World.RayCast(origin, direction, maximumDistance, out Engine.Physics.PhysicsRayHit hit, ignored)
                ? new Engine.Scene.ScriptRayHit(origin + direction * hit.Distance, hit.Normal, hit.Distance)
                : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private T WithBody<T>(GameObject gameObject, Func<Engine.Physics.PhysicsWorld, BodyHandle, T> action, T fallback)
    {
        if (_physics is null || !_physics.TryGetBodyHandle(gameObject, out BodyHandle handle))
            return fallback;
        try
        {
            return action(_physics.World, handle);
        }
        catch (InvalidOperationException)
        {
            return fallback;
        }
    }

    private void WithBodyVoid(GameObject gameObject, Action<Engine.Physics.PhysicsWorld, BodyHandle> action)
    {
        if (_physics is null || !_physics.TryGetBodyHandle(gameObject, out BodyHandle handle))
            return;
        try
        {
            action(_physics.World, handle);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private IReadOnlyList<ScriptDescription> ListScripts() =>
        _current is not { Success: true } compilation
            ? []
            : [.. compilation.Scripts.Select(script => new ScriptDescription(script.Key, script.DisplayName, script.ComponentType))];

    private object? CreateInstance(ScriptComponent component)
    {
        if (_current is not { Success: true } compilation)
            return null;
        ScriptTypeRegistration? registration = compilation.Scripts.FirstOrDefault(script =>
            script.Key.Equals(component.Script, StringComparison.OrdinalIgnoreCase));
        if (registration is null || Activator.CreateInstance(registration.BehaviourType) is not ScriptBehaviour behaviour)
            return null;
        behaviour.Component = component;
        return behaviour;
    }

    private void ReleaseInstance(ScriptComponent component, object instance)
    {
        if (instance is not ScriptBehaviour behaviour)
            return;
        if (_playingScenes.Contains(component.Scene))
        {
            SafeRun(component, behaviour, behaviour.OnDisable);
            SafeRun(component, behaviour, behaviour.OnDestroy);
            _activeInstances.RemoveAll(entry => ReferenceEquals(entry.Component, component));
        }
        _started.Remove(behaviour);
    }

    public void Update(in FrameTime frameTime)
    {
        _lastFrameTime = frameTime;
        PumpReload();
        if (AlwaysPlay)
            SyncAlwaysPlayScenes();
        if (_stepRequested)
        {
            _stepRequested = false;
            RunUpdateTick(_lastFrameTime);
            RunFixedTick(_lastFrameTime);
            return;
        }
        if (_paused)
            return;
        RunUpdateTick(frameTime);
    }

    public void FixedUpdate(in FrameTime frameTime)
    {
        if (_paused)
            return;
        ScriptTime.FixedDeltaTime = frameTime.FixedDeltaTime;
        RunFixedTick(frameTime);
    }

    private void RunUpdateTick(in FrameTime frameTime)
    {
        ScriptTime.DeltaTime = frameTime.DeltaTime;
        ScriptTime.UnscaledDeltaTime = frameTime.UnscaledDeltaTime;
        ScriptTime.TotalTime = frameTime.TotalTime;
        for (int index = 0; index < _activeInstances.Count; index++)
        {
            (ScriptComponent component, ScriptBehaviour behaviour) = _activeInstances[index];
            if (!behaviour.IsActive)
                continue;
            if (_started.Add(behaviour))
                SafeRun(component, behaviour, behaviour.OnStart);
            SafeRun(component, behaviour, behaviour.OnUpdate);
        }
        ProcessDestroyed();
    }

    private void RunFixedTick(in FrameTime frameTime)
    {
        for (int index = 0; index < _activeInstances.Count; index++)
        {
            (ScriptComponent component, ScriptBehaviour behaviour) = _activeInstances[index];
            if (!behaviour.IsActive)
                continue;
            SafeRun(component, behaviour, behaviour.OnFixedUpdate);
        }
        ProcessDestroyed();
    }

    private void SyncAlwaysPlayScenes()
    {
        foreach (SceneModel scene in _scenes.ToArray())
        {
            if (!_playingScenes.Contains(scene))
                BeginPlay(scene);
        }
        foreach (SceneModel scene in _playingScenes.ToArray())
        {
            if (!_scenes.Contains(scene))
                EndPlay(scene);
        }
    }

    private void StartWatcher()
    {
        if (!Directory.Exists(_scriptsRoot))
            return;
        try
        {
            FileSystemWatcher watcher = new(_scriptsRoot, "*.cs")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            };
            watcher.Changed += OnScriptFileChanged;
            watcher.Created += OnScriptFileChanged;
            watcher.Deleted += OnScriptFileChanged;
            watcher.Renamed += OnScriptFileRenamed;
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch (IOException exception)
        {
            _log?.Warning($"Script file watcher unavailable: {exception.Message}");
        }
    }

    private void OnScriptFileChanged(object? sender, FileSystemEventArgs args) => QueueReload();

    private void OnScriptFileRenamed(object? sender, RenamedEventArgs args) => QueueReload();

    private void QueueReload() => _reloadQueuedAt ??= DateTime.UtcNow;

    private void PumpReload()
    {
        if (_reloadQueuedAt is not { } queuedAt)
            return;
        if ((DateTime.UtcNow - queuedAt).TotalSeconds < ReloadDebounceSeconds)
            return;
        _reloadQueuedAt = null;
        try
        {
            Recompile();
        }
        catch (Exception exception)
        {
            _log?.Error($"Script recompile failed: {exception.Message}");
        }
    }

    private void SafeRun(ScriptComponent component, ScriptBehaviour behaviour, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            _log?.Error($"Script '{behaviour.GetType().Name}' on '{component.Owner?.Name ?? "<null>"}' threw: {exception.Message}");
        }
    }

    public void Shutdown()
    {
        foreach (SceneModel scene in _playingScenes.ToArray())
            EndPlay(scene);
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
        ScriptHost.InstanceFactory = null;
        ScriptHost.InstanceReleaser = null;
        ScriptHost.ScriptsProvider = null;
        ScriptPhysics.LinearVelocityGetter = null;
        ScriptPhysics.LinearVelocitySetter = null;
        ScriptPhysics.AngularVelocityGetter = null;
        ScriptPhysics.AngularVelocitySetter = null;
        ScriptPhysics.ImpulseApplier = null;
        ScriptPhysics.ImpulseAtPointApplier = null;
        ScriptPhysics.AngularImpulseApplier = null;
        ScriptPhysics.MassGetter = null;
        ScriptPhysics.Waker = null;
        ScriptPhysics.Sleeper = null;
        ScriptPhysics.SleepingChecker = null;
        ScriptPhysics.BodyChecker = null;
        ScriptPhysics.GroundedChecker = null;
        ScriptPhysics.RayCaster = null;
        ScriptPhysics.InterpolatedPoseGetter = null;
        ScriptPhysics.BallJointCreator = null;
        ScriptPhysics.HingeJointCreator = null;
        ScriptPhysics.WeldJointCreator = null;
        ScriptPhysics.DistanceJointCreator = null;
        ScriptPhysics.JointDestroyer = null;
        ScriptScene.PrefabSpawner = null;
        ScriptScene.Finder = null;
        ScriptScene.Destroyer = null;
        ClearJoints();
        ScriptInput.Window = null;
        ScriptLog.Sink = null;
        _scenes.Clear();
        _current = null;
        _retiredContext = null;
        _log?.Information("Scripting shut down");
    }

    public void Dispose() => Shutdown();
}
