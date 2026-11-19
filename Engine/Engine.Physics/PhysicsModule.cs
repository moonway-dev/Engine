using BepuPhysics;
using Engine.Core;
using Engine.Mathematics;
using Engine.Scene;

namespace Engine.Physics;

public sealed class PhysicsModule : IUpdatableModule, IFixedUpdateModule
{
    public const string LogCategory = "Physics";

    private readonly Dictionary<GameObject, Registration> _bodies = [];
    private readonly HashSet<(Guid ObjectId, string Message)> _warnedBodies = [];
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
        public float Mass;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 CenterOfMassOffset;
        public Vector3 PreviousPosition;
        public Quaternion PreviousRotation = Quaternion.Identity;
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
            }
            else
            {
                gameObject.Transform.SetRenderPose(registration.PreviousPosition, registration.PreviousRotation);
            }
        }
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
        _world!.Step(frameTime.FixedDeltaTime);
        ReadBackTransforms();
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
        $"{kind}|{string.Join(";", specs.Select(spec => $"{spec.Kind}:{spec.Center}:{spec.Rotation}:{spec.Size}:{spec.Radius:R}:{spec.MeshKey}"))}";

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
