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
    public Vector3 LookOffset = new(0f, 1.2f, 0f);
    [InspectorRange(20f, 200f)]
    [InspectorTooltip("Speed in km/h at which the FOV kick reaches its maximum.")]
    public float SpeedFovKmh = 120f;
    [InspectorRange(0f, 25f)]
    [InspectorTooltip("Extra field of view added with speed, in degrees.")]
    public float SpeedFovKick = 8f;
    [InspectorRange(0.5f, 12f)]
    [InspectorTooltip("Rotation speed of the free camera orbit, in radians per second.")]
    public float OrbitSpeed = 4f;
    [InspectorRange(0.3f, 8f)]
    [InspectorTooltip("How fast the orbit returns behind the car after the mouse goes idle.")]
    public float OrbitReturnSpeed = 1.2f;
    [InspectorRange(0f, 3f)]
    [InspectorTooltip("Seconds of mouse inactivity before the orbit starts returning behind the car.")]
    public float OrbitIdleDelay = 1.5f;
    [InspectorRange(0.5f, 8f)]
    [InspectorTooltip("How fast the camera yaw catches up with the car's heading; lower values make the camera trail the car in turns.")]
    public float YawFollowSpeed = 2.5f;
    [InspectorRange(0f, 1f)]
    [InspectorTooltip("Fraction of the camera position that trails the car per second; 0 keeps the camera rigidly bolted to the car.")]
    public float FollowTrail = 0f;
    [InspectorRange(0.1f, 1f)]
    [InspectorTooltip("Radius of the clearance sphere kept between the camera and geometry.")]
    public float CollisionRadius = 0.35f;
    [InspectorRange(0f, 1f)]
    [InspectorTooltip("Extra distance kept between the camera and whatever it collides with.")]
    public float CollisionMargin = 0.25f;
    [InspectorRange(1f, 20f)]
    [InspectorTooltip("How fast the camera pulls back out after a collision, per second.")]
    public float CollisionRecovery = 5f;

    private float _baseFov;
    private CameraComponent? _camera;
    private float _orbitYaw;
    private float _orbitPitch;
    private float _orbitIdleTime;
    private float _smoothedYaw;
    private bool _hasSmoothedYaw;
    private Vector3 _trailingPosition;
    private bool _hasTrailingPosition;
    private float _collisionDistance;
    private bool _hasCollisionDistance;

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
            _smoothedYaw = target.Transform.Rotation.ToYawPitchRoll().X;
            _hasSmoothedYaw = true;
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
        if (!_hasSmoothedYaw)
        {
            _smoothedYaw = yaw;
            _hasSmoothedYaw = true;
        }
        bool mouseActive = ScriptInput.MouseDelta.Length > 0.5f;
        float yawFollowSpeed = mouseActive ? YawFollowSpeed * 4f : YawFollowSpeed;
        float yawBlend = 1f - MathF.Exp(-yawFollowSpeed * ScriptTime.DeltaTime);
        _smoothedYaw += AngleDelta(_smoothedYaw, yaw) * yawBlend;
        float distance = Offset.Length;
        float basePitch = MathF.Asin(MathUtil.Clamp(Offset.Y / distance, -1f, 1f));
        float pitch = MathUtil.Clamp(basePitch + _orbitPitch, -0.3f, 1.35f);
        Vector3 offset = new(
            MathF.Sin(_smoothedYaw) * MathF.Cos(pitch) * distance,
            MathF.Sin(pitch) * distance,
            MathF.Cos(_smoothedYaw) * MathF.Cos(pitch) * distance);
        Vector3 desiredPosition = targetPosition + offset;
        if (FollowTrail > MathUtil.Epsilon)
        {
            if (!_hasTrailingPosition)
            {
                _trailingPosition = desiredPosition;
                _hasTrailingPosition = true;
            }
            float blend = 1f - MathF.Exp(-FollowTrail * 12f * ScriptTime.DeltaTime);
            _trailingPosition = Vector3.Lerp(_trailingPosition, desiredPosition, blend);
            float trailGap = Vector3.Distance(_trailingPosition, desiredPosition);
            desiredPosition = trailGap > 0.01f
                ? _trailingPosition + (desiredPosition - _trailingPosition).Normalized * MathF.Min(trailGap, 1.5f)
                : desiredPosition;
        }
        Transform.Position = ResolveCollision(target, targetPosition, desiredPosition);

        Vector3 lookPoint = targetPosition + LookOffset;
        Vector3 toLook = lookPoint - Transform.Position;
        Transform.Rotation = toTargetRotation(toLook);

        if (_camera is not null && SpeedFovKick > MathUtil.Epsilon)
        {
            float speedKmh = velocity.Length * 3.6f;
            float kick = MathUtil.Clamp(speedKmh / MathF.Max(SpeedFovKmh, 1f), 0f, 1f);
            _camera.FieldOfViewY = MathUtil.Clamp(_baseFov + kick * SpeedFovKick * MathUtil.DegToRad, 0.3f, 2.4f);
        }
    }

    private Vector3 ResolveCollision(GameObject target, Vector3 targetPosition, Vector3 desiredPosition)
    {
        Vector3 anchor = targetPosition + LookOffset;
        Vector3 toCamera = desiredPosition - anchor;
        float distance = toCamera.Length;
        if (distance < MathUtil.Epsilon)
            return desiredPosition;
        Vector3 direction = toCamera / distance;
        float minimum = CollisionRadius + CollisionMargin;
        float targetDistance = distance;
        if (distance > minimum && ScriptPhysics.RayCast(anchor, direction, distance, target) is { } hit)
            targetDistance = MathUtil.Clamp(hit.Distance - CollisionRadius - CollisionMargin, minimum, distance);
        if (!_hasCollisionDistance || targetDistance < _collisionDistance)
        {
            _collisionDistance = targetDistance;
            _hasCollisionDistance = true;
        }
        else
        {
            float blend = 1f - MathF.Exp(-CollisionRecovery * ScriptTime.DeltaTime);
            _collisionDistance += (targetDistance - _collisionDistance) * blend;
        }
        return anchor + direction * _collisionDistance;
    }

    private void UpdateOrbit()
    {
        Vector2 delta = ScriptInput.MouseDelta;
        if (delta.Length > 0.5f)
        {
            _orbitIdleTime = 0f;
            _orbitYaw -= delta.X * 0.003f * OrbitSpeed;
            _orbitPitch = MathUtil.Clamp(_orbitPitch + delta.Y * 0.003f * OrbitSpeed, -1.0f, 1.1f);
            return;
        }
        _orbitIdleTime += ScriptTime.DeltaTime;
        float idle = MathF.Max(_orbitIdleTime - OrbitIdleDelay, 0f);
        if (idle <= 0f || (_orbitYaw == 0f && _orbitPitch == 0f))
            return;
        float decay = MathF.Exp(-OrbitReturnSpeed * idle);
        _orbitYaw *= decay;
        _orbitPitch *= decay;
        if (MathF.Abs(_orbitYaw) < 0.002f)
            _orbitYaw = 0f;
        if (MathF.Abs(_orbitPitch) < 0.002f)
            _orbitPitch = 0f;
    }

    private static float AngleDelta(float from, float to)
    {
        float delta = (to - from) % (MathF.PI * 2f);
        if (delta > MathF.PI)
            delta -= MathF.PI * 2f;
        if (delta < -MathF.PI)
            delta += MathF.PI * 2f;
        return delta;
    }

    private static Quaternion toTargetRotation(Vector3 toTarget)
    {
        if (toTarget.LengthSquared < MathUtil.Epsilon * MathUtil.Epsilon)
            return Quaternion.Identity;
        return Quaternion.LookRotation(toTarget.Normalized, Vector3.Up);
    }
}
