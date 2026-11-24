namespace Engine.Scripting;

using Engine.Mathematics;
using Engine.Scene;

public abstract class ScriptBehaviour
{
    public Script Component { get; internal set; } = null!;

    public GameObject Owner => Component.Owner;

    public Transform Transform => Owner.Transform;

    public Scene Scene => Owner.Scene;

    public string Name => Owner.Name;

    public bool Enabled { get; set; } = true;

    public bool IsActive => Component.IsActiveInHierarchy && Enabled;

    public virtual void OnEnable()
    {
    }

    public virtual void OnStart()
    {
    }

    public virtual void OnUpdate()
    {
    }

    public virtual void OnFixedUpdate()
    {
    }

    public virtual void OnDisable()
    {
    }

    public virtual void OnDestroy()
    {
    }

    public T? GetComponent<T>() where T : Component => Owner.GetComponent<T>();

    public Component? GetComponent(Type componentType) => Owner.GetComponent(componentType);

    public T AddComponent<T>() where T : Component, new() => Owner.AddComponent<T>();

    public GameObject? Find(string name) => Scene.FindByName(name);

    public GameObject CreateChild(string name)
    {
        GameObject child = Scene.CreateObject(name);
        child.SetParent(Owner);
        return child;
    }

    public void Destroy(GameObject gameObject) => gameObject.Destroy();

    public void Log(string message) => ScriptLog.Info(Name, message);

    public Vector3 GetLinearVelocity() => ScriptPhysics.GetLinearVelocity(Owner);

    public void SetLinearVelocity(Vector3 velocity) => ScriptPhysics.SetLinearVelocity(Owner, velocity);

    public Vector3 GetAngularVelocity() => ScriptPhysics.GetAngularVelocity(Owner);

    public void SetAngularVelocity(Vector3 velocity) => ScriptPhysics.SetAngularVelocity(Owner, velocity);

    public void AddImpulse(Vector3 impulse) => ScriptPhysics.AddImpulse(Owner, impulse);

    public void AddImpulseAtPoint(Vector3 impulse, Vector3 point) => ScriptPhysics.AddImpulseAtPoint(Owner, impulse, point);

    public void AddAngularImpulse(Vector3 impulse) => ScriptPhysics.AddAngularImpulse(Owner, impulse);

    public float GetMass() => ScriptPhysics.GetMass(Owner);

    public bool HasRigidBody() => ScriptPhysics.HasBody(Owner);

    public bool IsGrounded(float distance = 0.2f) => ScriptPhysics.IsGrounded(Owner, distance);

    public ScriptRayHit? RayCast(Vector3 direction, float maximumDistance, GameObject? ignore = null) =>
        ScriptPhysics.RayCast(Transform.Position, direction, maximumDistance, ignore);
}
