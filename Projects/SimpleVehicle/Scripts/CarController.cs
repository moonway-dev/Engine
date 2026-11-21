using Engine.Mathematics;
using Engine.Physics;
using Engine.Scene;
using Engine.Scripting;

public sealed class CarController : ScriptBehaviour
{
    [InspectorTooltip("Torque applied to each driven wheel.")]
    public float MotorTorque = 1800f;
    [InspectorTooltip("Reverse motor torque applied to each driven wheel.")]
    public float ReverseTorque = 1200f;
    [InspectorRange(20f, 260f)]
    [InspectorTooltip("Speed at which the motor cuts out, in km/h.")]
    public float MaxSpeedKmh = 190f;
    [InspectorRange(100f, 20000f)]
    [InspectorTooltip("Brake torque per wheel when braking.")]
    public float BrakeTorque = 5200f;
    [InspectorRange(0f, 1f)]
    [InspectorTooltip("Share of braking torque given to the front axle.")]
    public float FrontBrakeBias = 0.62f;
    [InspectorRange(100f, 20000f)]
    [InspectorTooltip("Brake torque per rear wheel while the handbrake is held.")]
    public float HandbrakeTorque = 6800f;
    [InspectorRange(0f, 1f)]
    [InspectorTooltip("Share of the service brake the front axle keeps while the handbrake is held.")]
    public float HandbrakeFrontBrakeFraction = 0.12f;
    [InspectorRange(0.05f, 1f)]
    [InspectorTooltip("Rear lateral grip multiplier while the handbrake is held; lower values let the tail slide.")]
    public float HandbrakeRearGrip = 0.28f;
    [InspectorRange(5f, 45f)]
    [InspectorTooltip("Steering lock at standstill, in degrees.")]
    public float MaxSteerDegrees = 38f;
    [InspectorRange(2f, 30f)]
    [InspectorTooltip("Steering lock at high speed, in degrees.")]
    public float HighSpeedSteerDegrees = 9f;
    [InspectorRange(20f, 260f)]
    [InspectorTooltip("Speed in km/h at which the steering lock reaches its high-speed value.")]
    public float SteerFadeSpeedKmh = 170f;
    [InspectorRange(1f, 12f)]
    [InspectorTooltip("How fast the steering wheel moves toward the target angle.")]
    public float SteeringRate = 6.5f;
    [InspectorRange(0f, 20f)]
    [InspectorTooltip("Downforce coefficient; pushes the body into the road with speed, in newtons per (m/s) squared.")]
    public float Downforce = 2.2f;
    [InspectorRange(0f, 5f)]
    [InspectorTooltip("Aerodynamic drag coefficient, in newtons per (m/s) squared.")]
    public float DragCoefficient = 0.65f;
    [InspectorRange(0f, 3f)]
    [InspectorTooltip("How quickly airborne rotation settles, per second; keeps ramp jumps controllable.")]
    public float AirStabilization = 2.5f;
    [InspectorRange(0f, 6f)]
    [InspectorTooltip("Mid-air pitch control authority from the throttle axis, in rad/s squared.")]
    public float AirPitchAuthority = 2.6f;
    [InspectorRange(0f, 6f)]
    [InspectorTooltip("Mid-air roll control authority from the steering axis, in rad/s squared.")]
    public float AirRollAuthority = 2.6f;
    [InspectorRange(0f, 1f)]
    [InspectorTooltip("Share of brake torque that keeps dragging on driven wheels when the throttle is released.")]
    public float EngineBrakingFraction = 0.05f;
    public bool AllWheelDrive;
    [InspectorTooltip("Key used as a handbrake.")]
    public KeyCode HandbrakeKey = KeyCode.Space;

    private WheelColliderComponent[] _wheels = [];
    private float _steer;
    private float _airborneTime;

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
        float deltaTime = ScriptTime.DeltaTime;

        Vector3 velocity = ScriptPhysics.GetLinearVelocity(Owner);
        float forwardSpeed = Vector3.Dot(velocity, Transform.Forward);
        float speedKmh = MathF.Abs(forwardSpeed) * 3.6f;

        float steerFade = MathUtil.Clamp(speedKmh / MathF.Max(SteerFadeSpeedKmh, 1f), 0f, 1f);
        float steerLock = MathUtil.Lerp(MaxSteerDegrees, HighSpeedSteerDegrees, steerFade);
        float maxSteerStep = SteeringRate * MaxSteerDegrees * deltaTime;
        _steer = MathUtil.MoveTowards(_steer, steerInput * steerLock, maxSteerStep);

        bool reversing = throttle < 0f && forwardSpeed < 1.5f;
        bool braking = (throttle < 0f && forwardSpeed >= 1.5f) || (throttle > 0f && forwardSpeed < -1.5f);
        float speedLimit = MathUtil.Clamp(1f - speedKmh / MathF.Max(MaxSpeedKmh, 1f), 0f, 1f);
        float driveTorque = (reversing ? ReverseTorque : MotorTorque) * throttle * speedLimit;
        float frontBrake = braking ? BrakeTorque * FrontBrakeBias : 0f;
        float rearBrake = braking ? BrakeTorque * (1f - FrontBrakeBias) : 0f;

        foreach (WheelColliderComponent wheel in _wheels)
        {
            bool front = wheel.Transform.LocalPosition.Z < 0f;
            wheel.SteerAngle = front ? _steer : 0f;
            wheel.LateralGrip = 1f;
            wheel.MotorTorque = 0f;
            wheel.BrakeTorque = 0f;
            if (handbrake)
            {
                if (front)
                {
                    wheel.BrakeTorque = BrakeTorque * HandbrakeFrontBrakeFraction;
                }
                else
                {
                    wheel.BrakeTorque = HandbrakeTorque;
                    wheel.LateralGrip = HandbrakeRearGrip;
                }
                continue;
            }
            if (braking)
            {
                wheel.BrakeTorque = front ? frontBrake : rearBrake;
                continue;
            }
            bool driven = AllWheelDrive || !front;
            if (driven && MathF.Abs(driveTorque) > MathUtil.Epsilon)
            {
                wheel.MotorTorque = driveTorque;
            }
            else if (driven && MathF.Abs(throttle) < MathUtil.Epsilon)
            {
                wheel.BrakeTorque = BrakeTorque * EngineBrakingFraction;
            }
            else if (MathF.Abs(throttle) < MathUtil.Epsilon && speedKmh < 3f)
            {
                wheel.BrakeTorque = BrakeTorque * 0.08f;
            }
        }

        float speed = velocity.Length;
        Vector3 extraForce = Transform.Up * (-speed * speed * Downforce) - velocity * (speed * DragCoefficient);
        ScriptPhysics.AddImpulse(Owner, extraForce * deltaTime);

        _airborneTime = _wheels.All(w => !w.IsGrounded) ? _airborneTime + deltaTime : 0f;
        if (_airborneTime > 0.25f)
        {
            float inertiaEstimate = ScriptPhysics.GetMass(Owner) * 1.8f;
            Vector3 angularVelocity = ScriptPhysics.GetAngularVelocity(Owner);
            ScriptPhysics.AddAngularImpulse(Owner, -angularVelocity * (inertiaEstimate * AirStabilization * deltaTime));
            Vector3 pitchAxis = Transform.Right;
            Vector3 rollAxis = Transform.Forward;
            ScriptPhysics.AddAngularImpulse(Owner, pitchAxis * (-throttle * AirPitchAuthority * inertiaEstimate * deltaTime));
            ScriptPhysics.AddAngularImpulse(Owner, rollAxis * (-steerInput * AirRollAuthority * inertiaEstimate * deltaTime));
        }
    }
}
