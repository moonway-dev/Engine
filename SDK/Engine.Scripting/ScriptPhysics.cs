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

    public static Func<Vector3, Vector3, float, float, GameObject?, ScriptRayHit?>? SphereCaster { get; set; }

    public static Func<GameObject, (Vector3 Position, Quaternion Rotation)>? InterpolatedPoseGetter { get; set; }

    public static (Vector3 Position, Quaternion Rotation) GetInterpolatedPose(GameObject gameObject) =>
        InterpolatedPoseGetter?.Invoke(gameObject) ?? (gameObject.Transform.Position, gameObject.Transform.Rotation);

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

    public static ScriptRayHit? SphereCast(Vector3 origin, Vector3 direction, float maximumDistance, float radius, GameObject? ignore = null) =>
        SphereCaster?.Invoke(origin, direction, maximumDistance, radius, ignore);

    public static Func<GameObject, GameObject, Vector3, Vector3, ScriptJoint?>? BallJointCreator { get; set; }

    public static Func<GameObject, GameObject, Vector3, Vector3, ScriptJoint?>? HingeJointCreator { get; set; }

    public static Func<GameObject, GameObject, ScriptJoint?>? WeldJointCreator { get; set; }

    public static Func<GameObject, GameObject, float, float, ScriptJoint?>? DistanceJointCreator { get; set; }

    public static Action<ScriptJoint>? JointDestroyer { get; set; }

    public static ScriptJoint? CreateBallJoint(GameObject a, GameObject b, Vector3 anchorA, Vector3 anchorB) =>
        BallJointCreator?.Invoke(a, b, anchorA, anchorB);

    public static ScriptJoint? CreateHingeJoint(GameObject a, GameObject b, Vector3 anchor, Vector3 axis) =>
        HingeJointCreator?.Invoke(a, b, anchor, axis);

    public static ScriptJoint? CreateWeldJoint(GameObject a, GameObject b) =>
        WeldJointCreator?.Invoke(a, b);

    public static ScriptJoint? CreateDistanceJoint(GameObject a, GameObject b, float minimumDistance, float maximumDistance) =>
        DistanceJointCreator?.Invoke(a, b, minimumDistance, maximumDistance);

    public static void DestroyJoint(ScriptJoint? joint)
    {
        if (joint is not null)
            JointDestroyer?.Invoke(joint);
    }
}

public sealed class ScriptJoint
{
    internal ScriptJoint(object[] constraints) => Constraint = constraints;

    internal object[] Constraint { get; private set; }

    public bool IsValid => Constraint.Length > 0;

    internal void Invalidate() => Constraint = [];
}
