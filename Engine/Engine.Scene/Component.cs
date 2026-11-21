namespace Engine.Scene;

public abstract class Component
{
    public Guid Id { get; internal set; } = Guid.NewGuid();

    public GameObject Owner { get; internal set; } = null!;

    public Transform Transform => Owner.Transform;

    public Scene Scene => Owner.Scene;

    public bool Enabled { get; set; } = true;

    public virtual void OnDrawGizmos() { }

    public bool IsActiveInHierarchy => Owner.IsActiveInHierarchy && Enabled;

    public virtual bool AllowMultiplePerObject => false;

    public virtual string ComponentTypeName => GetType().Name;

    protected internal virtual void OnAttach()
    {
    }

    protected internal virtual void OnDetach()
    {
    }
}
