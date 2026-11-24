using BepuPhysics;
using Engine.Mathematics;
using Engine.Scene;

namespace Engine.Physics;

[InspectorTooltip("Rigid body with per-body gravity, damping, collision detection and axis constraints. Disable the component to remove it from physics.")]
public sealed class Rigidbody : Component
{
    private bool _isAwake = true;

    [InspectorTooltip("Dynamic bodies respond to forces, Kinematic bodies are driven by code, Static bodies remain fixed.")]
    public RigidBodyKind Kind { get; set; } = RigidBodyKind.Dynamic;

    [InspectorTooltip("Mass in kilograms used by Dynamic bodies.")]
    [InspectorRange(0.001f, 1000f)]
    public float Mass { get; set; } = 1f;

    [InspectorTooltip("Linear velocity damping per second. Zero disables damping.")]
    [InspectorRange(0f, 100f)]
    public float LinearDamping { get; set; } = 0.05f;

    [InspectorTooltip("Angular velocity damping per second. Zero disables damping.")]
    [InspectorRange(0f, 100f)]
    public float AngularDamping { get; set; } = 0.05f;

    [InspectorTooltip("Apply world gravity to this Dynamic body.")]
    public bool UseGravity { get; set; } = true;

    [InspectorTooltip("Generate collisions and include this body in physics raycasts.")]
    public bool DetectCollisions { get; set; } = true;

    [SceneIgnore]
    [InspectorHidden]
    [InspectorTooltip("Drive the body from transforms or velocity rather than forces and gravity.")]
    public bool IsKinematic
    {
        get => Kind == RigidBodyKind.Kinematic;
        set => Kind = value ? RigidBodyKind.Kinematic : RigidBodyKind.Dynamic;
    }

    [InspectorTooltip("Smooth the render pose between fixed physics steps, or extrapolate from current velocity.")]
    public RigidbodyInterpolation Interpolation { get; set; } = RigidbodyInterpolation.Interpolate;

    [InspectorTooltip("Discrete uses speculative contacts, Passive expands bounds by velocity, Continuous uses sweep tests.")]
    public RigidbodyCollisionDetection CollisionDetection { get; set; } = RigidbodyCollisionDetection.Passive;

    [InspectorGroup("Constraints")]
    [InspectorTooltip("Prevent physics motion on selected position and rotation axes.")]
    public RigidbodyConstraints Constraints { get; set; }

    [SceneIgnore]
    [InspectorHidden]
    [InspectorTooltip("Sleeping bodies may be awakened by collisions or forces. This is not the component enabled state.")]
    public bool IsAwake
    {
        get => HasBody && Kind != RigidBodyKind.Static && World is not null ? World.IsBodyAwake(Body) : _isAwake;
        set
        {
            _isAwake = value;
            if (HasBody && Kind != RigidBodyKind.Static && World is not null)
                World.SetBodyAwake(Body, value);
        }
    }

    [SceneIgnore]
    public bool IsSleeping => HasBody && Kind != RigidBodyKind.Static && !IsAwake;

    [SceneIgnore]
    public Vector3 LinearVelocity
    {
        get => HasBody && Kind != RigidBodyKind.Static && World is not null ? World.GetBodyLinearVelocity(Body) : Vector3.Zero;
        set
        {
            if (IsActiveInHierarchy && HasBody && Kind != RigidBodyKind.Static && World is not null)
                World.SetBodyVelocity(Body, value);
        }
    }

    [SceneIgnore]
    public Vector3 AngularVelocity
    {
        get => HasBody && Kind != RigidBodyKind.Static && World is not null ? World.GetBodyAngularVelocity(Body) : Vector3.Zero;
        set
        {
            if (IsActiveInHierarchy && HasBody && Kind != RigidBodyKind.Static && World is not null)
                World.SetBodyVelocity(Body, LinearVelocity, value);
        }
    }

    [InspectorButton("Sleep")]
    public void Sleep() => IsAwake = false;
    [InspectorButton("Wake Up")]
    public void WakeUp() => IsAwake = true;

    internal BodyHandle Body { get; set; }
    internal StaticHandle Static { get; set; }
    internal bool HasBody { get; set; }
    internal PhysicsWorld? World { get; set; }
    internal bool InitialAwake => _isAwake;

    internal RigidbodySettings Settings => new(LinearDamping, AngularDamping, UseGravity, DetectCollisions, Constraints, CollisionDetection);
}
