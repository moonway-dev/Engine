using Engine.Mathematics;
using Engine.Scene;
using Engine.Scripting;

public sealed class ChaseCamera : ScriptBehaviour
{
    public EntityReference Target;
    public Vector3 Offset = new(0f, 3.2f, 7.5f);
    public Vector3 LookOffset = new(0f, 1.2f, 0f);
    [InspectorRange(20f, 200f)]
    public float SpeedFovKmh = 120f;
    [InspectorRange(0f, 25f)]
    public float SpeedFovKick = 8f;
    [InspectorRange(0.5f, 12f)]
    public float OrbitSpeed = 4f;
    [InspectorRange(0.3f, 8f)]
    public float OrbitReturnSpeed = 1.2f;
    [InspectorRange(0f, 3f)]
    public float OrbitIdleDelay = 1.5f;
    [InspectorRange(0.5f, 12f)]
    public float YawFollowSpeed = 6f;
    [InspectorRange(0f, 1f)]
    public float FollowTrail;
    [InspectorRange(0.1f, 1f)]
    public float CollisionRadius = 0.35f;
    [InspectorRange(0f, 1f)]
    public float CollisionMargin = 0.1f;
    [InspectorRange(1f, 20f)]
    public float CollisionRecovery = 5f;

    private Camera? _camera;
    private float _baseFov;
    private float _orbitYaw;
    private float _orbitPitch;
    private float _orbitIdleTime;
    private float _heading;
    private float _boomDistance;
    private Vector3 _offset;
    private Vector3 _previousTarget;
    private bool _initialized;

    public override void OnEnable()
    {
        _initialized = false;
        ScriptInput.SetCursorLocked(true);
    }

    public override void OnDisable()
    {
        ScriptInput.SetCursorLocked(false);
        if (_camera is not null)
            _camera.FieldOfViewY = _baseFov;
        _initialized = false;
    }

    public override void OnStart()
    {
        _camera = Owner.GetComponent<Camera>();
        _baseFov = _camera?.FieldOfViewY ?? MathUtil.DegToRad * 60f;
    }

    public override void OnUpdate()
    {
        if (Scene.FindById(Target.Id) is not { } target)
            return;
        float deltaTime = MathF.Max(ScriptTime.DeltaTime, 0f);
        (Vector3 position, Quaternion rotation) = ScriptPhysics.GetInterpolatedPose(target);
        Vector3 backward = rotation.Rotate(Vector3.Backward);
        float heading = MathF.Atan2(backward.X, backward.Z);
        bool reset = !_initialized || Vector3.Distance(position, _previousTarget) > 20f;
        if (reset)
        {
            _heading = heading;
            _boomDistance = 0f;
        }
        else
        {
            _heading += AngleDelta(_heading, heading) * Blend(MathF.Max(YawFollowSpeed, 0.1f) * 4f, deltaTime);
        }
        UpdateOrbit(deltaTime);
        float distance = MathF.Max(Offset.Length, 0.1f);
        float yaw = _heading + MathF.Atan2(Offset.X, Offset.Z) + _orbitYaw;
        float pitch = MathUtil.Clamp(MathF.Asin(MathUtil.Clamp(Offset.Y / distance, -1f, 1f)) + _orbitPitch, -0.2f, 1.35f);
        Vector3 desiredOffset = new(
            MathF.Sin(yaw) * MathF.Cos(pitch) * distance,
            MathF.Sin(pitch) * distance,
            MathF.Cos(yaw) * MathF.Cos(pitch) * distance);
        if (reset || FollowTrail <= MathUtil.Epsilon)
            _offset = desiredOffset;
        else
            _offset = Vector3.Lerp(_offset, desiredOffset, Blend(12f / MathF.Max(FollowTrail, 0.05f), deltaTime));
        Vector3 anchor = position + LookOffset;
        Vector3 cameraVector = position + _offset - anchor;
        float desiredDistance = cameraVector.Length;
        if (desiredDistance <= MathUtil.Epsilon)
            return;
        Vector3 direction = cameraVector / desiredDistance;
        float allowedDistance = desiredDistance;
        if (ScriptPhysics.SphereCast(anchor, direction, desiredDistance, MathF.Max(CollisionRadius, 0.01f), target) is { } hit)
            allowedDistance = MathUtil.Clamp(hit.Distance - MathF.Max(CollisionMargin, 0f), 0.05f, desiredDistance);
        if (reset || allowedDistance < _boomDistance)
            _boomDistance = allowedDistance;
        else
            _boomDistance += (allowedDistance - _boomDistance) * Blend(CollisionRecovery, deltaTime);
        _boomDistance = MathF.Min(_boomDistance, allowedDistance);
        Transform.Position = anchor + direction * _boomDistance;
        Transform.LookAt(anchor, Vector3.Up);
        if (_camera is not null)
        {
            float speed = ScriptPhysics.GetLinearVelocity(target).Length * 3.6f;
            float kick = MathUtil.Clamp(speed / MathF.Max(SpeedFovKmh, 1f), 0f, 1f);
            float fov = MathUtil.Clamp(_baseFov + kick * SpeedFovKick * MathUtil.DegToRad, 0.3f, 2.4f);
            _camera.FieldOfViewY = reset ? fov : MathUtil.Lerp(_camera.FieldOfViewY, fov, Blend(6f, deltaTime));
        }
        _previousTarget = position;
        _initialized = true;
    }

    private void UpdateOrbit(float deltaTime)
    {
        Vector2 mouse = ScriptInput.MouseDelta;
        if (mouse.LengthSquared > 0.01f)
        {
            _orbitIdleTime = 0f;
            _orbitYaw -= mouse.X * 0.003f * OrbitSpeed;
            _orbitPitch = MathUtil.Clamp(_orbitPitch + mouse.Y * 0.003f * OrbitSpeed, -1f, 1.1f);
        }
        else
        {
            float previousIdle = _orbitIdleTime;
            _orbitIdleTime += deltaTime;
            float returnTime = MathF.Max(_orbitIdleTime - OrbitIdleDelay, 0f) - MathF.Max(previousIdle - OrbitIdleDelay, 0f);
            float decay = MathF.Exp(-MathF.Max(OrbitReturnSpeed, 0f) * returnTime);
            _orbitYaw *= decay;
            _orbitPitch *= decay;
        }
        _orbitYaw = AngleDelta(0f, _orbitYaw);
    }

    private static float Blend(float rate, float deltaTime) => 1f - MathF.Exp(-MathF.Max(rate, 0f) * deltaTime);

    private static float AngleDelta(float from, float to)
    {
        float delta = (to - from) % (MathF.PI * 2f);
        if (delta > MathF.PI)
            delta -= MathF.PI * 2f;
        if (delta < -MathF.PI)
            delta += MathF.PI * 2f;
        return delta;
    }
}
