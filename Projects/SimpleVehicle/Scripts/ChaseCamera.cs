using Engine.Mathematics;
using Engine.Platform;
using Engine.Scene;
using Engine.Scripting;

public sealed class ChaseCamera : ScriptBehaviour
{
    [InspectorTooltip("Vehicle the camera follows; drag from the Hierarchy.")]
    public EntityReference Target;
    [InspectorTooltip("Camera offset behind the car, in the car's local space.")]
    public Vector3 Offset = new(0f, 3.2f, 7.5f);
    [InspectorRange(0f, 1f)]
    [InspectorTooltip("How much of the car's velocity is added to the look target as look-ahead.")]
    public float LookAhead = 0.35f;
    public Vector3 LookOffset = new(0f, 1.2f, 0f);
    [InspectorRange(0f, 25f)]
    [InspectorTooltip("Extra field of view added with speed, in degrees.")]
    public float SpeedFovKick = 8f;
    [InspectorRange(20f, 200f)]
    [InspectorTooltip("Speed in km/h at which the FOV kick reaches its maximum.")]
    public float SpeedFovKmh = 120f;

    [InspectorRange(0.5f, 12f)]
    [InspectorTooltip("Rotation speed of the free camera orbit, in radians per second.")]
    public float OrbitSpeed = 4f;
    [InspectorRange(0.5f, 8f)]
    [InspectorTooltip("How fast the camera returns behind the car after orbiting.")]
    public float OrbitReturnSpeed = 2.5f;

    private float _baseFov;
    private CameraComponent? _camera;
    private float _orbitYaw;
    private float _orbitPitch;

    public override void OnEnable()
    {
        ScriptInput.SetCursorLocked(true);
    }

    public override void OnDisable()
    {
        ScriptInput.SetCursorLocked(false);
    }

    public override void OnStart()
    {
        _camera = Owner.GetComponent<CameraComponent>();
        if (_camera is not null)
            _baseFov = _camera.FieldOfViewY;
        if (Owner.Scene.FindById(Target.Id) is { } target)
        {
            Transform.Position = target.Transform.Position + target.Transform.Rotation.Rotate(Offset);
            Transform.Rotation = toTargetRotation((target.Transform.Position + LookOffset - Transform.Position).Normalized);
        }
    }

    public override void OnUpdate()
    {
        if (Owner.Scene.FindById(Target.Id) is not { } target)
            return;
        (Vector3 targetPosition, Quaternion targetRotation) = ScriptPhysics.GetInterpolatedPose(target);
        Vector3 velocity = ScriptPhysics.HasBody(target) ? ScriptPhysics.GetLinearVelocity(target) : Vector3.Zero;

        UpdateOrbit();
        float yaw = targetRotation.ToYawPitchRoll().X + _orbitYaw;
        float distance = Offset.Length;
        float basePitch = MathF.Asin(MathUtil.Clamp(Offset.Y / distance, -1f, 1f));
        float pitch = MathUtil.Clamp(basePitch + _orbitPitch, -0.3f, 1.35f);
        Vector3 offset = new(
            MathF.Sin(yaw) * MathF.Cos(pitch) * distance,
            MathF.Sin(pitch) * distance,
            MathF.Cos(yaw) * MathF.Cos(pitch) * distance);
        Vector3 desiredPosition = targetPosition + offset;
        Transform.Position = desiredPosition;

        Vector3 lookPoint = targetPosition + LookOffset + velocity * LookAhead * 0.1f;
        Vector3 toLook = lookPoint - Transform.Position;
        Transform.Rotation = toTargetRotation(toLook);

        if (_camera is not null && SpeedFovKick > MathUtil.Epsilon)
        {
            float speedKmh = velocity.Length * 3.6f;
            float kick = MathUtil.Clamp(speedKmh / MathF.Max(SpeedFovKmh, 1f), 0f, 1f);
            _camera.FieldOfViewY = MathUtil.Clamp(_baseFov + kick * SpeedFovKick * MathUtil.DegToRad, 0.3f, 2.4f);
        }
    }

    private void UpdateOrbit()
    {
        Vector2 delta = ScriptInput.MouseDelta;
        _orbitYaw -= delta.X * 0.003f * OrbitSpeed;
        _orbitPitch = MathUtil.Clamp(_orbitPitch + delta.Y * 0.003f * OrbitSpeed, -1.0f, 1.1f);
    }

    private bool Orbiting() => MathF.Abs(_orbitYaw) > 0.02f || MathF.Abs(_orbitPitch) > 0.02f;

    private static Quaternion toTargetRotation(Vector3 toTarget)
    {
        if (toTarget.LengthSquared < MathUtil.Epsilon * MathUtil.Epsilon)
            return Quaternion.Identity;
        return Quaternion.LookRotation(toTarget.Normalized, Vector3.Up);
    }
}
