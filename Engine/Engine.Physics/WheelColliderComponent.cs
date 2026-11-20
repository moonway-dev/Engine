using BepuPhysics;
using Engine.Mathematics;
using Engine.Scene;

namespace Engine.Physics;

[InspectorTooltip("Unity-style raycast suspension wheel. Place on a child object of the vehicle, one per wheel; drives the wheel mesh rotation and travel.")]
public sealed class WheelColliderComponent : Component
{
    [InspectorTooltip("Suspension attachment point relative to the vehicle body.")]
    public Vector3 LocalAnchor { get; set; }
    [InspectorRange(0.05f, 3f)]
    public float Radius { get; set; } = 0.6f;
    [InspectorRange(0.05f, 2f)]
    [InspectorTooltip("Maximum suspension travel in meters.")]
    public float SuspensionDistance { get; set; } = 0.3f;
    [InspectorRange(0.5f, 20f)]
    [InspectorTooltip("Suspension spring frequency in hertz.")]
    public float Frequency { get; set; } = 1.8f;
    [InspectorRange(0f, 1f)]
    public float DampingRatio { get; set; } = 0.85f;
    [InspectorRange(0f, 2f)]
    [InspectorTooltip("How strongly the tire resists sideways sliding.")]
    public float LateralGrip { get; set; } = 1f;
    [InspectorRange(0f, 2f)]
    [InspectorTooltip("How strongly the tire converts torque into forward motion.")]
    public float ForwardGrip { get; set; } = 1f;
    [InspectorRange(500f, 200000f)]
    [InspectorTooltip("Maximum suspension spring force per wheel, in newtons.")]
    public float MaxSuspensionForce { get; set; } = 12000f;
    [InspectorRange(1f, 40f)]
    [InspectorTooltip("Maximum sideways acceleration the tires can generate, in m/s². Prevents rolling over in sharp turns.")]
    public float MaxLateralAcceleration { get; set; } = 12f;

    [InspectorTooltip("Motor torque around the wheel axle; set from scripts.")]
    public float MotorTorque { get; set; }
    [InspectorTooltip("Steering angle around the suspension axis in degrees; set from scripts.")]
    public float SteerAngle { get; set; }
    [InspectorTooltip("Brake torque; set from scripts.")]
    public float BrakeTorque { get; set; }

    [SceneIgnore]
    [InspectorHidden]
    public bool IsGrounded { get; internal set; }

    [SceneIgnore]
    [InspectorHidden]
    public Vector3 ContactPoint { get; internal set; }

    [SceneIgnore]
    [InspectorHidden]
    public float Compression { get; internal set; }

    private float _rollAngle;
    internal float LastTravel { get; set; }
    internal float LastSurfaceDistance { get; set; }
    internal float LastRoll { get; set; }
    internal bool HasVisual { get; set; }

    internal void Apply(
        PhysicsWorld world,
        BodyHandle chassisBody,
        Transform chassisTransform,
        Transform wheelTransform,
        float sprungMass,
        float deltaTime)
    {
        IsGrounded = false;
        Compression = 0f;
        Quaternion chassisRotation = chassisTransform.Rotation;
        Vector3 chassisPosition = chassisTransform.Position;
        Vector3 up = chassisRotation.Rotate(Vector3.Up);
        Vector3 forward = chassisRotation.Rotate(Vector3.Forward);
        Vector3 anchor = chassisPosition + chassisRotation.Rotate(LocalAnchor);
        Vector3 down = -up;
        float maximumDistance = SuspensionDistance + Radius;
        Quaternion steerRotation = Quaternion.FromAxisAngle(up, -SteerAngle * MathF.PI / 180f);
        Vector3 steeredForward = steerRotation.Rotate(forward);

        Vector3 velocityAtAnchor = world.GetBodyVelocityAtPoint(chassisBody, anchor);
        float compressionVelocity = Vector3.Dot(velocityAtAnchor, down);

        if (!world.RayCast(anchor, down, maximumDistance, out PhysicsRayHit hit, chassisBody))
        {
            float airborneForce = MathUtil.Clamp(
                SuspensionDistance * sprungMass * 4f * MathF.PI * MathF.PI * Frequency * Frequency * 0.25f,
                0f,
                MathF.Max(sprungMass * MathF.Abs(world.Gravity.Y) * 0.5f, 1f));
        world.ApplyImpulseAtPoint(chassisBody, up * airborneForce * deltaTime, anchor);
        LastTravel = SuspensionDistance;
        LastSurfaceDistance = 0f;
        LastRoll = _rollAngle;
        HasVisual = true;
        UpdateVisual(wheelTransform, chassisRotation, anchor, down, LastTravel, up, steerRotation, LastRoll);
        return;
    }

    if (LastSurfaceDistance - hit.Distance > Radius)
    {
        LastTravel = SuspensionDistance;
        LastRoll = _rollAngle;
        HasVisual = true;
        UpdateVisual(wheelTransform, chassisRotation, anchor, down, LastTravel, up, steerRotation, LastRoll);
        return;
    }

    IsGrounded = true;
        Compression = MathUtil.Clamp(maximumDistance - hit.Distance, 0f, SuspensionDistance);
        ContactPoint = anchor + down * hit.Distance;
        float springStrength = sprungMass * 4f * MathF.PI * MathF.PI * Frequency * Frequency;
        float damping = 2f * DampingRatio * MathF.Sqrt(springStrength * sprungMass);
        float force = MathUtil.Clamp(
            Compression * springStrength + compressionVelocity * damping,
            0f,
            MaxSuspensionForce);
        world.ApplyImpulseAtPoint(chassisBody, up * force * deltaTime, anchor);

        Vector3 normal = hit.Normal;
        Vector3 planeForward = steeredForward - normal * Vector3.Dot(steeredForward, normal);
        planeForward = planeForward.LengthSquared > MathUtil.Epsilon * MathUtil.Epsilon ? planeForward.Normalized : steeredForward;
        Vector3 lateral = Vector3.Cross(normal, planeForward);
        lateral = lateral.LengthSquared > MathUtil.Epsilon * MathUtil.Epsilon ? lateral.Normalized : Vector3.Up;
        Vector3 velocityAtContact = world.GetBodyVelocityAtPoint(chassisBody, anchor);
        float forwardSpeed = Vector3.Dot(velocityAtContact, planeForward);
        float lateralSpeed = Vector3.Dot(velocityAtContact, lateral);

        float driveImpulse = MotorTorque / MathF.Max(Radius, MathUtil.Epsilon) * deltaTime * ForwardGrip;
        float brakeImpulse = MathF.Min(
            MathF.Min(BrakeTorque / MathF.Max(Radius, MathUtil.Epsilon) * deltaTime, sprungMass * MathF.Abs(world.Gravity.Y) * deltaTime),
            MathF.Abs(forwardSpeed) * sprungMass);
        float forwardImpulse = driveImpulse - MathF.Sign(forwardSpeed) * brakeImpulse;
        float maxLateralImpulse = sprungMass * MaxLateralAcceleration * deltaTime;
        float lateralImpulse = MathUtil.Clamp(
            -lateralSpeed * sprungMass * MathUtil.Clamp(LateralGrip, 0f, 1f) * 0.5f,
            -maxLateralImpulse,
            maxLateralImpulse);
        world.ApplyImpulseAtPoint(chassisBody, planeForward * forwardImpulse + lateral * lateralImpulse, anchor);

        _rollAngle -= forwardSpeed / MathF.Max(Radius, MathUtil.Epsilon) * deltaTime;
        LastTravel = hit.Distance - Radius;
        LastSurfaceDistance = hit.Distance;
        LastRoll = _rollAngle;
        HasVisual = true;
        UpdateVisual(wheelTransform, chassisRotation, anchor, down, LastTravel, up, steerRotation, LastRoll);
    }

    private void UpdateVisual(
        Transform wheelTransform,
        Quaternion chassisRotation,
        Vector3 anchor,
        Vector3 down,
        float travel,
        Vector3 up,
        Quaternion steerRotation,
        float rollAngle)
    {
        UpdateVisualOnly(wheelTransform, chassisRotation, anchor, down, travel, up, chassisRotation.Rotate(Vector3.Forward), SteerAngle * MathF.PI / 180f, rollAngle);
    }

    internal void UpdateVisualOnly(
        Transform wheelTransform,
        Quaternion chassisRotation,
        Vector3 anchor,
        Vector3 down,
        float travel,
        Vector3 up,
        Vector3 forward,
        float steerAngle,
        float rollAngle)
    {
        Quaternion steer = Quaternion.FromAxisAngle(up, -steerAngle);
        Quaternion tilt = Quaternion.FromAxisAngle(Vector3.Backward, -MathF.PI * 0.5f);
        Quaternion roll = Quaternion.FromAxisAngle(Vector3.Up, rollAngle);
        Vector3 position = anchor + down * travel;
        Quaternion rotation = steer * chassisRotation * tilt * roll;
        wheelTransform.Position = position;
        wheelTransform.Rotation = rotation;
        wheelTransform.SetRenderPose(position, rotation, overridesCurrent: true);
    }
}
