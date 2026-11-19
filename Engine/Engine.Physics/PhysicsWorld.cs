using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.Trees;
using BepuUtilities.Memory;
using Engine.Mathematics;
using Engine.Scene;
using NVector3 = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;

namespace Engine.Physics;

public enum RigidBodyKind
{
    Static,
    Dynamic,
    Kinematic,
}

public readonly record struct PhysicsRayHit(float Distance, Vector3 Normal);

public sealed class PhysicsWorld : IDisposable
{
    private const float Epsilon = MathUtil.Epsilon;

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
                new SolveDescription(8, 2));
    }

    public Simulation Simulation => _simulation;

    public BodyHandle AddDynamic(Vector3 position, Quaternion rotation, IReadOnlyList<PhysicsColliderSpec> colliders, float mass) =>
        AddDynamic(position, rotation, colliders, mass, out _);

    public BodyHandle AddDynamic(Vector3 position, Quaternion rotation, IReadOnlyList<PhysicsColliderSpec> colliders, float mass, out Vector3 centerOfMassOffset)
    {
        centerOfMassOffset = Vector3.Zero;
        if (colliders.Count == 0)
            throw new ArgumentException("At least one collider is required.", nameof(colliders));
        RigidPose pose = new(ToSystem(position), ToSystemQuaternion(rotation));
        BodyDescription description;
        if (TryGetSingleShape(colliders, out PhysicsColliderSpec? single))
        {
            PhysicsColliderSpec spec = single!;
            description = spec.Kind switch
            {
                PhysicsColliderKind.Sphere => BodyDescription.CreateConvexDynamic(pose, MathF.Max(mass, Epsilon), _simulation.Shapes, new Sphere(MathF.Max(spec.Radius, Epsilon))),
                PhysicsColliderKind.Capsule => BodyDescription.CreateConvexDynamic(pose, MathF.Max(mass, Epsilon), _simulation.Shapes, CreateCapsule(spec)),
                _ => BodyDescription.CreateConvexDynamic(pose, MathF.Max(mass, Epsilon), _simulation.Shapes, CreateBox(spec)),
            };
        }
        else
        {
            Buffer<CompoundChild> children = BuildCompound(colliders, RigidBodyKind.Dynamic, mass, out BodyInertia inertia, out Vector3 centerOfMass);
            TypedIndex compoundIndex = _simulation.Shapes.Add(new Compound(children));
            centerOfMassOffset = centerOfMass;
            pose.Position += ToSystem(rotation.Rotate(centerOfMass));
            description = BodyDescription.CreateDynamic(pose, inertia, new CollidableDescription(compoundIndex, 0.1f), new BodyActivityDescription(0.01f));
        }
        return _simulation.Bodies.Add(description);
    }

    public BodyHandle AddKinematic(Vector3 position, Quaternion rotation, IReadOnlyList<PhysicsColliderSpec> colliders)
    {
        if (colliders.Count == 0)
            throw new ArgumentException("At least one collider is required.", nameof(colliders));
        RigidPose pose = new(ToSystem(position), ToSystemQuaternion(rotation));
        BodyDescription description;
        if (colliders.Count == 1 && colliders[0].Kind == PhysicsColliderKind.Mesh)
        {
            PhysicsColliderSpec spec = colliders[0];
            pose.Position += ToSystem(rotation.Rotate(spec.Center));
            pose.Orientation = ToSystemQuaternion(rotation * spec.Rotation);
            description = CreateKinematic(pose, CreateMeshShape(spec, out _));
        }
        else if (TryGetSingleShape(colliders, out PhysicsColliderSpec? single))
        {
            PhysicsColliderSpec spec = single!;
            description = spec.Kind switch
            {
                PhysicsColliderKind.Sphere => BodyDescription.CreateConvexKinematic(pose, _simulation.Shapes, new Sphere(MathF.Max(spec.Radius, Epsilon))),
                PhysicsColliderKind.Capsule => BodyDescription.CreateConvexKinematic(pose, _simulation.Shapes, CreateCapsule(spec)),
                _ => BodyDescription.CreateConvexKinematic(pose, _simulation.Shapes, CreateBox(spec)),
            };
        }
        else
        {
            Buffer<CompoundChild> children = BuildCompound(colliders, RigidBodyKind.Kinematic, 1f, out _, out _);
            TypedIndex compoundIndex = _simulation.Shapes.Add(new Compound(children));
            description = BodyDescription.CreateKinematic(pose, new CollidableDescription(compoundIndex, 0.1f), new BodyActivityDescription(0.01f));
        }
        return _simulation.Bodies.Add(description);
    }

    public StaticHandle AddStatic(Vector3 position, Quaternion rotation, IReadOnlyList<PhysicsColliderSpec> colliders)
    {
        if (colliders.Count == 0)
            throw new ArgumentException("At least one collider is required.", nameof(colliders));
        RigidPose pose = new(ToSystem(position), ToSystemQuaternion(rotation));
        TypedIndex shapeIndex;
        if (colliders.Count == 1 && colliders[0].Kind == PhysicsColliderKind.Mesh)
        {
            PhysicsColliderSpec spec = colliders[0];
            pose.Position += ToSystem(rotation.Rotate(spec.Center));
            pose.Orientation = ToSystemQuaternion(rotation * spec.Rotation);
            shapeIndex = CreateMeshShape(spec, out _);
        }
        else if (TryGetSingleShape(colliders, out PhysicsColliderSpec? single))
        {
            PhysicsColliderSpec spec = single!;
            shapeIndex = spec.Kind switch
            {
                PhysicsColliderKind.Sphere => _simulation.Shapes.Add(new Sphere(MathF.Max(spec.Radius, Epsilon))),
                PhysicsColliderKind.Capsule => _simulation.Shapes.Add(CreateCapsule(spec)),
                _ => _simulation.Shapes.Add(CreateBox(spec)),
            };
        }
        else
        {
            Buffer<CompoundChild> children = BuildCompound(colliders, RigidBodyKind.Static, 1f, out _, out Vector3 centerOfMass);
            shapeIndex = _simulation.Shapes.Add(new Compound(children));
            pose.Position += ToSystem(rotation.Rotate(centerOfMass));
        }
        StaticDescription description = new(pose, shapeIndex);
        return _simulation.Statics.Add(description);
    }

    private static BodyDescription CreateKinematic(RigidPose pose, TypedIndex shapeIndex) =>
        BodyDescription.CreateKinematic(pose, new CollidableDescription(shapeIndex, 0.1f), new BodyActivityDescription(0.01f));

    private static Box CreateBox(PhysicsColliderSpec spec) => new(
        MathF.Max(spec.Size.X, Epsilon),
        MathF.Max(spec.Size.Y, Epsilon),
        MathF.Max(spec.Size.Z, Epsilon));

    private static Capsule CreateCapsule(PhysicsColliderSpec spec)
    {
        float radius = MathF.Min(MathF.Max(spec.Radius, Epsilon), MathF.Max(spec.Size.Y * 0.5f, Epsilon));
        float length = MathF.Max(0f, spec.Size.Y - radius * 2f);
        return new Capsule(radius, length);
    }

    private static bool TryGetSingleShape(IReadOnlyList<PhysicsColliderSpec> colliders, out PhysicsColliderSpec? single)
    {
        single = null;
        if (colliders.Count != 1)
            return false;
        PhysicsColliderSpec spec = colliders[0];
        if (spec.Kind == PhysicsColliderKind.Mesh || spec.Center != Vector3.Zero || spec.Rotation != Quaternion.Identity)
            return false;
        single = spec;
        return true;
    }

    private Buffer<CompoundChild> BuildCompound(IReadOnlyList<PhysicsColliderSpec> colliders, RigidBodyKind kind, float mass, out BodyInertia inertia, out Vector3 centerOfMass)
    {
        List<(PhysicsColliderSpec Spec, RigidPose Pose)> children = [];
        foreach (PhysicsColliderSpec spec in colliders)
        {
            if (spec.Kind == PhysicsColliderKind.Mesh && (spec.Mesh is null || spec.Mesh.Indices.Length < 3))
                continue;
            children.Add((spec, new RigidPose(ToSystem(spec.Center), ToSystemQuaternion(spec.Rotation))));
        }

        CompoundBuilder builder = new(_bufferPool, _simulation.Shapes, Math.Max(children.Count, 1));
        try
        {
            if (children.Count == 0)
                throw new ArgumentException("At least one valid collider is required.", nameof(colliders));

            float weight = kind == RigidBodyKind.Dynamic ? MathF.Max(mass, Epsilon) / children.Count : 1f;
            foreach ((PhysicsColliderSpec spec, RigidPose pose) in children)
            {
                switch (spec.Kind)
                {
                    case PhysicsColliderKind.Sphere:
                        builder.Add(new Sphere(MathF.Max(spec.Radius, Epsilon)), pose, weight);
                        break;
                    case PhysicsColliderKind.Capsule:
                        builder.Add(CreateCapsule(spec), pose, weight);
                        break;
                    case PhysicsColliderKind.Mesh:
                        TypedIndex meshIndex = CreateMeshShape(spec, out BepuPhysics.Collidables.Mesh meshShape);
                        if (kind == RigidBodyKind.Dynamic)
                        {
                            BodyInertia meshInertia = meshShape.ComputeClosedInertia(weight);
                            builder.Add(meshIndex, pose, meshInertia.InverseInertiaTensor, 1f / meshInertia.InverseMass);
                        }
                        else
                        {
                            builder.AddForKinematic(meshIndex, pose, weight);
                        }
                        break;
                    default:
                        builder.Add(CreateBox(spec), pose, weight);
                        break;
                }
            }

            if (kind == RigidBodyKind.Dynamic)
            {
                builder.BuildDynamicCompound(out Buffer<CompoundChild> dynamicChildren, out inertia, out NVector3 center);
                centerOfMass = ToEngine(center);
                return dynamicChildren;
            }
            inertia = default;
            builder.BuildKinematicCompound(out Buffer<CompoundChild> kinematicChildren);
            centerOfMass = Vector3.Zero;
            return kinematicChildren;
        }
        finally
        {
            builder.Dispose();
        }
    }

    private TypedIndex CreateMeshShape(PhysicsColliderSpec spec, out BepuPhysics.Collidables.Mesh shape)
    {
        PhysicsMeshData mesh = spec.Mesh!;
        int triangleCount = mesh.Indices.Length / 3;
        for (int index = 0; index < triangleCount * 3; index++)
            if (mesh.Indices[index] >= mesh.Vertices.Length)
                throw new ArgumentException("Mesh collider contains an out-of-range vertex index.", nameof(spec));
        _bufferPool.Take<Triangle>(triangleCount, out Buffer<Triangle> triangles);
        NVector3 scale = ToSystem(spec.Size);
        for (int i = 0; i < triangleCount; i++)
        {
            int index = i * 3;
            triangles[i] = new Triangle(
                ToSystem(mesh.Vertices[mesh.Indices[index]]),
                ToSystem(mesh.Vertices[mesh.Indices[index + 1]]),
                ToSystem(mesh.Vertices[mesh.Indices[index + 2]]));
        }
        shape = new BepuPhysics.Collidables.Mesh(triangles, scale, _bufferPool);
        return _simulation.Shapes.Add(shape);
    }

    public void RemoveBody(BodyHandle handle)
    {
        TypedIndex shape = _simulation.Bodies[handle].Collidable.Shape;
        _simulation.Bodies.Remove(handle);
        _simulation.Shapes.RecursivelyRemoveAndDispose(shape, _bufferPool);
    }

    public void RemoveStatic(StaticHandle handle)
    {
        TypedIndex shape = _simulation.Statics[handle].Shape;
        _simulation.Statics.Remove(handle);
        _simulation.Shapes.RecursivelyRemoveAndDispose(shape, _bufferPool);
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

    public void SetBodyPosePreservingVelocity(BodyHandle handle, Vector3 position, Quaternion rotation)
    {
        BodyReference body = _simulation.Bodies[handle];
        body.Awake = true;
        body.Pose = new RigidPose(ToSystem(position), ToSystemQuaternion(rotation));
        body.UpdateBounds();
    }

    public void SetBodyAwake(BodyHandle handle, bool awake)
    {
        BodyReference body = _simulation.Bodies[handle];
        body.Awake = awake;
    }

    public Vector3 GetBodyPosition(BodyHandle handle) => ToEngine(_simulation.Bodies[handle].Pose.Position);

    public Quaternion GetBodyRotation(BodyHandle handle) => ToEngine(_simulation.Bodies[handle].Pose.Orientation);

    public Vector3 GetBodyLinearVelocity(BodyHandle handle) => ToEngine(_simulation.Bodies[handle].Velocity.Linear);

    public Vector3 GetBodyAngularVelocity(BodyHandle handle) => ToEngine(_simulation.Bodies[handle].Velocity.Angular);

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

    public void SetBodyVelocity(BodyHandle handle, Vector3 linearVelocity, Vector3 angularVelocity)
    {
        _simulation.Bodies[handle].Velocity = new BodyVelocity(ToSystem(linearVelocity), ToSystem(angularVelocity));
    }

    public void ApplyImpulse(BodyHandle handle, Vector3 impulse)
    {
        BodyReference body = _simulation.Bodies[handle];
        if (body.LocalInertia.InverseMass <= 0f)
            return;
        body.Awake = true;
        body.ApplyImpulse(ToSystem(impulse), NVector3.Zero);
    }

    public void ApplyImpulseAtPoint(BodyHandle handle, Vector3 impulse, Vector3 point)
    {
        BodyReference body = _simulation.Bodies[handle];
        if (body.LocalInertia.InverseMass <= 0f)
            return;
        body.Awake = true;
        body.ApplyImpulse(ToSystem(impulse), ToSystem(point - GetBodyPosition(handle)));
    }

    public void ApplyAngularImpulse(BodyHandle handle, Vector3 impulse)
    {
        BodyReference body = _simulation.Bodies[handle];
        if (body.LocalInertia.InverseMass <= 0f)
            return;
        body.Awake = true;
        body.ApplyAngularImpulse(ToSystem(impulse));
    }

    public float GetBodyMass(BodyHandle handle)
    {
        float inverseMass = _simulation.Bodies[handle].LocalInertia.InverseMass;
        return inverseMass > 0f ? 1f / inverseMass : 0f;
    }

    public bool RayCast(Vector3 origin, Vector3 direction, float maximumDistance, out PhysicsRayHit hit, BodyHandle? ignore = null)
    {
        hit = default;
        if (_disposed || maximumDistance <= 0f)
            return false;
        NVector3 normalized = ToSystem(direction);
        float length = normalized.Length();
        if (length < Epsilon)
            return false;
        normalized /= length;
        ClosestRayHitHandler handler = new(ignore);
        _simulation.RayCast(ToSystem(origin), normalized, maximumDistance, ref handler);
        if (!handler.Hit)
            return false;
        hit = new PhysicsRayHit(handler.T, ToEngine(handler.Normal));
        return true;
    }

    public void Step(float deltaTime) => _simulation.Timestep(deltaTime);

    private sealed class ClosestRayHitHandler(BodyHandle? ignore) : IRayHitHandler
    {
        public bool Hit;
        public float T = float.MaxValue;
        public NVector3 Normal;

        public bool AllowTest(CollidableReference collidable) =>
            !ignore.HasValue || !(collidable.Mobility != CollidableMobility.Static && collidable.BodyHandle == ignore.Value);

        public bool AllowTest(CollidableReference collidable, int child) => AllowTest(collidable);

        public void OnRayHit(in RayData ray, ref float maximumT, float t, in NVector3 normal, CollidableReference collidable, int childIndex)
        {
            if (t >= T)
                return;
            T = t;
            Normal = normal;
            Hit = true;
            maximumT = t;
        }
    }

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
