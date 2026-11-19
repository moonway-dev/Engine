using BepuPhysics;
using BepuUtilities;
using Engine.Mathematics;
using NVector3 = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;

namespace Engine.Physics;

internal sealed class PhysicsBodyState(BodyInertia inertia, Vector3 position, Quaternion rotation)
{
    public BodyInertia Inertia = inertia;
    public RigidbodySettings Settings = RigidbodySettings.Default;
    public Vector3 PositionAnchor = position;
    public Quaternion RotationAnchor = rotation;

    public Vector3 FilterLinear(Vector3 value) => new(
        Settings.Constraints.HasFlag(RigidbodyConstraints.FreezePositionX) ? 0f : value.X,
        Settings.Constraints.HasFlag(RigidbodyConstraints.FreezePositionY) ? 0f : value.Y,
        Settings.Constraints.HasFlag(RigidbodyConstraints.FreezePositionZ) ? 0f : value.Z);

    public Vector3 FilterAngular(Vector3 value) => new(
        Settings.Constraints.HasFlag(RigidbodyConstraints.FreezeRotationX) ? 0f : value.X,
        Settings.Constraints.HasFlag(RigidbodyConstraints.FreezeRotationY) ? 0f : value.Y,
        Settings.Constraints.HasFlag(RigidbodyConstraints.FreezeRotationZ) ? 0f : value.Z);

    public Vector3 FilterAngularWorld(Vector3 value, Quaternion rotation)
    {
        Vector3 local = rotation.Inverse.Rotate(value);
        return rotation.Rotate(FilterAngular(local));
    }
}

internal sealed class PhysicsBodySettings
{
    public readonly Dictionary<BodyHandle, PhysicsBodyState> Bodies = [];
    public readonly Dictionary<StaticHandle, bool> StaticCollisions = [];
    public NVector3 Gravity;

    public bool AllowsCollision(BepuPhysics.Collidables.CollidableReference collidable) =>
        collidable.Mobility == BepuPhysics.Collidables.CollidableMobility.Static
            ? !StaticCollisions.TryGetValue(collidable.StaticHandle, out bool enabled) || enabled
            : !Bodies.TryGetValue(collidable.BodyHandle, out PhysicsBodyState? state) || state.Settings.DetectCollisions;
}
