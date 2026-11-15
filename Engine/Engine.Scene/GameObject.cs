namespace Engine.Scene;

public sealed class GameObject
{
    private readonly List<Component> _components = [];
    private readonly List<GameObject> _children = [];

    public Guid Id { get; internal set; }

    public string Name { get; set; } = "GameObject";

    public bool Active { get; set; } = true;

    public Transform Transform { get; }

    public GameObject? Parent { get; private set; }

    public IReadOnlyList<GameObject> Children => _children;

    public IReadOnlyList<Component> Components => _components;

    public Scene Scene { get; private set; }

    public PrefabInstance? PrefabInstance { get; internal set; }

    public bool IsActiveInHierarchy
    {
        get
        {
            GameObject? current = this;
            while (current is not null)
            {
                if (!current.Active)
                    return false;
                current = current.Parent;
            }
            return true;
        }
    }

    internal GameObject(Scene scene, string name)
    {
        Id = Guid.NewGuid();
        Scene = scene;
        Name = name;
        Transform = new Transform { Owner = this };
    }

    public T AddComponent<T>() where T : Component, new()
    {
        T component = new();
        AttachComponent(component);
        return component;
    }

    public T AddComponent<T>(T component) where T : Component
    {
        AttachComponent(component);
        return component;
    }

    public Component AddComponent(string componentKey)
    {
        Component? component = Scene.ComponentRegistry.Create(componentKey);
        if (component is null)
            throw new KeyNotFoundException($"Component key '{componentKey}' is not registered.");
        AttachComponent(component);
        return component;
    }

    public Component AddComponent(Type componentType)
    {
        Component? component = Scene.ComponentRegistry.Create(componentType);
        if (component is null)
            throw new KeyNotFoundException($"Component type '{componentType.FullName}' is not registered.");
        AttachComponent(component);
        return component;
    }

    internal void AttachComponent(Component component)
    {
        AttachComponent(component, invokeLifecycle: true);
    }

    internal void AttachComponentForRestore(Component component)
    {
        AttachComponent(component, invokeLifecycle: false);
    }

    internal void InvokeComponentAttach(Component component)
    {
        if (!_components.Contains(component) || !ReferenceEquals(component.Owner, this))
            throw new InvalidOperationException("The component is not attached to this GameObject.");
        component.OnAttach();
    }

    internal void RemoveComponentAfterFailedAttach(Component component)
    {
        if (!_components.Remove(component))
            return;
        Scene.Unregister(component);
        component.Owner = null!;
    }

    private void AttachComponent(Component component, bool invokeLifecycle)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (component.Owner is not null)
            throw new InvalidOperationException("A component cannot be attached to more than one GameObject.");
        if (_components.Any(c => c.GetType() == component.GetType()))
            throw new InvalidOperationException($"GameObject '{Name}' already has a component of type '{component.GetType().Name}'.");
        Scene.Register(component);
        component.Owner = this;
        _components.Add(component);
        if (!invokeLifecycle)
            return;
        try
        {
            component.OnAttach();
        }
        catch
        {
            RemoveComponentAfterFailedAttach(component);
            throw;
        }
    }

    public T? GetComponent<T>() where T : Component =>
        _components.OfType<T>().FirstOrDefault();

    public Component? GetComponent(Type componentType)
    {
        ArgumentNullException.ThrowIfNull(componentType);
        return _components.FirstOrDefault(component => componentType.IsInstanceOfType(component));
    }

    public Component? GetComponent(string componentKey)
    {
        Type? type = Scene.ComponentRegistry.GetRegisteredType(componentKey);
        return type is null ? null : GetComponent(type);
    }

    public bool TryGetComponent<T>(out T component) where T : Component
    {
        component = GetComponent<T>()!;
        return component is not null;
    }

    public IEnumerable<T> GetComponentsInChildren<T>() where T : Component
    {
        foreach (GameObject child in _children)
        {
            T? component = child.GetComponent<T>();
            if (component is not null)
                yield return component;
            foreach (T nested in child.GetComponentsInChildren<T>())
                yield return nested;
        }
    }

    public bool RemoveComponent<T>() where T : Component
    {
        Component? component = _components.FirstOrDefault(c => c is T);
        return component is not null && RemoveComponent(component);
    }

    public bool RemoveComponent(string componentKey)
    {
        Component? component = GetComponent(componentKey);
        return component is not null && RemoveComponent(component);
    }

    public bool RemoveComponent(Type componentType)
    {
        Component? component = GetComponent(componentType);
        return component is not null && RemoveComponent(component);
    }

    public bool RemoveComponent(Component component)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (!_components.Contains(component))
            return false;
        component.OnDetach();
        _components.Remove(component);
        Scene.Unregister(component);
        component.Owner = null!;
        return true;
    }

    public void SetParent(GameObject? newParent)
    {
        if (newParent == this)
            throw new InvalidOperationException("Cannot parent a GameObject to itself.");
        if (newParent is not null && !ReferenceEquals(newParent.Scene, Scene))
            throw new InvalidOperationException("Cannot parent a GameObject to an object in another scene.");
        if (newParent is not null && IsAncestorOf(newParent))
            throw new InvalidOperationException("Cannot parent a GameObject to one of its own descendants.");
        if (Parent == newParent)
            return;
        GameObject? oldParent = Parent;
        oldParent?._children.Remove(this);
        if (oldParent is null)
            Scene.RemoveFromRoots(this);
        Parent = newParent;
        if (newParent is not null)
            newParent._children.Add(this);
        else
            Scene.AddToRoots(this);
        Transform.MarkDirty();
    }

    public bool IsAncestorOf(GameObject other)
    {
        GameObject? current = other.Parent;
        while (current is not null)
        {
            if (current == this)
                return true;
            current = current.Parent;
        }
        return false;
    }

    public GameObject Clone() => Scene.CloneSubtree(this, CloneIdPolicy.GenerateNewIds);

    public GameObject Clone(CloneIdPolicy idPolicy, GameObject? parent = null) =>
        Scene.CloneSubtree(this, idPolicy, parent);

    internal void DestroyInternal()
    {
        foreach (GameObject child in _children.ToArray())
            child.DestroyInternal();
        _children.Clear();
        foreach (Component component in _components.ToArray())
            RemoveComponent(component);
        if (Parent is null)
            Scene.RemoveFromRoots(this);
        else
            Parent._children.Remove(this);
        Parent = null;
        Scene.Unregister(this);
    }

    public void Destroy() => Scene.Remove(this);
}
