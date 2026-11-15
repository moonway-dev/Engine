using BepuPhysics;
using BepuPhysics.Collidables;
using BepuUtilities.Memory;
using Engine.Mathematics;
using NVector3 = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;

namespace Engine.Physics;

public enum RigidBodyKind
{
    Static,
    Dynamic,
    Kinematic,
}

public enum ColliderShape
{
    Box,
    Sphere,
}

public sealed class PhysicsWorld : IDisposable
{
    private readonly BufferPool _bufferPool;
    private readonly Simulation _simulation;
    private bool _disposed;

    public PhysicsWorld(Vector3 gravity)
    {
        _bufferPool = new BufferPool();
        _simulation = Simulation.Create(
            _bufferPool,
            new NarrowPhaseCallbacks(),
            new PoseIntegratorCallbacks(new NVector3(gravity.X, gravity.Y, gravity.Z)),
            new SolveDescription(4, 1));
    }

    public Simulation Simulation => _simulation;

    public BodyHandle AddDynamic(Vector3 position, Quaternion rotation, ColliderShape shape, Vector3 size, float mass)
    {
        BodyDescription description = shape switch
        {
            ColliderShape.Sphere => BodyDescription.CreateConvexDynamic(
                new RigidPose(ToSystem(position), ToSystemQuaternion(rotation)),
                MathF.Max(mass, MathUtil.Epsilon),
                _simulation.Shapes,
                new Sphere(MathF.Max(size.X, MathUtil.Epsilon))),
            _ => BodyDescription.CreateConvexDynamic(
                new RigidPose(ToSystem(position), ToSystemQuaternion(rotation)),
                MathF.Max(mass, MathUtil.Epsilon),
                _simulation.Shapes,
                new Box(MathF.Max(size.X, MathUtil.Epsilon), MathF.Max(size.Y, MathUtil.Epsilon), MathF.Max(size.Z, MathUtil.Epsilon))),
        };
        return _simulation.Bodies.Add(description);
    }

    public BodyHandle AddKinematic(Vector3 position, Quaternion rotation, ColliderShape shape, Vector3 size)
    {
        BodyDescription description = shape switch
        {
            ColliderShape.Sphere => BodyDescription.CreateConvexKinematic(
                new RigidPose(ToSystem(position), ToSystemQuaternion(rotation)),
                _simulation.Shapes,
                new Sphere(MathF.Max(size.X, MathUtil.Epsilon))),
            _ => BodyDescription.CreateConvexKinematic(
                new RigidPose(ToSystem(position), ToSystemQuaternion(rotation)),
                _simulation.Shapes,
                new Box(MathF.Max(size.X, MathUtil.Epsilon), MathF.Max(size.Y, MathUtil.Epsilon), MathF.Max(size.Z, MathUtil.Epsilon))),
        };
        return _simulation.Bodies.Add(description);
    }

    public StaticHandle AddStatic(Vector3 position, Quaternion rotation, ColliderShape shape, Vector3 size)
    {
        TypedIndex shapeIndex = shape switch
        {
            ColliderShape.Sphere => _simulation.Shapes.Add(new Sphere(MathF.Max(size.X, MathUtil.Epsilon))),
            _ => _simulation.Shapes.Add(new Box(MathF.Max(size.X, MathUtil.Epsilon), MathF.Max(size.Y, MathUtil.Epsilon), MathF.Max(size.Z, MathUtil.Epsilon))),
        };
        StaticDescription description = new(new RigidPose(ToSystem(position), ToSystemQuaternion(rotation)), shapeIndex);
        return _simulation.Statics.Add(description);
    }

    public void RemoveBody(BodyHandle handle)
    {
        TypedIndex shape = _simulation.Bodies[handle].Collidable.Shape;
        _simulation.Bodies.Remove(handle);
        _simulation.Shapes.Remove(shape);
    }

    public void RemoveStatic(StaticHandle handle)
    {
        TypedIndex shape = _simulation.Statics[handle].Shape;
        _simulation.Statics.Remove(handle);
        _simulation.Shapes.Remove(shape);
    }

    public void SetStaticPose(StaticHandle handle, Vector3 position, Quaternion rotation)
    {
        _simulation.Statics.GetDescription(handle, out StaticDescription description);
        description.Pose = new RigidPose(ToSystem(position), ToSystemQuaternion(rotation));
        _simulation.Statics.ApplyDescription(handle, description);
    }

    public void SetBodyPose(BodyHandle handle, Vector3 position, Quaternion rotation)
    {
        BodyReference body = _simulation.Bodies[handle];
        body.Awake = true;
        body.Pose = new RigidPose(ToSystem(position), ToSystemQuaternion(rotation));
        body.Velocity = default;
        body.UpdateBounds();
    }

    public void SetBodyAwake(BodyHandle handle, bool awake)
    {
        BodyReference body = _simulation.Bodies[handle];
        body.Awake = awake;
    }

    public Vector3 GetBodyPosition(BodyHandle handle) => ToEngine(_simulation.Bodies[handle].Pose.Position);

    public Quaternion GetBodyRotation(BodyHandle handle) => ToEngine(_simulation.Bodies[handle].Pose.Orientation);

    public void SetBodyPosition(BodyHandle handle, Vector3 position)
    {
        SetBodyPose(handle, position, GetBodyRotation(handle));
    }

    public void SetBodyRotation(BodyHandle handle, Quaternion rotation)
    {
        SetBodyPose(handle, GetBodyPosition(handle), rotation);
    }

    public void SetBodyVelocity(BodyHandle handle, Vector3 linearVelocity)
    {
        _simulation.Bodies[handle].Velocity.Linear = ToSystem(linearVelocity);
    }

    public void Step(float deltaTime) => _simulation.Timestep(deltaTime);

    private static NVector3 ToSystem(Vector3 value) => new(value.X, value.Y, value.Z);

    private static NQuaternion ToSystemQuaternion(Quaternion value) => new(value.X, value.Y, value.Z, value.W);

    private static Vector3 ToEngine(NVector3 value) => new(value.X, value.Y, value.Z);

    private static Quaternion ToEngine(NQuaternion value) => new(value.X, value.Y, value.Z, value.W);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _simulation.Dispose();
        _bufferPool.Clear();
    }
}
