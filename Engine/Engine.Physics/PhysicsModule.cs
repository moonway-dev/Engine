using Engine.Core;
using Engine.Mathematics;
using Engine.Scene;

namespace Engine.Physics;

public sealed class PhysicsModule : IUpdatableModule, IFixedUpdateModule
{
    public const string LogCategory = "Physics";

    private readonly Dictionary<RigidBodyComponent, Registration> _bodies = [];
    private PhysicsWorld? _world;
    private ILogger? _log;
    private Scene.Scene? _attachedScene;

    private readonly record struct Configuration(RigidBodyKind Kind, ColliderShape Shape, Vector3 Size, Vector3 Scale, float Mass, Vector3 Center);

    private sealed class Registration(Configuration configuration)
    {
        public Configuration Configuration = configuration;
        public Vector3 Position;
        public Quaternion Rotation;
        public bool IsAwake;
    }

    public string Name => "Physics";

    public Vector3 Gravity { get; set; } = new(0f, -9.81f, 0f);

    public PhysicsWorld World => _world ?? throw new InvalidOperationException("Physics module is not initialized.");

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
        _world = new PhysicsWorld(Gravity);
        _log.Information($"Physics initialized (gravity {Gravity.Y} m/s^2)");
    }

    public void Update(in FrameTime frameTime)
    {
        if (_world is not null && _attachedScene is not null)
            SyncBodies();
    }

    public void FixedUpdate(in FrameTime frameTime)
    {
        if (_world is null || _attachedScene is null)
            return;
        SyncBodies();
        _world.Step(frameTime.FixedDeltaTime);
        ReadBackTransforms();
    }

    private void SyncBodies()
    {
        List<RigidBodyComponent> active = [];
        foreach (GameObject gameObject in _attachedScene!.EnumerateHierarchy())
        {
            if (!gameObject.IsActiveInHierarchy)
                continue;
            foreach (RigidBodyComponent body in gameObject.Components.OfType<RigidBodyComponent>())
                if (body.IsActiveInHierarchy)
                    active.Add(body);
        }

        HashSet<RigidBodyComponent> activeSet = [.. active];
        foreach (RigidBodyComponent body in _bodies.Keys.Where(body => !activeSet.Contains(body)).ToArray())
        {
            RemoveWorldBody(body, _bodies[body]);
            _bodies.Remove(body);
        }

        foreach (RigidBodyComponent body in active)
        {
            Configuration configuration = new(body.Kind, body.Shape, body.Size, body.Transform.Scale, body.Mass, body.Center);
            if (!_bodies.TryGetValue(body, out Registration? registration))
            {
                registration = new Registration(configuration);
                _bodies.Add(body, registration);
                CreateWorldBody(body, registration);
            }
            else if (registration.Configuration.Kind != configuration.Kind
                || registration.Configuration.Shape != configuration.Shape
                || registration.Configuration.Size != configuration.Size
                || (registration.Configuration.Scale - configuration.Scale).LengthSquared > 0.000001f
                || registration.Configuration.Mass != configuration.Mass
                || registration.Configuration.Center != configuration.Center)
            {
                RemoveWorldBody(body, registration);
                registration.Configuration = configuration;
                CreateWorldBody(body, registration);
            }
            else
            {
                Vector3 position = body.Transform.Position;
                Quaternion rotation = body.Transform.Rotation;
                if (position != registration.Position || rotation != registration.Rotation)
                {
                    Vector3 center = Vector3.TransformPoint(body.Center, body.Transform.WorldMatrix);
                    if (configuration.Kind == RigidBodyKind.Static)
                        _world!.SetStaticPose(body.Static, center, rotation);
                    else
                        _world!.SetBodyPose(body.Body, center, rotation);
                }
                if (configuration.Kind != RigidBodyKind.Static && body.IsAwake != registration.IsAwake)
                    _world!.SetBodyAwake(body.Body, body.IsAwake);
                CaptureTransform(body, registration);
            }
        }
    }

    private void CreateWorldBody(RigidBodyComponent body, Registration registration)
    {
        Configuration configuration = registration.Configuration;
        Vector3 scale = configuration.Scale;
        scale = new Vector3(MathF.Abs(scale.X), MathF.Abs(scale.Y), MathF.Abs(scale.Z));
        Vector3 size = configuration.Size * scale;
        if (configuration.Shape == ColliderShape.Sphere)
            size = new Vector3(configuration.Size.X * MathF.Max(scale.X, MathF.Max(scale.Y, scale.Z)));
        Vector3 center = Vector3.TransformPoint(body.Center, body.Transform.WorldMatrix);
        Quaternion rotation = body.Transform.Rotation;
        switch (configuration.Kind)
        {
            case RigidBodyKind.Static:
                body.Static = _world!.AddStatic(center, rotation, configuration.Shape, size);
                break;
            case RigidBodyKind.Kinematic:
                body.Body = _world!.AddKinematic(center, rotation, configuration.Shape, size);
                break;
            default:
                body.Body = _world!.AddDynamic(center, rotation, configuration.Shape, size, configuration.Mass);
                break;
        }
        body.HasBody = true;
        if (configuration.Kind != RigidBodyKind.Static)
            _world!.SetBodyAwake(body.Body, body.IsAwake);
        CaptureTransform(body, registration);
    }

    private static void CaptureTransform(RigidBodyComponent body, Registration registration)
    {
        registration.Position = body.Transform.Position;
        registration.Rotation = body.Transform.Rotation;
        registration.IsAwake = body.IsAwake;
    }

    private void RemoveWorldBody(RigidBodyComponent body, Registration registration)
    {
        if (!body.HasBody || _world is null)
            return;
        if (registration.Configuration.Kind == RigidBodyKind.Static)
            _world.RemoveStatic(body.Static);
        else
            _world.RemoveBody(body.Body);
        body.HasBody = false;
    }

    private void ReadBackTransforms()
    {
        foreach (GameObject gameObject in _attachedScene!.EnumerateHierarchy())
        {
            foreach (RigidBodyComponent body in gameObject.Components.OfType<RigidBodyComponent>())
            {
                if (!_bodies.TryGetValue(body, out Registration? registration) || registration.Configuration.Kind != RigidBodyKind.Dynamic)
                    continue;
                body.Transform.Rotation = _world!.GetBodyRotation(body.Body);
                Vector3 offset = Vector3.TransformPoint(body.Center, body.Transform.WorldMatrix) - body.Transform.Position;
                body.Transform.Position = _world.GetBodyPosition(body.Body) - offset;
            }
        }
        foreach ((RigidBodyComponent body, Registration registration) in _bodies)
            CaptureTransform(body, registration);
    }

    private void ClearRegistrations()
    {
        foreach (RigidBodyComponent body in _bodies.Keys)
            body.HasBody = false;
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
