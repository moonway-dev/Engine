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
    private readonly Dictionary<BodyHandle, int> BodyLayers = [];
    private readonly Dictionary<StaticHandle, int> StaticLayers = [];
    private readonly Dictionary<BodyHandle, int[]> BodyChildLayers = [];
    public long[] LayerMasks = InitMasks();
    public NVector3 Gravity;

    private static long[] InitMasks()
    {
        long[] masks = new long[PhysicsLayers.MaxLayers];
        Array.Fill(masks, -1L);
        return masks;
    }

    public bool AllowsCollision(BepuPhysics.Collidables.CollidableReference collidable) =>
        collidable.Mobility == BepuPhysics.Collidables.CollidableMobility.Static
            ? !StaticCollisions.TryGetValue(collidable.StaticHandle, out bool enabled) || enabled
            : !Bodies.TryGetValue(collidable.BodyHandle, out PhysicsBodyState? state) || state.Settings.DetectCollisions;

    public bool AllowsCollisionPair(BepuPhysics.Collidables.CollidableReference a, BepuPhysics.Collidables.CollidableReference b, int childIndexA = -1, int childIndexB = -1) =>
        AllowsCollision(a) && AllowsCollision(b) && LayerMaskAllows(LayerOf(a, childIndexA), LayerOf(b, childIndexB));

    public void SetBodyChildLayers(BodyHandle handle, IReadOnlyList<int> layers) =>
        BodyChildLayers[handle] = [.. layers];

    public void SetBodyLayer(BodyHandle handle, int layer) =>
        BodyLayers[handle] = Math.Clamp(layer, 0, PhysicsLayers.MaxLayers - 1);

    public void SetStaticLayer(StaticHandle handle, int layer) =>
        StaticLayers[handle] = Math.Clamp(layer, 0, PhysicsLayers.MaxLayers - 1);

    public void ForgetBodyLayer(BodyHandle handle)
    {
        BodyLayers.Remove(handle);
        BodyChildLayers.Remove(handle);
    }

    public void ForgetStaticLayer(StaticHandle handle) => StaticLayers.Remove(handle);

    public void SetLayerMask(int layer, long mask)
    {
        if (layer is >= 0 and < PhysicsLayers.MaxLayers)
            LayerMasks[layer] = mask;
    }

    public long GetLayerMask(int layer) =>
        layer is >= 0 and < PhysicsLayers.MaxLayers ? LayerMasks[layer] : -1L;

    private int LayerOf(BepuPhysics.Collidables.CollidableReference collidable, int childIndex = -1)
    {
        int bodyLayer = collidable.Mobility == BepuPhysics.Collidables.CollidableMobility.Static
            ? StaticLayers.GetValueOrDefault(collidable.StaticHandle, 0)
            : BodyLayers.GetValueOrDefault(collidable.BodyHandle, 0);
        if (childIndex >= 0
            && collidable.Mobility != BepuPhysics.Collidables.CollidableMobility.Static
            && BodyChildLayers.TryGetValue(collidable.BodyHandle, out int[]? layers)
            && childIndex < layers.Length
            && layers[childIndex] >= 0)
            return layers[childIndex];
        return bodyLayer;
    }

    private bool LayerMaskAllows(int a, int b)
    {
        long maskA = LayerMasks[a];
        long maskB = LayerMasks[b];
        return ((maskA >> b) & 1L) != 0L && ((maskB >> a) & 1L) != 0L;
    }
}
