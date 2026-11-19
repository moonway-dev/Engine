namespace Engine.Scripting;

using Engine.Mathematics;
using Engine.Scene;

public static class ScriptPhysics
{
    public static Func<GameObject, Vector3>? LinearVelocityGetter { get; set; }

    public static Action<GameObject, Vector3>? LinearVelocitySetter { get; set; }

    public static Func<GameObject, Vector3>? AngularVelocityGetter { get; set; }

    public static Action<GameObject, Vector3>? AngularVelocitySetter { get; set; }

    public static Action<GameObject, Vector3>? ImpulseApplier { get; set; }

    public static Action<GameObject, Vector3, Vector3>? ImpulseAtPointApplier { get; set; }

    public static Action<GameObject, Vector3>? AngularImpulseApplier { get; set; }

    public static Func<GameObject, float>? MassGetter { get; set; }

    public static Action<GameObject>? Waker { get; set; }

    public static Action<GameObject>? Sleeper { get; set; }

    public static Func<GameObject, bool>? SleepingChecker { get; set; }

    public static Func<GameObject, bool>? BodyChecker { get; set; }

    public static Func<GameObject, float, bool>? GroundedChecker { get; set; }

    public static Func<Vector3, Vector3, float, GameObject?, ScriptRayHit?>? RayCaster { get; set; }

    public static Vector3 GetLinearVelocity(GameObject gameObject) =>
        LinearVelocityGetter?.Invoke(gameObject) ?? Vector3.Zero;

    public static void SetLinearVelocity(GameObject gameObject, Vector3 velocity) =>
        LinearVelocitySetter?.Invoke(gameObject, velocity);

    public static Vector3 GetAngularVelocity(GameObject gameObject) =>
        AngularVelocityGetter?.Invoke(gameObject) ?? Vector3.Zero;

    public static void SetAngularVelocity(GameObject gameObject, Vector3 velocity) =>
        AngularVelocitySetter?.Invoke(gameObject, velocity);

    public static void AddImpulse(GameObject gameObject, Vector3 impulse) =>
        ImpulseApplier?.Invoke(gameObject, impulse);

    public static void AddImpulseAtPoint(GameObject gameObject, Vector3 impulse, Vector3 point) =>
        ImpulseAtPointApplier?.Invoke(gameObject, impulse, point);

    public static void AddAngularImpulse(GameObject gameObject, Vector3 impulse) =>
        AngularImpulseApplier?.Invoke(gameObject, impulse);

    public static float GetMass(GameObject gameObject) => MassGetter?.Invoke(gameObject) ?? 0f;

    public static void Wake(GameObject gameObject) => Waker?.Invoke(gameObject);

    public static void Sleep(GameObject gameObject) => Sleeper?.Invoke(gameObject);

    public static bool IsSleeping(GameObject gameObject) => SleepingChecker?.Invoke(gameObject) ?? false;

    public static bool HasBody(GameObject gameObject) => BodyChecker?.Invoke(gameObject) ?? false;

    public static bool IsGrounded(GameObject gameObject, float distance) =>
        GroundedChecker?.Invoke(gameObject, distance) ?? false;

    public static ScriptRayHit? RayCast(Vector3 origin, Vector3 direction, float maximumDistance, GameObject? ignore = null) =>
        RayCaster?.Invoke(origin, direction, maximumDistance, ignore);
}
