using BepuPhysics;
using System.Globalization;
using Engine.Core;
using Engine.Mathematics;
using Engine.Scene;

namespace Engine.Physics;

public sealed class PhysicsModule : IUpdatableModule, IFixedUpdateModule
{
    public const string LogCategory = "Physics";

    private readonly Dictionary<GameObject, Registration> _bodies = [];
    private readonly HashSet<(Guid ObjectId, string Message)> _warnedBodies = [];
    private readonly Dictionary<Component, List<ConstraintHandle>> _joints = [];
    private readonly Dictionary<Component, string> _jointSignatures = [];
    private readonly List<WheelColliderComponent> _groundedWheels = [];
    private PhysicsWorld? _world;
    private ILogger? _log;
    private Scene.Scene? _attachedScene;

    private readonly record struct Configuration(RigidBodyKind Kind, string ConfigKey);

    private sealed class Registration(Configuration configuration)
    {
        public Configuration Configuration = configuration;
        public RigidBodyComponent? RigidBody;
        public BodyHandle Body;
        public StaticHandle Static;
        public bool HasBody;
        public long Generation;
        public float Mass;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 CenterOfMassOffset;
        public Vector3 PreviousPosition;
        public Quaternion PreviousRotation = Quaternion.Identity;
        public Vector3 RenderPosition;
        public Quaternion RenderRotation = Quaternion.Identity;
    }

    public string Name => "Physics";
    public Vector3 Gravity { get; set; } = new(0f, -9.81f, 0f);
    public bool SimulationActive { get; set; } = true;
    public Func<string, PhysicsMeshData?>? MeshDataResolver { get; set; }
    public float RenderInterpolationAlpha { get; private set; } = 1f;
    public PhysicsWorld World => _world ?? throw new InvalidOperationException("Physics module is not initialized.");

    public bool TryGetBodyHandle(GameObject gameObject, out BodyHandle body)
    {
        body = default;
        Synchronize();
        if (_world is null || !_bodies.TryGetValue(gameObject, out Registration? registration))
            return false;
        if (!registration.HasBody || registration.Configuration.Kind == RigidBodyKind.Static || registration.RigidBody is { IsActiveInHierarchy: false })
            return false;
        body = registration.Body;
        return true;
    }

    public bool TryGetStaticHandle(GameObject gameObject, out StaticHandle staticBody)
    {
        staticBody = default;
        Synchronize();
        if (_world is null || !_bodies.TryGetValue(gameObject, out Registration? registration))
            return false;
        if (!registration.HasBody || registration.Configuration.Kind != RigidBodyKind.Static || registration.RigidBody is { IsActiveInHierarchy: false })
            return false;
        staticBody = registration.Static;
        return true;
    }

    public bool TryGetRenderPose(GameObject gameObject, out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = Quaternion.Identity;
        if (_world is null || !_bodies.TryGetValue(gameObject, out Registration? registration))
            return false;
        position = registration.RenderPosition;
        rotation = registration.RenderRotation;
        return true;
    }

    public void Synchronize()
    {
        if (_world is not null && _attachedScene is not null)
            SyncBodies();
    }

    public void AttachScene(Scene.Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ClearRegistrations();
        _attachedScene = scene;
        if (_world is not null)
        {
            _world.Dispose();
            _world = new PhysicsWorld(Gravity);
        }
    }

    public void Initialize(IEngineContext context)
    {
        _log = context.Logging.CreateLogger(LogCategory);
        ComponentRegistry.RegisterGlobal(() => new RigidBodyComponent());
        ComponentRegistry.RegisterGlobal(() => new BoxColliderComponent());
        ComponentRegistry.RegisterGlobal(() => new SphereColliderComponent());
        ComponentRegistry.RegisterGlobal(() => new CapsuleColliderComponent());
        ComponentRegistry.RegisterGlobal(() => new MeshColliderComponent());
        ComponentRegistry.RegisterGlobal(() => new FixedJointComponent());
        ComponentRegistry.RegisterGlobal(() => new HingeJointComponent());
        ComponentRegistry.RegisterGlobal(() => new SpringJointComponent());
        ComponentRegistry.RegisterGlobal(() => new CharacterJointComponent());
        ComponentRegistry.RegisterGlobal(() => new ConfigurableJointComponent());
        ComponentRegistry.RegisterGlobal(() => new ConstantForceComponent());
        ComponentRegistry.RegisterGlobal(() => new WheelColliderComponent());
        _world = new PhysicsWorld(Gravity);
        _log.Information($"Physics initialized (gravity {Gravity.Y} m/s^2)");
    }

    public void Update(in FrameTime frameTime)
    {
        if (_world is null || _attachedScene is null)
            return;
        SyncBodies();
        RenderInterpolationAlpha = SimulationActive ? Math.Clamp(frameTime.FixedInterpolationAlpha, 0f, 1f) : 1f;
        foreach ((GameObject gameObject, Registration registration) in _bodies)
        {
            RigidbodyInterpolation interpolation = registration.RigidBody?.Interpolation ?? RigidbodyInterpolation.Interpolate;
            if (!SimulationActive || registration.Configuration.Kind != RigidBodyKind.Dynamic || !registration.HasBody || interpolation == RigidbodyInterpolation.None)
            {
                gameObject.Transform.ClearRenderPose();
                registration.RenderPosition = gameObject.Transform.Position;
                registration.RenderRotation = gameObject.Transform.Rotation;
                continue;
            }
            if (interpolation == RigidbodyInterpolation.Extrapolate)
            {
                float dt = frameTime.FixedDeltaTime * RenderInterpolationAlpha;
                Vector3 position = gameObject.Transform.Position + _world.GetBodyLinearVelocity(registration.Body) * dt;
                Vector3 angular = _world.GetBodyAngularVelocity(registration.Body);
                Quaternion rotation = angular.LengthSquared > MathUtil.Epsilon * MathUtil.Epsilon
                    ? Quaternion.FromAxisAngle(angular.Normalized, angular.Length * dt) * gameObject.Transform.Rotation
                    : gameObject.Transform.Rotation;
                gameObject.Transform.SetRenderPose(position, rotation, overridesCurrent: true);
                registration.RenderPosition = position;
                registration.RenderRotation = rotation;
            }
            else
            {
                gameObject.Transform.SetRenderPose(registration.PreviousPosition, registration.PreviousRotation);
                registration.RenderPosition = Vector3.Lerp(registration.PreviousPosition, gameObject.Transform.Position, RenderInterpolationAlpha);
                registration.RenderRotation = Quaternion.Slerp(registration.PreviousRotation, gameObject.Transform.Rotation, RenderInterpolationAlpha);
            }
        }
        ApplyWheelVisuals();
    }

    public void FixedUpdate(in FrameTime frameTime)
    {
        if (_world is null || _attachedScene is null)
            return;
        SyncBodies();
        if (SimulationActive)
            DoFixedUpdate(frameTime);
    }

    public void SimulateOnce(in FrameTime frameTime)
    {
        if (_world is null || _attachedScene is null)
            return;
        SyncBodies();
        DoFixedUpdate(frameTime);
    }

    private void DoFixedUpdate(in FrameTime frameTime)
    {

        foreach ((GameObject gameObject, Registration registration) in _bodies)
            if (registration.Configuration.Kind == RigidBodyKind.Dynamic && registration.HasBody)
                SetPreviousPose(gameObject, registration);
        float deltaTime = frameTime.FixedDeltaTime;
        ApplyConstantForces(deltaTime);
        ApplyWheelColliders(deltaTime);
        _world!.Step(deltaTime);
        ApplyJointMotors(deltaTime);
        ReadBackTransforms();
    }

    private void ApplyConstantForces(float deltaTime)
    {
        if (_world is null || _attachedScene is null)
            return;
        foreach (GameObject gameObject in _attachedScene.EnumerateHierarchy())
        {
            if (!gameObject.IsActiveInHierarchy || !_bodies.TryGetValue(gameObject, out Registration? registration))
                continue;
            if (registration.Configuration.Kind != RigidBodyKind.Dynamic || !registration.HasBody)
                continue;
            foreach (ConstantForceComponent force in gameObject.Components.OfType<ConstantForceComponent>())
            {
                if (!force.Enabled)
                    continue;
                force.Apply(_world, registration.Body, gameObject.Transform.Rotation, deltaTime);
            }
        }
    }

    private void ApplyWheelVisuals()
    {
        foreach (GameObject gameObject in _attachedScene!.EnumerateHierarchy())
        {
            foreach (WheelColliderComponent wheel in gameObject.Components.OfType<WheelColliderComponent>())
            {
                GameObject chassis = !_bodies.ContainsKey(wheel.Owner) && wheel.Owner.Parent is { } parent ? parent : wheel.Owner;
                if (!wheel.HasVisual || !_bodies.TryGetValue(chassis, out Registration? registration) || !registration.HasBody)
                    continue;
                Quaternion rotation = registration.RenderRotation;
                Vector3 position = registration.RenderPosition;
                Vector3 up = rotation.Rotate(Vector3.Up);
                Vector3 forward = rotation.Rotate(Vector3.Forward);
                Vector3 anchor = position + rotation.Rotate(wheel.LocalAnchor);
                float travel = wheel.LastTravel;
                if (wheel.Enabled && _world is not null)
                {
                    SuspensionProbeState state = WheelColliderComponent.ProbeSupport(_world, registration.Body, anchor, up, wheel.SuspensionDistance, wheel.Radius, out float probed, out _, out _);
                    if (state == SuspensionProbeState.Supported)
                        travel = probed;
                }
                wheel.UpdateVisualOnly(wheel.Owner.Transform, rotation, anchor, -up, travel, up, forward, wheel.SteerAngle * MathF.PI / 180f, wheel.LastRoll);
            }
        }
    }

    private void ApplyJointMotors(float deltaTime)
    {
        foreach ((Component component, _) in _joints)
        {
            if (component is HingeJointComponent hinge)
                hinge.ApplyMotor(this, deltaTime);
        }
    }

    private void ApplyWheelColliders(float deltaTime)
    {
        if (_world is null || _attachedScene is null)
            return;
        _groundedWheels.Clear();
        Dictionary<GameObject, List<(WheelColliderComponent Wheel, Engine.Scene.Transform Transform)>> wheelsByChassis = [];
        foreach (GameObject gameObject in _attachedScene.EnumerateHierarchy())
        {
            if (!gameObject.IsActiveInHierarchy)
                continue;
            foreach (WheelColliderComponent wheel in gameObject.Components.OfType<WheelColliderComponent>())
            {
                if (!wheel.Enabled)
                    continue;
                GameObject chassis = !_bodies.ContainsKey(wheel.Owner) && wheel.Owner.Parent is { } parent ? parent : wheel.Owner;
                if (!wheelsByChassis.TryGetValue(chassis, out List<(WheelColliderComponent Wheel, Engine.Scene.Transform Transform)>? list))
                    wheelsByChassis[chassis] = list = [];
                list.Add((wheel, wheel.Owner.Transform));
            }
        }
        foreach ((GameObject chassis, List<(WheelColliderComponent Wheel, Engine.Scene.Transform Transform)> wheels) in wheelsByChassis)
        {
            if (!_bodies.TryGetValue(chassis, out Registration? registration))
                continue;
            if (registration.Configuration.Kind != RigidBodyKind.Dynamic || !registration.HasBody)
                continue;
            float sprungMass = MathF.Max(registration.Mass / wheels.Count, 0.1f);
            foreach ((WheelColliderComponent wheel, Engine.Scene.Transform transform) in wheels)
            {
                wheel.Apply(_world, registration.Body, chassis.Transform, transform, sprungMass, deltaTime);
                if (wheel.IsGrounded)
                    _groundedWheels.Add(wheel);
            }
            ApplyAntiRollBars(_world, registration.Body, chassis.Transform, wheels, deltaTime);
        }
    }

    private static void ApplyAntiRollBars(
        PhysicsWorld world,
        BodyHandle body,
        Transform chassisTransform,
        List<(WheelColliderComponent Wheel, Engine.Scene.Transform Transform)> wheels,
        float deltaTime)
    {
        Quaternion rotation = chassisTransform.Rotation;
        Vector3 up = rotation.Rotate(Vector3.Up);
        Vector3 position = chassisTransform.Position;
        for (int i = 0; i < wheels.Count; i++)
        {
            (WheelColliderComponent leftWheel, _) = wheels[i];
            if (leftWheel.AntiRollStiffness <= 0f)
                continue;
            for (int j = i + 1; j < wheels.Count; j++)
            {
                (WheelColliderComponent rightWheel, _) = wheels[j];
                if (MathF.Abs(MathF.Abs(rightWheel.LocalAnchor.Z) - MathF.Abs(leftWheel.LocalAnchor.Z)) > 0.05f)
                    continue;
                if (leftWheel.LocalAnchor.X * rightWheel.LocalAnchor.X >= 0f)
                    continue;
                float stiffness = (leftWheel.AntiRollStiffness + rightWheel.AntiRollStiffness) * 0.5f;
                if (stiffness <= 0f)
                    break;
                float compressionDifference = leftWheel.Compression - rightWheel.Compression;
                if (MathF.Abs(compressionDifference) < MathUtil.Epsilon)
                    break;
                float impulse = compressionDifference * stiffness * deltaTime;
                Vector3 leftAnchor = position + rotation.Rotate(leftWheel.LocalAnchor);
                Vector3 rightAnchor = position + rotation.Rotate(rightWheel.LocalAnchor);
                world.ApplyImpulseAtPoint(body, up * impulse, leftAnchor);
                world.ApplyImpulseAtPoint(body, -up * impulse, rightAnchor);
                break;
            }
        }
    }

    internal bool TryGetJointBody(GameObject gameObject, out BodyHandle body)
    {
        body = default;
        if (_world is null || !_bodies.TryGetValue(gameObject, out Registration? registration) || !registration.HasBody)
            return false;
        if (registration.Configuration.Kind == RigidBodyKind.Static)
            return false;
        body = registration.Body;
        return true;
    }

    internal Vector3 GetBodyAngularVelocityDifference(BodyHandle a, BodyHandle b) =>
        _world is null ? Vector3.Zero : _world.GetBodyAngularVelocity(a) - _world.GetBodyAngularVelocity(b);

    internal float GetJointMass(BodyHandle body) =>
        _bodies.Values.FirstOrDefault(registration => registration.HasBody && registration.Body == body) is { } registration
            ? MathF.Max(registration.Mass, 0.1f)
            : 1f;

    internal void ApplyJointImpulse(BodyHandle body, Vector3 impulse)
    {
        if (_world is null)
            return;
        _world.ApplyAngularImpulse(body, impulse);
    }

    internal Vector3 GetJointBodyAngularVelocity(BodyHandle body) =>
        _world is null ? Vector3.Zero : _world.GetBodyAngularVelocity(body);

    internal Vector3 GetJointBodyPosition(BodyHandle body) =>
        _world is null ? Vector3.Zero : _world.GetBodyPosition(body);

    internal Vector3 GetJointBodyLinearVelocity(BodyHandle body) =>
        _world is null ? Vector3.Zero : _world.GetBodyLinearVelocity(body);

    internal void SetJointBodyVelocity(BodyHandle body, Vector3 linearVelocity, Vector3 angularVelocity)
    {
        if (_world is null)
            return;
        _world.SetBodyVelocity(body, linearVelocity, angularVelocity);
        _world.SetBodyAwake(body, true);
    }

    private void SyncJoints()
    {
        if (_world is null || _attachedScene is null)
            return;
        HashSet<Component> active = [];
        foreach (GameObject gameObject in _attachedScene.EnumerateHierarchy())
        {
            if (!gameObject.IsActiveInHierarchy)
                continue;
            foreach (Component component in gameObject.Components)
            {
                if (component is not PhysicsJointComponent joint || !joint.Enabled)
                    continue;
                if (joint.ConnectedBody.Resolve(_attachedScene) is not { } connected
                    || !TryGetJointBody(gameObject, out BodyHandle bodyA)
                    || !TryGetJointBody(connected, out BodyHandle bodyB))
                    continue;
                if (bodyA.Value == bodyB.Value)
                    continue;
                active.Add(component);
                string signature = $"{joint.BuildSignature()}|B{bodyA.Value}|B{bodyB.Value}|G{_bodies[gameObject].Generation}|G{_bodies[connected].Generation}";
                if (_joints.TryGetValue(component, out List<ConstraintHandle>? existing)
                    && _jointSignatures.TryGetValue(component, out string? current)
                    && current == signature)
                    continue;
                RemoveJointState(component);
                try
                {
                    _joints[component] =
                    [
                        .. joint.CreateConstraints(_world, bodyA, bodyB, gameObject.Transform, connected.Transform),
                    ];
                    _jointSignatures[component] = signature;
                }
                catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
                {
                    _log?.Warning($"Joint '{component.GetType().Name}' on '{gameObject.Name}' failed: {exception.Message}");
                    _joints.Remove(component);
                    _jointSignatures.Remove(component);
                }
            }
        }
        foreach (Component component in _joints.Keys.Where(key => !active.Contains(key)).ToArray())
            RemoveJointState(component);
    }

    private void RemoveJointState(Component component)
    {
        if (!_joints.Remove(component, out List<ConstraintHandle>? handles))
            return;
        _jointSignatures.Remove(component);
        foreach (ConstraintHandle handle in handles)
        {
            try
            {
                _world?.RemoveJoint(handle);
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentOutOfRangeException or KeyNotFoundException)
            {
            }
        }
    }

    private void SyncBodies()
    {
        _world!.Gravity = Gravity;
        List<GameObject> active = [];
        foreach (GameObject gameObject in _attachedScene!.EnumerateHierarchy())
        {
            if (!gameObject.IsActiveInHierarchy)
                continue;
            RigidBodyComponent? rigidBody = gameObject.GetComponent<RigidBodyComponent>();
            if (rigidBody is { Enabled: false })
                continue;
            bool hasCollider = gameObject.Components.OfType<ColliderComponent>().Any(collider => collider.IsActiveInHierarchy);
            if (rigidBody is not null || hasCollider)
                active.Add(gameObject);
        }
        HashSet<GameObject> activeSet = [.. active];
        foreach (GameObject gameObject in _bodies.Keys.Where(gameObject => !activeSet.Contains(gameObject)).ToArray())
        {
            RemoveWorldBody(gameObject, _bodies[gameObject]);
            _bodies.Remove(gameObject);
        }
        foreach (GameObject gameObject in active)
        {
            RigidBodyComponent? rigidBody = gameObject.GetComponent<RigidBodyComponent>();
            RigidBodyKind kind = rigidBody?.Kind ?? RigidBodyKind.Static;
            float mass = rigidBody?.Mass ?? 1f;
            List<PhysicsColliderSpec> specs = CollectSpecs(gameObject, kind);
            Configuration configuration = new(kind, BuildConfigKey(kind, specs));
            if (!_bodies.TryGetValue(gameObject, out Registration? registration))
            {
                registration = new Registration(configuration) { RigidBody = rigidBody };
                _bodies.Add(gameObject, registration);
                CreateWorldBody(gameObject, registration, specs, mass);
            }
            else if (registration.RigidBody != rigidBody || registration.Configuration != configuration)
            {
                bool preserveVelocity = registration.HasBody && registration.Configuration.Kind != RigidBodyKind.Static && kind != RigidBodyKind.Static;
                Vector3 linear = preserveVelocity ? _world.GetBodyLinearVelocity(registration.Body) : Vector3.Zero;
                Vector3 angular = preserveVelocity ? _world.GetBodyAngularVelocity(registration.Body) : Vector3.Zero;
                RemoveWorldBody(gameObject, registration);
                registration.Configuration = configuration;
                registration.RigidBody = rigidBody;
                registration.CenterOfMassOffset = Vector3.Zero;
                CreateWorldBody(gameObject, registration, specs, mass);
                if (preserveVelocity && registration.HasBody)
                {
                    _world.SetBodyVelocity(registration.Body, linear, angular);
                }
                SetPreviousPose(gameObject, registration);
            }
            if (!registration.HasBody)
                continue;
            if (kind != RigidBodyKind.Static)
            {
                if (kind == RigidBodyKind.Dynamic && registration.Mass != mass)
                    _world.SetBodyMass(registration.Body, mass);
                registration.Mass = mass;
                _world.ConfigureBody(registration.Body, rigidBody?.Settings ?? RigidbodySettings.Default);
            }
            else
            {
                _world.SetStaticCollisionEnabled(registration.Static, rigidBody?.DetectCollisions ?? true);
            }
            Vector3 position = gameObject.Transform.Position;
            Quaternion rotation = gameObject.Transform.Rotation;
            if (position != registration.Position || rotation != registration.Rotation)
            {
                gameObject.Transform.ClearRenderPose();
                Vector3 physicsPosition = position + rotation.Rotate(registration.CenterOfMassOffset);
                if (kind == RigidBodyKind.Static)
                    _world.SetStaticPose(registration.Static, physicsPosition, rotation);
                else
                    _world.SetBodyPosePreservingVelocity(registration.Body, physicsPosition, rotation);
                SetPreviousPose(gameObject, registration);
            }
            CaptureTransform(gameObject, registration);
        }
        SyncJoints();
    }

    private List<PhysicsColliderSpec> CollectSpecs(GameObject gameObject, RigidBodyKind kind)
    {
        Vector3 scale = gameObject.Transform.Scale;
        scale = new Vector3(MathF.Abs(scale.X), MathF.Abs(scale.Y), MathF.Abs(scale.Z));
        List<PhysicsColliderSpec> specs = [];
        foreach (ColliderComponent collider in gameObject.Components.OfType<ColliderComponent>())
        {
            if (!collider.IsActiveInHierarchy)
                continue;
            if (collider.Ignore)
                continue;
            PhysicsColliderSpec spec = collider.CreateSpec(scale, MeshDataResolver);
            if (spec.Kind == PhysicsColliderKind.Mesh)
            {
                if (spec.Mesh is null)
                {
                    WarnOnce(gameObject, $"MeshCollider '{spec.MeshKey}' on '{gameObject.Name}' has no resolved mesh data.");
                    continue;
                }
                if (kind == RigidBodyKind.Dynamic)
                {
                    WarnOnce(gameObject, $"RigidBody on '{gameObject.Name}' is dynamic but has a MeshCollider; mesh colliders are skipped for dynamic bodies.");
                    continue;
                }
            }
            specs.Add(spec);
        }
        return specs;
    }

    private static string BuildConfigKey(RigidBodyKind kind, IReadOnlyList<PhysicsColliderSpec> specs) =>
        $"{kind}|{string.Join(";", specs.Select(spec => $"{spec.Kind}:{FormatVector(spec.Center)}:{FormatQuaternion(spec.Rotation)}:{FormatVector(spec.Size)}:{FormatFloat(spec.Radius)}:{spec.MeshKey}"))}";

    private static string FormatFloat(float value) =>
        MathF.Round(value, 4).ToString("R", CultureInfo.InvariantCulture);

    private static string FormatVector(Vector3 value) =>
        $"{FormatFloat(value.X)},{FormatFloat(value.Y)},{FormatFloat(value.Z)}";

    private static string FormatQuaternion(Quaternion value) =>
        $"{FormatFloat(value.X)},{FormatFloat(value.Y)},{FormatFloat(value.Z)},{FormatFloat(value.W)}";

    private void CreateWorldBody(GameObject gameObject, Registration registration, IReadOnlyList<PhysicsColliderSpec> specs, float mass)
    {
        if (specs.Count == 0)
        {
            WarnOnce(gameObject, $"{(registration.RigidBody is null ? "Collider" : "RigidBody")} on '{gameObject.Name}' has no valid collider; object is not simulated.");
            return;
        }
        Quaternion rotation = gameObject.Transform.Rotation;
        bool awake = registration.RigidBody?.InitialAwake ?? true;
        switch (registration.Configuration.Kind)
        {
            case RigidBodyKind.Static:
                registration.Static = _world!.AddStatic(gameObject.Transform.Position, rotation, specs);
                _world.SetStaticCollisionEnabled(registration.Static, registration.RigidBody?.DetectCollisions ?? true);
                break;
            case RigidBodyKind.Kinematic:
                registration.Body = _world!.AddKinematic(gameObject.Transform.Position, rotation, specs);
                break;
            default:
                registration.Body = _world!.AddDynamic(gameObject.Transform.Position, rotation, specs, mass, out Vector3 centerOfMassOffset);
                registration.CenterOfMassOffset = centerOfMassOffset;
                break;
        }
        registration.Generation++;
        registration.HasBody = true;
        registration.Mass = mass;
        if (registration.Configuration.Kind != RigidBodyKind.Static)
        {
            _world!.ConfigureBody(registration.Body, registration.RigidBody?.Settings ?? RigidbodySettings.Default);
            _world.SetBodyAwake(registration.Body, awake);
        }
        if (registration.RigidBody is not null)
        {
            registration.RigidBody.Body = registration.Body;
            registration.RigidBody.Static = registration.Static;
            registration.RigidBody.World = _world;
            registration.RigidBody.HasBody = true;
        }
        SetPreviousPose(gameObject, registration);
        CaptureTransform(gameObject, registration);
        registration.RenderPosition = gameObject.Transform.Position;
        registration.RenderRotation = gameObject.Transform.Rotation;
    }

    private void WarnOnce(GameObject gameObject, string message)
    {
        if (_warnedBodies.Add((gameObject.Id, message)))
            _log?.Warning(message);
    }

    private static void CaptureTransform(GameObject gameObject, Registration registration)
    {
        registration.Position = gameObject.Transform.Position;
        registration.Rotation = gameObject.Transform.Rotation;
    }

    private void SetPreviousPose(GameObject gameObject, Registration registration)
    {
        if (registration.Configuration.Kind == RigidBodyKind.Dynamic && registration.HasBody)
        {
            Quaternion rotation = _world!.GetBodyRotation(registration.Body);
            registration.PreviousRotation = rotation;
            registration.PreviousPosition = _world.GetBodyPosition(registration.Body) - rotation.Rotate(registration.CenterOfMassOffset);
        }
        else
        {
            gameObject.Transform.ClearRenderPose();
            registration.PreviousPosition = gameObject.Transform.Position;
            registration.PreviousRotation = gameObject.Transform.Rotation;
        }
    }

    private void RemoveWorldBody(GameObject gameObject, Registration registration)
    {
        gameObject.Transform.ClearRenderPose();
        if (registration.RigidBody is not null)
        {
            registration.RigidBody.HasBody = false;
            registration.RigidBody.World = null;
        }
        if (!registration.HasBody || _world is null)
            return;
        if (registration.Configuration.Kind == RigidBodyKind.Static)
            _world.RemoveStatic(registration.Static);
        else
            _world.RemoveBody(registration.Body);
        registration.HasBody = false;
    }

    private void ReadBackTransforms()
    {
        foreach ((GameObject gameObject, Registration registration) in _bodies)
        {
            if (registration.Configuration.Kind == RigidBodyKind.Static || !registration.HasBody)
                continue;
            Quaternion rotation = _world!.GetBodyRotation(registration.Body);
            gameObject.Transform.Rotation = rotation;
            gameObject.Transform.Position = _world.GetBodyPosition(registration.Body) - rotation.Rotate(registration.CenterOfMassOffset);
        }
        foreach ((GameObject gameObject, Registration registration) in _bodies)
            CaptureTransform(gameObject, registration);
    }

    private void ClearRegistrations()
    {
        foreach ((GameObject gameObject, Registration registration) in _bodies)
        {
            gameObject.Transform.ClearRenderPose();
            if (registration.RigidBody is null)
                continue;
            registration.RigidBody.HasBody = false;
            registration.RigidBody.World = null;
        }
        _bodies.Clear();
        foreach (Component component in _joints.Keys.ToArray())
            RemoveJointState(component);
        _joints.Clear();
        _jointSignatures.Clear();
        _groundedWheels.Clear();
    }

    public void Shutdown()
    {
        ClearRegistrations();
        _world?.Dispose();
        _world = null;
        _attachedScene = null;
        _log?.Information("Physics shut down");
    }
}
