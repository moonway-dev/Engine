using Engine.Mathematics;
using Engine.Physics;
using Engine.Scene;
using Engine.Scripting;

public sealed class CarController : ScriptBehaviour
{
    [InspectorTooltip("Torque applied to driven wheels.")]
    public float MotorTorque = 1400f;
    [InspectorTooltip("Reverse motor torque.")]
    public float ReverseTorque = 900f;
    [InspectorTooltip("Brake torque applied while the handbrake is held.")]
    public float BrakeTorque = 3000f;
    [InspectorRange(5f, 45f)]
    [InspectorTooltip("Maximum steering angle of the front wheels, in degrees.")]
    public float MaxSteerDegrees = 36f;
    [InspectorRange(1f, 12f)]
    [InspectorTooltip("How fast the steering wheel moves toward the target angle.")]
    public float SteeringRate = 5f;
    [InspectorRange(20f, 260f)]
    [InspectorTooltip("Speed at which the motor cuts out, in km/h.")]
    public float MaxSpeedKmh = 140f;
    public bool AllWheelDrive;
    [InspectorTooltip("Key used as a handbrake.")]
    public KeyCode HandbrakeKey = KeyCode.Space;

    private WheelColliderComponent[] _wheels = [];
    private float _steer;

    public override void OnStart()
    {
        _wheels = [.. Owner.GetComponentsInChildren<WheelColliderComponent>()];
    }

    public override void OnUpdate()
    {
        if (_wheels.Length == 0)
            return;
        float throttle = ScriptInput.GetAxis("Vertical");
        float steerInput = ScriptInput.GetAxis("Horizontal");
        bool handbrake = ScriptInput.IsKeyDown(HandbrakeKey);

        float speedKmh = MathF.Abs(Vector3.Dot(ScriptPhysics.GetLinearVelocity(Owner), Transform.Forward)) * 3.6f;
        float speedLimit = MathUtil.Clamp(1f - speedKmh / MathF.Max(MaxSpeedKmh, 1f), 0f, 1f);
        float steerFraction = MaxSteerDegrees > MathUtil.Epsilon ? MathF.Abs(_steer) / MaxSteerDegrees : 0f;
        speedLimit *= MathUtil.Lerp(1f, 0.4f, MathUtil.Clamp(steerFraction, 0f, 1f));

        float targetSteer = steerInput * MaxSteerDegrees;
        float maxSteerStep = SteeringRate * MaxSteerDegrees * ScriptTime.DeltaTime;
        _steer = MathUtil.MoveTowards(_steer, targetSteer, maxSteerStep);

        bool reversing = throttle < 0f;
        float driveTorque = (reversing ? ReverseTorque : MotorTorque) * throttle * speedLimit;
        bool idle = MathF.Abs(throttle) < MathUtil.Epsilon;

        foreach (WheelColliderComponent wheel in _wheels)
        {
            bool front = wheel.Transform.LocalPosition.Z < 0f;
            wheel.SteerAngle = front ? _steer : 0f;
            if (handbrake)
            {
                wheel.MotorTorque = 0f;
                wheel.BrakeTorque = BrakeTorque;
                continue;
            }
            bool driven = AllWheelDrive || !front;
            wheel.MotorTorque = driven ? driveTorque : 0f;
            wheel.BrakeTorque = idle && speedKmh < 3f ? BrakeTorque * 0.1f : 0f;
        }
    }
}
