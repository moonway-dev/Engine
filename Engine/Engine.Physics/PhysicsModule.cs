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
        public Vector3 Position;
        public Quaternion Rotation;
        public bool IsAwake;
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
        if (_world is null || !_bodies.TryGetValue(gameObject, out Registration? registration))
            return false;
        if (!registration.HasBody || registration.Configuration.Kind == RigidBodyKind.Static)
            return false;
        body = registration.Body;
        return true;
    }

    public bool TryGetStaticHandle(GameObject gameObject, out StaticHandle staticBody)
    {
        staticBody = default;
        if (_world is null || !_bodies.TryGetValue(gameObject, out Registration? registration))
            return false;
        if (!registration.HasBody || registration.Configuration.Kind != RigidBodyKind.Static)
            return false;
        staticBody = registration.Static;
        return true;
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
        if (_world is null || _attachedScene is null || !SimulationActive)
            return;
        SyncBodies();
        RenderInterpolationAlpha = Math.Clamp(frameTime.FixedInterpolationAlpha, 0f, 1f);
        foreach ((GameObject gameObject, Registration registration) in _bodies)
        {
            if (registration.Configuration.Kind != RigidBodyKind.Dynamic || !registration.HasBody)
            {
                gameObject.Transform.ClearRenderPose();
                continue;
            }
            gameObject.Transform.SetRenderPose(registration.PreviousPosition, registration.PreviousRotation);
        }
    }

    public void FixedUpdate(in FrameTime frameTime)
    {
        if (_world is null || _attachedScene is null || !SimulationActive)
            return;
        DoFixedUpdate(frameTime);
    }

    public void SimulateOnce(in FrameTime frameTime)
    {
        if (_world is null || _attachedScene is null)
            return;
        DoFixedUpdate(frameTime);
    }

    private void DoFixedUpdate(in FrameTime frameTime)
    {
        SyncBodies();
        foreach ((GameObject gameObject, Registration registration) in _bodies)
            if (registration.Configuration.Kind == RigidBodyKind.Dynamic && registration.HasBody)
                SetPreviousPose(gameObject, registration);
        _world.Step(frameTime.FixedDeltaTime);
        ReadBackTransforms();
    }

    private void SyncBodies()
    {
        List<GameObject> active = [];
        foreach (GameObject gameObject in _attachedScene!.EnumerateHierarchy())
        {
            if (!gameObject.IsActiveInHierarchy)
                continue;
            bool hasRigidBody = gameObject.Components.OfType<RigidBodyComponent>().Any(body => body.IsActiveInHierarchy);
            bool hasCollider = gameObject.Components.OfType<ColliderComponent>().Any(collider => collider.IsActiveInHierarchy);
            if (hasRigidBody || hasCollider)
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
            RigidBodyComponent? rigidBody = gameObject.Components.OfType<RigidBodyComponent>().FirstOrDefault(body => body.IsActiveInHierarchy);
            RigidBodyKind kind = rigidBody?.Kind ?? RigidBodyKind.Static;
            float mass = rigidBody?.Mass ?? 1f;
            List<PhysicsColliderSpec> specs = CollectSpecs(gameObject, kind);
            Configuration configuration = new(kind, BuildConfigKey(kind, mass, specs));
            if (!_bodies.TryGetValue(gameObject, out Registration? registration))
            {
                registration = new Registration(configuration) { RigidBody = rigidBody };
                _bodies.Add(gameObject, registration);
                CreateWorldBody(gameObject, registration, specs, mass);
            }
            else if (registration.RigidBody != rigidBody
                || registration.Configuration.Kind != configuration.Kind
                || registration.Configuration.ConfigKey != configuration.ConfigKey)
            {
                bool preserveVelocity = registration.HasBody
                    && registration.Configuration.Kind == RigidBodyKind.Dynamic
                    && configuration.Kind == RigidBodyKind.Dynamic;
                Vector3 linearVelocity = preserveVelocity ? _world!.GetBodyLinearVelocity(registration.Body) : Vector3.Zero;
                Vector3 angularVelocity = preserveVelocity ? _world!.GetBodyAngularVelocity(registration.Body) : Vector3.Zero;
                RemoveWorldBody(gameObject, registration);
                registration.Configuration = configuration;
                registration.RigidBody = rigidBody;
                registration.CenterOfMassOffset = Vector3.Zero;
                CreateWorldBody(gameObject, registration, specs, mass);
                if (preserveVelocity && registration.HasBody)
                {
                    _world!.SetBodyVelocity(registration.Body, linearVelocity, angularVelocity);
                    if (registration.RigidBody is not null)
                        registration.RigidBody.IsAwake = true;
                }
                SetPreviousPose(gameObject, registration);
            }
            else
            {
                if (!registration.HasBody)
                    continue;
                Vector3 position = gameObject.Transform.Position;
                Quaternion rotation = gameObject.Transform.Rotation;
                if (position != registration.Position || rotation != registration.Rotation)
                {
                    gameObject.Transform.ClearRenderPose();
                    Vector3 physicsPosition = position + rotation.Rotate(registration.CenterOfMassOffset);

                    if (configuration.Kind == RigidBodyKind.Static)
                        _world!.SetStaticPose(registration.Static, physicsPosition, rotation);
                    else if (configuration.Kind == RigidBodyKind.Dynamic)
                        _world!.SetBodyPosePreservingVelocity(registration.Body, physicsPosition, rotation);
                    else
                        _world!.SetBodyPose(registration.Body, physicsPosition, rotation);
                    SetPreviousPose(gameObject, registration);
                }
                if (configuration.Kind != RigidBodyKind.Static && rigidBody is not null && rigidBody.IsAwake != registration.IsAwake)
                    _world!.SetBodyAwake(registration.Body, rigidBody.IsAwake);
                CaptureTransform(gameObject, registration);
            }
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

    private static string BuildConfigKey(RigidBodyKind kind, float mass, IReadOnlyList<PhysicsColliderSpec> specs)
    {
        return $"{kind}:{mass:R}|{string.Join(";", specs.Select(spec =>
            $"{spec.Kind}:{spec.Center}:{spec.Rotation}:{spec.Size}:{spec.Radius:R}:{spec.MeshKey}"))}";
    }

    private void CreateWorldBody(GameObject gameObject, Registration registration, IReadOnlyList<PhysicsColliderSpec> specs, float mass)
    {
        if (specs.Count == 0)
        {
            string source = registration.RigidBody is null ? "Collider" : "RigidBody";
            WarnOnce(gameObject, $"{source} on '{gameObject.Name}' has no valid collider; object is not simulated.");
            return;
        }

        Quaternion rotation = gameObject.Transform.Rotation;
        switch (registration.Configuration.Kind)
        {
            case RigidBodyKind.Static:
                registration.Static = _world!.AddStatic(gameObject.Transform.Position, rotation, specs);
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
        SetPreviousPose(gameObject, registration);
        if (registration.RigidBody is not null)
        {
            registration.RigidBody.Body = registration.Body;
            registration.RigidBody.Static = registration.Static;
            registration.RigidBody.HasBody = true;
            if (registration.Configuration.Kind != RigidBodyKind.Static)
                _world!.SetBodyAwake(registration.Body, registration.RigidBody.IsAwake);
        }
        CaptureTransform(gameObject, registration);
    }

    private void WarnOnce(GameObject gameObject, string message)
    {
        if (!_warnedBodies.Add((gameObject.Id, message)))
            return;
        _log?.Warning(message);
    }

    private static void CaptureTransform(GameObject gameObject, Registration registration)
    {
        registration.Position = gameObject.Transform.Position;
        registration.Rotation = gameObject.Transform.Rotation;
        registration.IsAwake = registration.RigidBody?.IsAwake ?? true;
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
        if (!registration.HasBody || _world is null)
            return;
        if (registration.Configuration.Kind == RigidBodyKind.Static)
            _world.RemoveStatic(registration.Static);
        else
            _world.RemoveBody(registration.Body);
        registration.HasBody = false;
        if (registration.RigidBody is not null)
            registration.RigidBody.HasBody = false;
    }

    private void ReadBackTransforms()
    {
        foreach ((GameObject gameObject, Registration registration) in _bodies)
        {
            if (registration.Configuration.Kind != RigidBodyKind.Dynamic || !registration.HasBody)
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
        foreach (Registration registration in _bodies.Values)
            if (registration.RigidBody is not null)
                registration.RigidBody.HasBody = false;
        _bodies.Clear();
    }

    public void Shutdown()
    {
        _world?.Dispose();
        _world = null;
        ClearRegistrations();
        _attachedScene = null;
        _log?.Information("Physics shut down");
    }
}
