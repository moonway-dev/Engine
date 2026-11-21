using BepuPhysics;
using Engine.Mathematics;
using Engine.Scene;

namespace Engine.Physics;

internal enum SuspensionProbeState
{
    Airborne,
    Supported,
    Blocked,
}

[InspectorTooltip("Unity-style sphere-cast suspension wheel. Place on a child object of the vehicle, one per wheel; drives the wheel mesh rotation and travel.")]
public sealed class WheelColliderComponent : Component
{
    [InspectorTooltip("Suspension attachment point relative to the vehicle body.")]
    public Vector3 LocalAnchor { get; set; }
    [InspectorRange(0.05f, 3f)]
    public float Radius { get; set; } = 0.6f;
    [InspectorRange(0.05f, 1.5f)]
    [InspectorTooltip("Physical wheel width along the axle, in meters; the wheel collider is a cylinder of this width.")]
    public float Width { get; set; } = 0.3f;
    [InspectorLayer]
    [InspectorTooltip("Physics layer index from Project Settings; the wheel's collision cylinder uses it inside the vehicle body.")]
    public int PhysicsLayer { get; set; }
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
    [InspectorRange(0.2f, 3f)]
    [InspectorTooltip("Peak tire friction coefficient. Total tire force is limited to this times the current suspension load.")]
    public float FrictionCoefficient { get; set; } = 1.1f;
    [InspectorRange(0f, 50000f)]
    [InspectorTooltip("Anti-roll bar stiffness shared with the opposite wheel on the same axle, in newtons per meter of compression difference.")]
    public float AntiRollStiffness { get; set; }

    [SceneIgnore]
    [InspectorHidden]
    public float Load { get; internal set; }

    [SceneIgnore]
    [InspectorHidden]
    public float SlipFraction { get; internal set; }

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
    private float _smoothedCompression;
    private Vector3 _smoothedNormal = Vector3.Up;
    internal float LastTravel { get; set; }
    internal float LastRoll { get; set; }
    internal bool HasVisual { get; set; }

    private const float TireComplianceRate = 60f;
    private const float MaxDampingForce = 4500f;
    private const float NormalSmoothingRate = 45f;
    private const float MinSupportNormal = 0.35f;
    private const float ProbeMargin = 0.05f;

    internal static SuspensionProbeState ProbeSupport(
        PhysicsWorld world,
        BodyHandle? ignore,
        Vector3 anchor,
        Vector3 up,
        float suspensionDistance,
        float radius,
        out float travel,
        out Vector3 normal,
        out Vector3 contact)
    {
        travel = suspensionDistance;
        normal = up;
        contact = Vector3.Zero;
        Vector3 down = -up;
        float raise = radius * 0.5f;
        Vector3 start = anchor + up * raise;
        float maximumDistance = suspensionDistance + raise + radius;
        if (!world.SweepSphere(start, down, maximumDistance, radius, out PhysicsSweepHit sweep, ignore))
            return SuspensionProbeState.Airborne;
        if (sweep.StartedIntersecting)
            return ProbeFallback(world, ignore, start, down, maximumDistance, anchor, up, suspensionDistance, radius, out travel, out normal, out contact);
        if (Vector3.Dot(sweep.Normal, up) < MinSupportNormal)
            return SuspensionProbeState.Blocked;
        float rawTravel = Vector3.Dot(anchor - (sweep.Position + sweep.Normal * radius), up);
        if (rawTravel > suspensionDistance + ProbeMargin)
            return SuspensionProbeState.Airborne;
        travel = MathUtil.Clamp(rawTravel, 0f, suspensionDistance);
        normal = sweep.Normal;
        contact = sweep.Position;
        return SuspensionProbeState.Supported;
    }

    private static SuspensionProbeState ProbeFallback(
        PhysicsWorld world,
        BodyHandle? ignore,
        Vector3 start,
        Vector3 down,
        float maximumDistance,
        Vector3 anchor,
        Vector3 up,
        float suspensionDistance,
        float radius,
        out float travel,
        out Vector3 normal,
        out Vector3 contact)
    {
        travel = suspensionDistance;
        normal = up;
        contact = Vector3.Zero;
        if (TrySmallSweep(world, ignore, start, down, maximumDistance, anchor, up, suspensionDistance, radius, out travel, out normal, out contact))
            return SuspensionProbeState.Supported;
        Vector3 higherStart = anchor + up * (radius * 1.5f);
        if (TrySmallSweep(world, ignore, higherStart, down, maximumDistance + radius, anchor, up, suspensionDistance, radius, out travel, out normal, out contact))
            return SuspensionProbeState.Supported;
        return SuspensionProbeState.Blocked;
    }

    private static bool TrySmallSweep(
        PhysicsWorld world,
        BodyHandle? ignore,
        Vector3 start,
        Vector3 down,
        float maximumDistance,
        Vector3 anchor,
        Vector3 up,
        float suspensionDistance,
        float radius,
        out float travel,
        out Vector3 normal,
        out Vector3 contact)
    {
        travel = suspensionDistance;
        normal = up;
        contact = Vector3.Zero;
        if (!world.SweepSphere(start, down, maximumDistance, radius * 0.5f, out PhysicsSweepHit sweep, ignore))
            return false;
        if (sweep.StartedIntersecting || Vector3.Dot(sweep.Normal, up) < MinSupportNormal)
            return false;
        float rawTravel = Vector3.Dot(anchor - (sweep.Position + sweep.Normal * radius), up);
        if (rawTravel > suspensionDistance + ProbeMargin)
            return false;
        travel = MathUtil.Clamp(rawTravel, 0f, suspensionDistance);
        normal = sweep.Normal;
        contact = sweep.Position;
        return true;
    }

    public override void OnDrawGizmos()
    {
        Transform owner = Owner.Transform;
        Transform chassis = owner.Parent ?? owner;
        Vector3 chassisScale = new(
            MathF.Abs(chassis.Scale.X),
            MathF.Abs(chassis.Scale.Y),
            MathF.Abs(chassis.Scale.Z));
        Vector3 anchor = chassis.Position + chassis.Rotation.Rotate(LocalAnchor * chassisScale);
        Quaternion axle = chassis.Rotation * Quaternion.FromAxisAngle(new Vector3(0f, 0f, 1f), -MathF.PI * 0.5f);
        Vector3 axleDirection = axle.Rotate(Vector3.Up);
        Vector3 down = -chassis.Rotation.Rotate(Vector3.Up);
        Vector4 cylinderColor = new(1f, 0.6f, 0.2f, 1f);
        Vector4 travelColor = new(1f, 0.95f, 0.3f, 1f);
        DebugDraw.DrawWireCylinder(anchor, axle, Radius, MathF.Max(Width, 0.05f), cylinderColor);
        Vector3 travelEnd = anchor + down * SuspensionDistance;
        DebugDraw.DrawLine(anchor, travelEnd, travelColor);
        float markerHalf = MathF.Max(Width * 0.5f, 0.05f);
        DebugDraw.DrawLine(travelEnd - axleDirection * markerHalf, travelEnd + axleDirection * markerHalf, travelColor);
    }

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
        Load = 0f;
        SlipFraction = 0f;
        Quaternion chassisRotation = chassisTransform.Rotation;
        Vector3 chassisPosition = chassisTransform.Position;
        Vector3 up = chassisRotation.Rotate(Vector3.Up);
        Vector3 forward = chassisRotation.Rotate(Vector3.Forward);
        Vector3 anchor = chassisPosition + chassisRotation.Rotate(LocalAnchor);
        Vector3 down = -up;
        Quaternion steerRotation = Quaternion.FromAxisAngle(up, -SteerAngle * MathF.PI / 180f);
        Vector3 steeredForward = steerRotation.Rotate(forward);

        Vector3 velocityAtAnchor = world.GetBodyVelocityAtPoint(chassisBody, anchor);
        float compressionVelocity = Vector3.Dot(velocityAtAnchor, down);
        float springStrength = sprungMass * 4f * MathF.PI * MathF.PI * Frequency * Frequency;
        float damping = 2f * DampingRatio * MathF.Sqrt(springStrength * sprungMass);
        float dampingForce = MathUtil.Clamp(compressionVelocity * damping, -MaxDampingForce, MaxDampingForce);

        SuspensionProbeState state = ProbeSupport(world, chassisBody, anchor, up, SuspensionDistance, Radius, out float travel, out Vector3 surfaceNormal, out Vector3 contact);
        if (state == SuspensionProbeState.Airborne)
        {
            _smoothedCompression = MathUtil.Lerp(_smoothedCompression, 0f, MathUtil.Clamp(deltaTime * TireComplianceRate, 0f, 1f));
            float travelVisual = SuspensionDistance - _smoothedCompression;
            LastTravel = travelVisual;
            LastRoll = _rollAngle;
            HasVisual = true;
            UpdateVisual(wheelTransform, chassisRotation, anchor, down, travelVisual, up, steerRotation, LastRoll);
            return;
        }

        IsGrounded = true;
        if (state == SuspensionProbeState.Blocked)
        {
            Compression = SuspensionDistance - _smoothedCompression;
        }
        else
        {
            float compression = SuspensionDistance - travel;
            Compression = compression;
            ContactPoint = contact;
            Vector3 wheelCenter = anchor - up * travel;
            Vector3 contactToCenter = wheelCenter - contact;
            Vector3 rawNormal = contactToCenter.LengthSquared > MathUtil.Epsilon * MathUtil.Epsilon
                ? contactToCenter.Normalized
                : surfaceNormal;
            Vector3 normal = Vector3.Lerp(_smoothedNormal, rawNormal, MathUtil.Clamp(deltaTime * NormalSmoothingRate, 0f, 1f));
            normal = normal.LengthSquared > MathUtil.Epsilon * MathUtil.Epsilon ? normal.Normalized : rawNormal;
            _smoothedNormal = normal;
            _smoothedCompression = MathUtil.Lerp(_smoothedCompression, compression, MathUtil.Clamp(deltaTime * TireComplianceRate, 0f, 1f));
        }

        float force = MathUtil.Clamp(_smoothedCompression * springStrength + dampingForce, 0f, MaxSuspensionForce);
        world.ApplyImpulseAtPoint(chassisBody, up * force * deltaTime, anchor);

        Vector3 planeForward = steeredForward - _smoothedNormal * Vector3.Dot(steeredForward, _smoothedNormal);
        planeForward = planeForward.LengthSquared > MathUtil.Epsilon * MathUtil.Epsilon ? planeForward.Normalized : steeredForward;
        Vector3 lateral = Vector3.Cross(_smoothedNormal, planeForward);
        lateral = lateral.LengthSquared > MathUtil.Epsilon * MathUtil.Epsilon ? lateral.Normalized : Vector3.Up;
        Vector3 velocityAtContact = world.GetBodyVelocityAtPoint(chassisBody, anchor);
        float forwardSpeed = Vector3.Dot(velocityAtContact, planeForward);
        float lateralSpeed = Vector3.Dot(velocityAtContact, lateral);

        float load = MathF.Max(force, 0f);
        Load = load;
        float driveImpulse = MotorTorque / MathF.Max(Radius, MathUtil.Epsilon) * deltaTime * ForwardGrip;
        float brakeImpulse = MathF.Min(
            MathF.Min(BrakeTorque / MathF.Max(Radius, MathUtil.Epsilon) * deltaTime, sprungMass * MathF.Abs(world.Gravity.Y) * deltaTime),
            MathF.Abs(forwardSpeed) * sprungMass);
        float forwardImpulse = driveImpulse - MathF.Sign(forwardSpeed) * brakeImpulse;
        float lateralImpulse = -lateralSpeed * load * MathUtil.Clamp(LateralGrip, 0f, 2f) * deltaTime;
        float maxTireImpulse = FrictionCoefficient * load * deltaTime;
        float demandedImpulse = MathF.Sqrt(forwardImpulse * forwardImpulse + lateralImpulse * lateralImpulse);
        if (demandedImpulse > maxTireImpulse && demandedImpulse > MathUtil.Epsilon)
        {
            float tractionScale = maxTireImpulse / demandedImpulse;
            forwardImpulse *= tractionScale;
            lateralImpulse *= tractionScale;
            SlipFraction = 1f - tractionScale;
        }
        else
        {
            SlipFraction = 0f;
        }
        float maxLateralImpulse = sprungMass * MaxLateralAcceleration * deltaTime;
        lateralImpulse = MathUtil.Clamp(lateralImpulse, -maxLateralImpulse, maxLateralImpulse);
        world.ApplyImpulseAtPoint(chassisBody, planeForward * forwardImpulse + lateral * lateralImpulse, anchor);

        _rollAngle -= forwardSpeed / MathF.Max(Radius, MathUtil.Epsilon) * deltaTime;
        float groundedTravel = SuspensionDistance - _smoothedCompression;
        LastTravel = groundedTravel;
        LastRoll = _rollAngle;
        HasVisual = true;
        UpdateVisual(wheelTransform, chassisRotation, anchor, down, groundedTravel, up, steerRotation, LastRoll);
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
