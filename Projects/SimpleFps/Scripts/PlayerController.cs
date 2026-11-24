using Engine.Mathematics;
using Engine.Platform;
using Engine.Scene;
using Engine.Scripting;

public sealed class PlayerController : ScriptBehaviour
{
    [InspectorRange(1f, 15f)]
    [InspectorTooltip("Horizontal movement speed in m/s.")]
    public float MoveSpeed = 6f;

    [InspectorRange(0f, 1f)]
    [InspectorTooltip("How much steering is allowed while airborne; 1 equals full ground control.")]
    public float AirControl = 0.3f;

    [InspectorRange(0f, 500f)]
    [InspectorTooltip("Upward impulse applied on jump, in N*s.")]
    public float JumpImpulse = 16f;

    [InspectorRange(0.5f, 3f)]
    [InspectorTooltip("Length of the downward ground probe ray, in meters.")]
    public float GroundCheckDistance = 0.95f;

    [InspectorRange(0.0005f, 0.01f)]
    [InspectorTooltip("Radians of rotation per pixel of mouse movement.")]
    public float MouseSensitivity = 0.0022f;

    [InspectorRange(5f, 89f)]
    [InspectorTooltip("How far the camera can look up or down, in degrees.")]
    public float MaxPitchDegrees = 85f;

    [InspectorTooltip("Hide and lock the cursor while the script is active.")]
    public bool LockCursor = true;

    private Engine.Scene.Camera? _camera;
    private float _yaw;
    private float _pitch;

    public override void OnEnable()
    {
        if (LockCursor)
            ScriptInput.SetCursorLocked(true);
    }

    public override void OnDisable()
    {
        if (LockCursor)
            ScriptInput.SetCursorLocked(false);
    }

    public override void OnStart()
    {
        _camera = Owner.GetComponentsInChildren<Engine.Scene.Camera>().FirstOrDefault();
        Vector3 bodyAngles = Transform.Rotation.ToYawPitchRoll();
        _yaw = bodyAngles.X;
        if (_camera is not null)
            _pitch = _camera.Transform.LocalRotation.ToYawPitchRoll().Y;
    }

    public override void OnUpdate()
    {
        ApplyLook();
        ApplyMovement();
    }

    private void ApplyLook()
    {
        Vector2 delta = ScriptInput.MouseDelta;
        _yaw -= delta.X * MouseSensitivity;
        float maxPitch = MaxPitchDegrees * MathUtil.DegToRad;
        _pitch = MathUtil.Clamp(_pitch - delta.Y * MouseSensitivity, -maxPitch, maxPitch);
        Transform.Rotation = Quaternion.FromAxisAngle(Vector3.Up, _yaw);
        if (_camera is not null)
            _camera.Transform.LocalRotation = Quaternion.FromYawPitchRoll(0f, _pitch, 0f);
    }

    private void ApplyMovement()
    {
        Transform viewTransform = _camera?.Transform ?? Transform;
        Vector3 forward = viewTransform.Forward;
        forward = new Vector3(forward.X, 0f, forward.Z);
        if (forward.Length < MathUtil.Epsilon)
            forward = Quaternion.FromAxisAngle(Vector3.Up, _yaw).Rotate(Vector3.Forward);
        forward = forward.Normalized;
        Vector3 right = Vector3.Cross(forward, Vector3.Up).Normalized;
        float steer = ScriptInput.GetAxis("Horizontal");
        float throttle = ScriptInput.GetAxis("Vertical");
        Vector3 wish = forward * throttle + right * steer;
        if (wish.Length > MathUtil.Epsilon)
            wish = wish.Normalized * MoveSpeed;

        bool grounded = IsGrounded();
        if (grounded && ScriptInput.IsKeyPressed(KeyCode.Space))
        {
            ScriptPhysics.AddImpulse(Owner, Vector3.Up * JumpImpulse);
            ScriptPhysics.Wake(Owner);
        }

        Vector3 velocity = ScriptPhysics.GetLinearVelocity(Owner);
        Vector3 horizontal = new(velocity.X, 0f, velocity.Z);
        float blend = grounded ? 1f : MathUtil.Clamp(AirControl, 0f, 1f);
        Vector3 target = new(wish.X, 0f, wish.Z);
        Vector3 blended = horizontal + (target - horizontal) * blend;
        ScriptPhysics.SetLinearVelocity(Owner, new Vector3(blended.X, velocity.Y, blended.Z));

    }

    private bool IsGrounded()
    {
        ScriptRayHit? hit = ScriptPhysics.RayCast(Transform.Position, Vector3.Down, GroundCheckDistance, Owner);
        return hit is not null;
    }
}
