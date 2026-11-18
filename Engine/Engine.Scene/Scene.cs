namespace Engine.Scene;

using Engine.Mathematics;

public sealed class Scene
{
    private readonly Dictionary<Guid, GameObject> _objectsById = [];
    private readonly Dictionary<Guid, Component> _componentsById = [];
    private readonly List<GameObject> _rootObjects = [];
    private readonly List<UnknownComponentPayload> _unknownComponents = [];
    private readonly List<SceneDiagnostic> _diagnostics = [];

    public string Name { get; set; } = "Scene";

    public Vector4 SkyColor { get; set; } = new(0.35f, 0.55f, 0.85f, 1f);

    public int SourceVersion { get; internal set; } = SceneSerializer.CurrentVersion;

    public int Version { get; set; } = SceneSerializer.CurrentVersion;

    public IReadOnlyList<GameObject> RootObjects => _rootObjects;

    public IReadOnlyCollection<GameObject> AllObjects => _objectsById.Values;

    public IReadOnlyCollection<Component> AllComponents => _componentsById.Values;

    public IReadOnlyList<UnknownComponentPayload> UnknownComponents => _unknownComponents;

    public IReadOnlyList<SceneDiagnostic> Diagnostics => _diagnostics;

    public ComponentRegistry ComponentRegistry { get; }

    public Scene(string name = "Scene")
        : this(name, null)
    {
    }

    public Scene(string name, ComponentRegistry? componentRegistry)
    {
        Name = name;
        ComponentRegistry = componentRegistry?.Clone() ?? new ComponentRegistry();
        ComponentRegistry.RegisterBuiltIns();
    }

    public GameObject CreateObject(string name = "GameObject", GameObject? parent = null)
    {
        if (parent is not null && !ReferenceEquals(parent.Scene, this))
            throw new InvalidOperationException("Cannot create a child under an object in another scene.");
        GameObject gameObject = new(this, name);
        Register(gameObject);
        if (parent is not null)
            gameObject.SetParent(parent);
        else
            _rootObjects.Add(gameObject);
        return gameObject;
    }

    internal void Register(GameObject gameObject)
    {
        if (gameObject.Id == Guid.Empty)
            gameObject.Id = Guid.NewGuid();
        if (!_objectsById.TryAdd(gameObject.Id, gameObject))
            throw new InvalidOperationException($"A GameObject with ID '{gameObject.Id}' already exists in scene '{Name}'.");
    }

    internal void Register(Component component)
    {
        if (component.Id == Guid.Empty)
            component.Id = Guid.NewGuid();
        if (!_componentsById.TryAdd(component.Id, component))
            throw new InvalidOperationException($"A component with ID '{component.Id}' already exists in scene '{Name}'.");
    }

    internal void ReRegisterWithId(GameObject gameObject, Guid id)
    {
        if (id == Guid.Empty)
            throw new InvalidOperationException("GameObject IDs cannot be empty.");
        if (gameObject.Id == id)
            return;
        if (_objectsById.TryGetValue(id, out GameObject? existing) && !ReferenceEquals(existing, gameObject))
            throw new InvalidOperationException($"A GameObject with ID '{id}' already exists in scene '{Name}'.");
        if (!_objectsById.TryGetValue(gameObject.Id, out GameObject? registered) || !ReferenceEquals(registered, gameObject))
            throw new InvalidOperationException("The GameObject is not registered in this scene.");
        _objectsById.Remove(gameObject.Id);
        gameObject.Id = id;
        _objectsById.Add(id, gameObject);
    }

    internal void ReRegisterWithId(Component component, Guid id)
    {
        if (id == Guid.Empty)
            throw new InvalidOperationException("Component IDs cannot be empty.");
        if (component.Id == id)
            return;
        if (_componentsById.TryGetValue(id, out Component? existing) && !ReferenceEquals(existing, component))
            throw new InvalidOperationException($"A component with ID '{id}' already exists in scene '{Name}'.");
        if (!_componentsById.TryGetValue(component.Id, out Component? registered) || !ReferenceEquals(registered, component))
            throw new InvalidOperationException("The component is not registered in this scene.");
        _componentsById.Remove(component.Id);
        component.Id = id;
        _componentsById.Add(id, component);
    }

    internal void AddToRoots(GameObject gameObject)
    {
        if (!_rootObjects.Contains(gameObject))
            _rootObjects.Add(gameObject);
    }

    internal void RemoveFromRoots(GameObject gameObject) => _rootObjects.Remove(gameObject);

    internal void Unregister(GameObject gameObject)
    {
        if (_objectsById.TryGetValue(gameObject.Id, out GameObject? registered) && ReferenceEquals(registered, gameObject))
            _objectsById.Remove(gameObject.Id);
        _unknownComponents.RemoveAll(payload => payload.OwnerId == gameObject.Id);
    }

    internal void Unregister(Component component)
    {
        if (_componentsById.TryGetValue(component.Id, out Component? registered) && ReferenceEquals(registered, component))
            _componentsById.Remove(component.Id);
    }

    internal void AddUnknownComponent(UnknownComponentPayload payload)
    {
        _unknownComponents.Add(payload);
    }

    internal void AddDiagnostic(SceneDiagnostic diagnostic)
    {
        _diagnostics.Add(diagnostic);
    }

    public void Remove(GameObject gameObject)
    {
        ArgumentNullException.ThrowIfNull(gameObject);
        if (!_objectsById.TryGetValue(gameObject.Id, out GameObject? registered) || !ReferenceEquals(registered, gameObject))
            return;
        gameObject.DestroyInternal();
    }

    public GameObject? FindById(Guid id) => _objectsById.GetValueOrDefault(id);

    public Component? FindComponentById(Guid id) => _componentsById.GetValueOrDefault(id);

    public GameObject? FindByName(string name) =>
        _objectsById.Values.FirstOrDefault(o => o.Name.Equals(name, StringComparison.Ordinal));

    public IEnumerable<GameObject> EnumerateHierarchy()
    {
        foreach (GameObject root in _rootObjects)
        {
            yield return root;
            foreach (GameObject descendant in EnumerateDescendants(root))
                yield return descendant;
        }
    }

    private static IEnumerable<GameObject> EnumerateDescendants(GameObject gameObject)
    {
        foreach (GameObject child in gameObject.Children)
        {
            yield return child;
            foreach (GameObject descendant in EnumerateDescendants(child))
                yield return descendant;
        }
    }

    public SceneSnapshot CaptureSnapshot() => SceneSnapshot.Capture(this);

    public Scene Clone(CloneIdPolicy idPolicy = CloneIdPolicy.PreserveIds) =>
        CaptureSnapshot().Restore(idPolicy, ComponentRegistry).Scene;

    public GameObject CloneSubtree(
        GameObject root,
        CloneIdPolicy idPolicy = CloneIdPolicy.GenerateNewIds,
        GameObject? parent = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (!ReferenceEquals(root.Scene, this))
            throw new InvalidOperationException("The source GameObject belongs to another scene.");
        if (parent is not null && !ReferenceEquals(parent.Scene, this))
            throw new InvalidOperationException("The clone parent belongs to another scene.");
        SceneSnapshot snapshot = SceneSnapshot.CaptureSubtree(root);
        SceneSnapshotRestoreResult result = snapshot.RestoreInto(this, idPolicy, parent);
        return result.RootObjects.Single();
    }

    public void Save(string path) => SceneSerializer.Save(this, path);

    public static Scene Load(string path) => SceneSerializer.Load(path);

    public static Scene Load(string path, ComponentRegistry componentRegistry) =>
        SceneSerializer.Load(path, componentRegistry);

    public static Scene FromJson(string json) => SceneSerializer.FromJson(json);

    public static Scene FromJson(string json, ComponentRegistry componentRegistry) =>
        SceneSerializer.FromJson(json, componentRegistry);

    public string ToJson() => SceneSerializer.ToJson(this);
}

public sealed class SceneHost
{
    private Scene? _activeScene;

    public Scene? ActiveScene
    {
        get => _activeScene;
        set
        {
            if (ReferenceEquals(_activeScene, value))
                return;
            Scene? previous = _activeScene;
            _activeScene = value;
            ActiveSceneChanged?.Invoke(this, new ActiveSceneChangedEventArgs(previous, value));
        }
    }

    public event EventHandler<ActiveSceneChangedEventArgs>? ActiveSceneChanged;

    public Scene RequireActiveScene() =>
        _activeScene ?? throw new InvalidOperationException("No active scene is assigned.");
}

public sealed class ActiveSceneChangedEventArgs : EventArgs
{
    public Scene? Previous { get; }

    public Scene? Current { get; }

    public ActiveSceneChangedEventArgs(Scene? previous, Scene? current)
    {
        Previous = previous;
        Current = current;
    }
}

public sealed class ComponentRegistry
{
    private static readonly object GlobalLock = new();
    private static readonly Dictionary<string, ComponentRegistration> GlobalRegistrations =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<Type, string> GlobalKeysByType = [];

    private readonly Dictionary<string, ComponentRegistration> _registrations =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Type, ComponentRegistration> _registrationsByType = [];

    public IReadOnlyCollection<string> RegisteredKeys => _registrations.Keys.ToArray();

    public static void RegisterGlobal<TComponent>(Func<TComponent> factory, string? key = null)
        where TComponent : Component
    {
        RegisterGlobal(typeof(TComponent), () => factory(), key);
    }

    public static void RegisterGlobal(Type componentType, Func<Component> factory, string? key = null, bool overwrite = false)
    {
        ComponentRegistration registration = CreateRegistration(componentType, factory, key);
        lock (GlobalLock)
        {
            if (!overwrite)
            {
                if (GlobalRegistrations.TryGetValue(registration.Key, out ComponentRegistration? byKey)
                    && byKey.ComponentType != registration.ComponentType)
                    throw new InvalidOperationException(
                        $"Component key '{registration.Key}' is already registered for type '{byKey.ComponentType.FullName}'.");
                if (GlobalKeysByType.TryGetValue(registration.ComponentType, out string? existingKey)
                    && !existingKey.Equals(registration.Key, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"Component type '{registration.ComponentType.FullName}' is already registered with key '{existingKey}'.");
            }
            if (GlobalRegistrations.TryGetValue(registration.Key, out ComponentRegistration? replacedKey)
                && replacedKey.ComponentType != registration.ComponentType)
                GlobalKeysByType.Remove(replacedKey.ComponentType);
            if (GlobalKeysByType.TryGetValue(registration.ComponentType, out string? replacedType)
                && !replacedType.Equals(registration.Key, StringComparison.OrdinalIgnoreCase))
                GlobalRegistrations.Remove(replacedType);
            GlobalRegistrations[registration.Key] = registration;
            GlobalKeysByType[registration.ComponentType] = registration.Key;
        }
    }

    public void Register<TComponent>(Func<TComponent> factory, string? key = null)
        where TComponent : Component
    {
        Register(typeof(TComponent), () => factory(), key);
    }

    public void Register(Type componentType, Func<Component> factory, string? key = null, bool overwrite = false)
    {
        ComponentRegistration registration = CreateRegistration(componentType, factory, key);
        if (!overwrite)
        {
            if (_registrations.TryGetValue(registration.Key, out ComponentRegistration? byKey)
                && byKey.ComponentType != registration.ComponentType)
                throw new InvalidOperationException(
                    $"Component key '{registration.Key}' is already registered for type '{byKey.ComponentType.FullName}'.");
            if (_registrationsByType.TryGetValue(registration.ComponentType, out ComponentRegistration? byType)
                && !byType.Key.Equals(registration.Key, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Component type '{registration.ComponentType.FullName}' is already registered with key '{byType.Key}'.");
        }
        if (_registrations.TryGetValue(registration.Key, out ComponentRegistration? replacedKey)
            && replacedKey.ComponentType != registration.ComponentType)
            _registrationsByType.Remove(replacedKey.ComponentType);
        if (_registrationsByType.TryGetValue(registration.ComponentType, out ComponentRegistration? replacedType)
            && !replacedType.Key.Equals(registration.Key, StringComparison.OrdinalIgnoreCase))
            _registrations.Remove(replacedType.Key);
        _registrations[registration.Key] = registration;
        _registrationsByType[registration.ComponentType] = registration;
    }

    internal void RegisterBuiltIns()
    {
        RegisterIfMissing(typeof(CameraComponent), () => new CameraComponent());
        RegisterIfMissing(typeof(LightComponent), () => new LightComponent());
        lock (GlobalLock)
        {
            foreach (ComponentRegistration registration in GlobalRegistrations.Values)
                RegisterIfMissing(registration.ComponentType, registration.Factory, registration.Key);
        }
    }

    public ComponentRegistry Clone()
    {
        ComponentRegistry clone = new();
        foreach (ComponentRegistration registration in _registrations.Values)
            clone.Register(registration.ComponentType, registration.Factory, registration.Key);
        return clone;
    }

    public bool Contains(string componentKey) => _registrations.ContainsKey(componentKey);

    public string? GetKey(Type componentType) =>
        _registrationsByType.TryGetValue(componentType, out ComponentRegistration? registration)
            ? registration.Key
            : null;

    public Type? GetRegisteredType(string componentKey) =>
        _registrations.TryGetValue(componentKey, out ComponentRegistration? registration)
            ? registration.ComponentType
            : null;

    public Component? Create(string componentKey) =>
        _registrations.TryGetValue(componentKey, out ComponentRegistration? registration)
            ? registration.Factory()
            : null;

    public Component? Create(Type componentType) =>
        _registrationsByType.TryGetValue(componentType, out ComponentRegistration? registration)
            ? registration.Factory()
            : null;

    public Component? Create(string componentKey, GameObject owner)
    {
        Component? component = Create(componentKey);
        if (component is null)
            return null;
        owner.AddComponent(component);
        return component;
    }

    public Component? Create(Type componentType, GameObject owner)
    {
        Component? component = Create(componentType);
        if (component is null)
            return null;
        owner.AddComponent(component);
        return component;
    }

    internal Component? CreateForRestore(string componentKey, GameObject owner)
    {
        Component? component = Create(componentKey);
        if (component is null)
            return null;
        owner.AttachComponentForRestore(component);
        return component;
    }

    internal Component CreateAndAttach(Type type, GameObject owner) =>
        Create(type, owner)
        ?? throw new InvalidOperationException($"Component type '{type.Name}' is not registered in the scene component registry.");

    public static string GetComponentKey(Type type) =>
        type.Name.EndsWith("Component", StringComparison.Ordinal)
            ? type.Name[..^"Component".Length]
            : type.Name;

    private void RegisterIfMissing(Type componentType, Func<Component> factory, string? key = null)
    {
        string resolvedKey = key ?? GetComponentKey(componentType);
        if (_registrationsByType.TryGetValue(componentType, out ComponentRegistration? existingType))
        {
            if (!existingType.Key.Equals(resolvedKey, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Component type '{componentType.FullName}' is already registered with key '{existingType.Key}'.");
            return;
        }
        Register(componentType, factory, resolvedKey);
    }

    private static ComponentRegistration CreateRegistration(
        Type componentType,
        Func<Component> factory,
        string? key)
    {
        ArgumentNullException.ThrowIfNull(componentType);
        ArgumentNullException.ThrowIfNull(factory);
        if (!componentType.IsSubclassOf(typeof(Component)) || componentType.IsAbstract)
            throw new ArgumentException($"Type '{componentType.FullName}' must be a concrete Component.", nameof(componentType));
        string resolvedKey = key ?? GetComponentKey(componentType);
        if (string.IsNullOrWhiteSpace(resolvedKey))
            throw new ArgumentException("Component keys cannot be empty.", nameof(key));
        return new ComponentRegistration(resolvedKey, componentType, factory);
    }

    private sealed record ComponentRegistration(
        string Key,
        Type ComponentType,
        Func<Component> Factory);
}
