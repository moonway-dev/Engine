using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.Constraints;
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
    private readonly BepuUtilities.ThreadDispatcher _dispatcher = new(Environment.ProcessorCount);
    private readonly PhysicsBodySettings _settings;
    private bool _disposed;

    public PhysicsWorld(Vector3 gravity)
    {
        _bufferPool = new BufferPool();
        _settings = new PhysicsBodySettings { Gravity = ToSystem(gravity) };
        _simulation = Simulation.Create(
            _bufferPool,
            new NarrowPhaseCallbacks(_settings),
            new PoseIntegratorCallbacks(_settings),
            new SolveDescription(8, 2));
    }

    public Vector3 Gravity
    {
        get => ToEngine(_settings.Gravity);
        set => _settings.Gravity = ToSystem(value);
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
        BodyHandle handle = _simulation.Bodies.Add(description);
        _settings.Bodies.Add(handle, new PhysicsBodyState(description.LocalInertia, ToEngine(description.Pose.Position), ToEngine(description.Pose.Orientation)));
        return handle;
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
        BodyHandle handle = _simulation.Bodies.Add(description);
        _settings.Bodies.Add(handle, new PhysicsBodyState(description.LocalInertia, ToEngine(description.Pose.Position), ToEngine(description.Pose.Orientation)));
        return handle;
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
        _settings.Bodies.Remove(handle);
        _simulation.Shapes.RecursivelyRemoveAndDispose(shape, _bufferPool);
    }

    public const float LinearConstraintFrequency = 60f;
    public const float AngularConstraintFrequency = 10f;

    public void RemoveJoint(ConstraintHandle handle)
    {
        if (!_simulation.Solver.ConstraintExists(handle))
            return;
        _simulation.Solver.Remove(handle);
    }

    private NVector3 ToLocalOffset(BodyHandle body, Vector3 worldPoint)
    {
        RigidPose pose = _simulation.Bodies[body].Pose;
        return ToSystem(ToEngine(pose.Orientation).Inverse.Rotate(worldPoint - ToEngine(pose.Position)));
    }

    private NVector3 ToLocalAxis(BodyHandle body, Vector3 worldDirection)
    {
        NQuaternion rotation = _simulation.Bodies[body].Pose.Orientation;
        return ToSystem(ToEngine(rotation).Inverse.Rotate(worldDirection));
    }

    private NQuaternion ToLocalBasis(BodyHandle body, NVector3 localAxis)
    {
        Vector3 axis = ToEngine(localAxis);
        if (axis.LengthSquared <= Epsilon * Epsilon)
            axis = Vector3.Up;
        else
            axis = axis.Normalized;
        Vector3 helper = MathF.Abs(axis.Y) < 0.999f ? Vector3.Up : Vector3.Right;
        Vector3 perpendicular = Vector3.Cross(axis, helper);
        perpendicular = perpendicular.LengthSquared > Epsilon * Epsilon ? perpendicular.Normalized : Vector3.Right;
        return ToSystemQuaternion(Quaternion.LookRotation(axis, perpendicular));
    }

    private ConstraintHandle Add<TDescription>(BodyHandle a, BodyHandle b, TDescription description)
        where TDescription : unmanaged, ITwoBodyConstraintDescription<TDescription>
    {
        if (a.Value == b.Value)
            throw new ArgumentException("A joint cannot connect a body to itself.");
        return _simulation.Solver.Add(a, b, description);
    }

    public ConstraintHandle[] AddFixedJoint(BodyHandle a, BodyHandle b)
    {
        RigidPose poseA = _simulation.Bodies[a].Pose;
        RigidPose poseB = _simulation.Bodies[b].Pose;
        Weld weld = new()
        {
            LocalOffset = ToLocalOffset(a, ToEngine(poseB.Position)),
            LocalOrientation = ToSystemQuaternion(ToEngine(poseA.Orientation).Inverse * ToEngine(poseB.Orientation)),
            SpringSettings = new SpringSettings(60f, 1f),
        };
        return [Add(a, b, weld)];
    }

    public ConstraintHandle[] AddBallJoint(BodyHandle a, BodyHandle b, Vector3 anchorA, Vector3 anchorB)
    {
        BallSocket socket = new()
        {
            LocalOffsetA = ToLocalOffset(a, anchorA),
            LocalOffsetB = ToLocalOffset(b, anchorB),
            SpringSettings = new SpringSettings(60f, 1f),
        };
        return [Add(a, b, socket)];
    }

    public ConstraintHandle[] AddHingeJoint(BodyHandle a, BodyHandle b, Vector3 anchorA, Vector3 anchorB, Vector3 worldAxis)
    {
        Vector3 axis = worldAxis.LengthSquared > Epsilon * Epsilon ? worldAxis.Normalized : Vector3.Up;
        BallSocket socket = new()
        {
            LocalOffsetA = ToLocalOffset(a, anchorA),
            LocalOffsetB = ToLocalOffset(b, anchorB),
            SpringSettings = new SpringSettings(LinearConstraintFrequency, 1f),
        };
        AngularHinge hinge = new()
        {
            LocalHingeAxisA = ToLocalAxis(a, axis),
            LocalHingeAxisB = ToLocalAxis(b, axis),
            SpringSettings = new SpringSettings(AngularConstraintFrequency, 1f),
        };
        return [Add(a, b, socket), Add(a, b, hinge)];
    }

    public ConstraintHandle[] AddSpringJoint(BodyHandle a, BodyHandle b, Vector3 anchorA, Vector3 anchorB, float targetDistance, float frequency, float dampingRatio)
    {
        Vector3 delta = anchorB - anchorA;
        Vector3 axis = delta.LengthSquared > Epsilon * Epsilon ? delta.Normalized : Vector3.Up;
        LinearAxisServo servo = new()
        {
            LocalOffsetA = ToLocalOffset(a, anchorA),
            LocalOffsetB = ToLocalOffset(b, anchorB),
            LocalPlaneNormal = ToLocalAxis(a, axis),
            TargetOffset = MathF.Max(0f, targetDistance),
            ServoSettings = new ServoSettings(float.MaxValue, 0f, float.MaxValue),
            SpringSettings = new SpringSettings(frequency, Math.Clamp(dampingRatio, 0f, 1f)),
        };
        return [Add(a, b, servo)];
    }

    public ConstraintHandle[] AddCharacterJoint(BodyHandle a, BodyHandle b, Vector3 anchorA, Vector3 anchorB, Vector3 twistAxis, float swingLimitDegrees, float twistLimitDegrees)
    {
        Vector3 axis = twistAxis.LengthSquared > Epsilon * Epsilon ? twistAxis.Normalized : Vector3.Up;
        List<ConstraintHandle> handles = [];
        BallSocket socket = new()
        {
            LocalOffsetA = ToLocalOffset(a, anchorA),
            LocalOffsetB = ToLocalOffset(b, anchorB),
            SpringSettings = new SpringSettings(LinearConstraintFrequency, 1f),
        };
        handles.Add(Add(a, b, socket));
        if (swingLimitDegrees > 0f && swingLimitDegrees < 179.9f)
        {
            SwingLimit swing = new()
            {
                AxisLocalA = ToLocalAxis(a, axis),
                AxisLocalB = ToLocalAxis(b, axis),
                MinimumDot = MathUtil.Clamp(MathF.Cos(swingLimitDegrees * MathF.PI / 180f), -1f, 1f),
                SpringSettings = new SpringSettings(AngularConstraintFrequency, 1f),
            };
            handles.Add(Add(a, b, swing));
        }
        if (twistLimitDegrees > 0f && twistLimitDegrees < 179.9f)
        {
            TwistLimit twist = new()
            {
                LocalBasisA = ToLocalBasis(a, ToLocalAxis(a, axis)),
                LocalBasisB = ToLocalBasis(b, ToLocalAxis(b, axis)),
                MinimumAngle = -twistLimitDegrees * MathF.PI / 180f,
                MaximumAngle = twistLimitDegrees * MathF.PI / 180f,
                SpringSettings = new SpringSettings(AngularConstraintFrequency, 1f),
            };
            handles.Add(Add(a, b, twist));
        }
        return [.. handles];
    }

    public ConstraintHandle[] AddLinearLimit(BodyHandle a, BodyHandle b, Vector3 worldAxis, Vector3 anchorA, Vector3 anchorB, float minimumOffset, float maximumOffset)
    {
        Vector3 axis = worldAxis.LengthSquared > Epsilon * Epsilon ? worldAxis.Normalized : Vector3.Up;
        LinearAxisLimit limit = new()
        {
            LocalOffsetA = ToLocalOffset(a, anchorA),
            LocalOffsetB = ToLocalOffset(b, anchorB),
            LocalAxis = ToLocalAxis(a, axis),
            MinimumOffset = minimumOffset,
            MaximumOffset = maximumOffset,
            SpringSettings = new SpringSettings(60f, 1f),
        };
        return [Add(a, b, limit)];
    }

    public void RemoveStatic(StaticHandle handle)
    {
        TypedIndex shape = _simulation.Statics[handle].Shape;
        _simulation.Statics.Remove(handle);
        _settings.StaticCollisions.Remove(handle);
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
        if (_settings.Bodies.TryGetValue(handle, out PhysicsBodyState? state))
        {
            state.PositionAnchor = position;
            state.RotationAnchor = rotation;
        }
        body.Velocity = default;
        body.UpdateBounds();
    }

    public void SetBodyPosePreservingVelocity(BodyHandle handle, Vector3 position, Quaternion rotation)
    {
        BodyReference body = _simulation.Bodies[handle];
        body.Awake = true;
        body.Pose = new RigidPose(ToSystem(position), ToSystemQuaternion(rotation));
        if (_settings.Bodies.TryGetValue(handle, out PhysicsBodyState? state))
        {
            state.PositionAnchor = position;
            state.RotationAnchor = rotation;
        }
        body.UpdateBounds();
    }

    public void SetBodyAwake(BodyHandle handle, bool awake)
    {
        BodyReference body = _simulation.Bodies[handle];
        body.Awake = awake;
    }

    public bool IsBodyAwake(BodyHandle handle) => _simulation.Bodies[handle].Awake;

    public Vector3 GetBodyPosition(BodyHandle handle) => ToEngine(_simulation.Bodies[handle].Pose.Position);

    public Quaternion GetBodyRotation(BodyHandle handle) => ToEngine(_simulation.Bodies[handle].Pose.Orientation);

    public Vector3 GetBodyLinearVelocity(BodyHandle handle) => ToEngine(_simulation.Bodies[handle].Velocity.Linear);

    public Vector3 GetBodyVelocityAtPoint(BodyHandle handle, Vector3 point)
    {
        BodyReference body = _simulation.Bodies[handle];
        Vector3 position = ToEngine(body.Pose.Position);
        Vector3 angular = ToEngine(body.Velocity.Angular);
        return ToEngine(body.Velocity.Linear) + Vector3.Cross(angular, point - position);
    }

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
        SetBodyVelocity(handle, linearVelocity, GetBodyAngularVelocity(handle));
    }

    public void SetBodyVelocity(BodyHandle handle, Vector3 linearVelocity, Vector3 angularVelocity)
    {
        BodyReference body = _simulation.Bodies[handle];
        if (_settings.Bodies.TryGetValue(handle, out PhysicsBodyState? state))
        {
            linearVelocity = state.FilterLinear(linearVelocity);
            angularVelocity = state.FilterAngularWorld(angularVelocity, ToEngine(body.Pose.Orientation));
        }
        if (linearVelocity.LengthSquared > 0f || angularVelocity.LengthSquared > 0f)
            body.Awake = true;
        body.Velocity = new BodyVelocity(ToSystem(linearVelocity), ToSystem(angularVelocity));
    }

    public void ApplyImpulse(BodyHandle handle, Vector3 impulse)
    {
        BodyReference body = _simulation.Bodies[handle];
        if (body.LocalInertia.InverseMass <= 0f)
            return;
        body.Awake = true;
        body.ApplyImpulse(ToSystem(impulse), NVector3.Zero);
        EnforceConstraints(handle, _settings.Bodies[handle]);
    }

    public void ApplyImpulseAtPoint(BodyHandle handle, Vector3 impulse, Vector3 point)
    {
        BodyReference body = _simulation.Bodies[handle];
        if (body.LocalInertia.InverseMass <= 0f)
            return;
        body.Awake = true;
        body.ApplyImpulse(ToSystem(impulse), ToSystem(point - GetBodyPosition(handle)));
        EnforceConstraints(handle, _settings.Bodies[handle]);
    }

    public void ApplyAngularImpulse(BodyHandle handle, Vector3 impulse)
    {
        BodyReference body = _simulation.Bodies[handle];
        if (body.LocalInertia.InverseMass <= 0f)
            return;
        body.Awake = true;
        body.ApplyAngularImpulse(ToSystem(impulse));
        EnforceConstraints(handle, _settings.Bodies[handle]);
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
        ClosestRayHitHandler handler = new(ignore, _settings);
        _simulation.RayCast(ToSystem(origin), normalized, maximumDistance, ref handler);
        if (!handler.Hit)
            return false;
        hit = new PhysicsRayHit(handler.T, ToEngine(handler.Normal));
        return true;
    }

    public RigidbodySettings GetBodySettings(BodyHandle handle) => _settings.Bodies[handle].Settings;

    public void ConfigureBody(BodyHandle handle, RigidbodySettings settings, bool force = false)
    {
        settings.Validate();
        PhysicsBodyState state = _settings.Bodies[handle];
        if (!force && state.Settings == settings)
            return;
        BodyReference body = _simulation.Bodies[handle];
        if (state.Settings.Constraints != settings.Constraints)
        {
            state.PositionAnchor = ToEngine(body.Pose.Position);
            state.RotationAnchor = ToEngine(body.Pose.Orientation);
        }
        state.Settings = settings;
        BodyInertia inertia = state.Inertia;
        if ((settings.Constraints & RigidbodyConstraints.FreezeRotationX) != 0)
        {
            inertia.InverseInertiaTensor.XX = 0f;
            inertia.InverseInertiaTensor.YX = 0f;
            inertia.InverseInertiaTensor.ZX = 0f;
        }
        if ((settings.Constraints & RigidbodyConstraints.FreezeRotationY) != 0)
        {
            inertia.InverseInertiaTensor.YY = 0f;
            inertia.InverseInertiaTensor.YX = 0f;
            inertia.InverseInertiaTensor.ZY = 0f;
        }
        if ((settings.Constraints & RigidbodyConstraints.FreezeRotationZ) != 0)
        {
            inertia.InverseInertiaTensor.ZZ = 0f;
            inertia.InverseInertiaTensor.ZX = 0f;
            inertia.InverseInertiaTensor.ZY = 0f;
        }
        body.SetLocalInertia(inertia);
        body.GetDescription(out BodyDescription description);
        description.Collidable.Continuity = settings.CollisionDetection switch
        {
            RigidbodyCollisionDetection.Continuous => ContinuousDetection.Continuous(1e-3f, 1e-2f),
            RigidbodyCollisionDetection.Discrete => ContinuousDetection.Discrete,
            _ => ContinuousDetection.Passive,
        };
        description.Collidable.MaximumSpeculativeMargin = settings.CollisionDetection == RigidbodyCollisionDetection.Continuous
            ? 0.1f
            : float.MaxValue;
        body.ApplyDescription(description);
        body.Awake = true;
        EnforceConstraints(handle, state);
    }

    public void SetBodyMass(BodyHandle handle, float mass)
    {
        if (!float.IsFinite(mass) || mass <= 0f)
            throw new ArgumentOutOfRangeException(nameof(mass));
        PhysicsBodyState state = _settings.Bodies[handle];
        if (state.Inertia.InverseMass <= 0f)
            return;
        float scale = (1f / mass) / state.Inertia.InverseMass;
        state.Inertia.InverseMass = 1f / mass;
        state.Inertia.InverseInertiaTensor.XX *= scale;
        state.Inertia.InverseInertiaTensor.YX *= scale;
        state.Inertia.InverseInertiaTensor.YY *= scale;
        state.Inertia.InverseInertiaTensor.ZX *= scale;
        state.Inertia.InverseInertiaTensor.ZY *= scale;
        state.Inertia.InverseInertiaTensor.ZZ *= scale;
        ConfigureBody(handle, state.Settings, true);
    }

    public void SetStaticCollisionEnabled(StaticHandle handle, bool enabled)
    {
        bool wasEnabled = !_settings.StaticCollisions.TryGetValue(handle, out bool previous) || previous;
        if (wasEnabled == enabled)
            return;
        _settings.StaticCollisions[handle] = enabled;
        _simulation.Statics.GetDescription(handle, out StaticDescription description);
        _simulation.Statics.ApplyDescription(handle, description);
    }

    public void Step(float deltaTime)
    {
        if (!float.IsFinite(deltaTime) || deltaTime <= 0f)
            throw new ArgumentOutOfRangeException(nameof(deltaTime));
        foreach ((BodyHandle handle, PhysicsBodyState state) in _settings.Bodies)
            if (state.Settings.Constraints != RigidbodyConstraints.None)
                EnforceConstraints(handle, state);
        _simulation.Timestep(deltaTime);
        foreach ((BodyHandle handle, PhysicsBodyState state) in _settings.Bodies)
            if (state.Settings.Constraints != RigidbodyConstraints.None)
                EnforceConstraints(handle, state);
    }

    private void EnforceConstraints(BodyHandle handle, PhysicsBodyState state)
    {
        BodyReference body = _simulation.Bodies[handle];
        RigidbodyConstraints constraints = state.Settings.Constraints;
        Vector3 position = ToEngine(body.Pose.Position);
        Quaternion rotation = ToEngine(body.Pose.Orientation);
        if ((constraints & RigidbodyConstraints.FreezePositionX) != 0)
            position.X = state.PositionAnchor.X;
        if ((constraints & RigidbodyConstraints.FreezePositionY) != 0)
            position.Y = state.PositionAnchor.Y;
        if ((constraints & RigidbodyConstraints.FreezePositionZ) != 0)
            position.Z = state.PositionAnchor.Z;
        if ((constraints & RigidbodyConstraints.FreezeRotation) == RigidbodyConstraints.FreezeRotation)
            rotation = state.RotationAnchor;
        else if ((constraints & RigidbodyConstraints.FreezeRotation) != 0)
        {
            Quaternion delta = state.RotationAnchor.Inverse * rotation;
            if (delta.W < 0f)
                delta = -delta;
            Vector3 vector = state.FilterAngular(new Vector3(delta.X, delta.Y, delta.Z));
            delta = new Quaternion(vector.X, vector.Y, vector.Z, delta.W).Normalized;
            rotation = state.RotationAnchor * delta;
        }
        bool poseChanged = position != ToEngine(body.Pose.Position) || rotation != ToEngine(body.Pose.Orientation);
        body.Pose = new RigidPose(ToSystem(position), ToSystemQuaternion(rotation));
        body.Velocity.Linear = ToSystem(state.FilterLinear(ToEngine(body.Velocity.Linear)));
        body.Velocity.Angular = ToSystem(state.FilterAngularWorld(ToEngine(body.Velocity.Angular), rotation));
        if (poseChanged)
            body.UpdateBounds();
    }

    private sealed class ClosestRayHitHandler(BodyHandle? ignore, PhysicsBodySettings settings) : IRayHitHandler
    {
        public bool Hit;
        public float T = float.MaxValue;
        public NVector3 Normal;

        public bool AllowTest(CollidableReference collidable) =>
            settings.AllowsCollision(collidable) && (!ignore.HasValue || !(collidable.Mobility != CollidableMobility.Static && collidable.BodyHandle == ignore.Value));

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
        _dispatcher.Dispose();
        if (_disposed)
            return;
        _disposed = true;
        _simulation.Dispose();
        _bufferPool.Clear();
    }
}
