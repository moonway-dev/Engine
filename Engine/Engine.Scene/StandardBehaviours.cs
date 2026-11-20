using Engine.Core;
using Engine.Mathematics;

namespace Engine.Scene;

public enum BehaviourSpace
{
    Self,
    World,
}

public sealed class RotatorComponent : Component, ISceneBehaviour
{
    public Vector3 Axis { get; set; } = Vector3.Up;
    [InspectorRange(0f, 720f)]
    [InspectorTooltip("Degrees per second.")]
    public float DegreesPerSecond { get; set; } = 90f;
    public BehaviourSpace Space { get; set; } = BehaviourSpace.Self;

    public void BehaviourUpdate(in FrameTime frameTime)
    {
        Vector3 axis = Axis;
        if (axis.LengthSquared < MathUtil.Epsilon * MathUtil.Epsilon)
            return;
        Quaternion delta = Quaternion.FromAxisAngle(axis.Normalized, DegreesPerSecond * MathUtil.DegToRad * frameTime.DeltaTime);
        Transform.LocalRotation = Space == BehaviourSpace.Self ? Transform.LocalRotation * delta : delta * Transform.LocalRotation;
    }

    public void BehaviourFixedUpdate(in FrameTime frameTime)
    {
    }
}

public sealed class OscillatorComponent : Component, ISceneBehaviour
{
    public Vector3 Offset { get; set; } = new(0f, 1f, 0f);
    [InspectorRange(0.01f, 10f)]
    public float Frequency { get; set; } = 1f;
    [InspectorRange(0f, 360f)]
    public float PhaseDegrees { get; set; }

    private Vector3 _basePosition;
    private float _time;
    private bool _captured;

    public void BehaviourUpdate(in FrameTime frameTime)
    {
        if (!_captured)
        {
            _basePosition = Transform.LocalPosition;
            _time = PhaseDegrees * MathUtil.DegToRad / MathF.Max(Frequency, MathUtil.Epsilon);
            _captured = true;
        }
        _time += frameTime.DeltaTime * MathF.Max(Frequency, MathUtil.Epsilon);
        Transform.LocalPosition = _basePosition + Offset * (MathF.Sin(_time * MathUtil.TwoPi) * 0.5f + 0.5f);
    }

    public void BehaviourFixedUpdate(in FrameTime frameTime)
    {
    }
}

public sealed class LookAtComponent : Component, ISceneBehaviour
{
    [InspectorTooltip("Object to face; drag from the Hierarchy.")]
    public EntityReference Target { get; set; }
    public bool KeepUpright { get; set; } = true;
    [InspectorRange(0f, 30f)]
    [InspectorTooltip("Turn speed; 0 snaps instantly.")]
    public float TurnSpeed { get; set; } = 8f;

    public void BehaviourUpdate(in FrameTime frameTime)
    {
        if (Owner.Scene.FindById(Target.Id) is not { } target)
            return;
        Vector3 toTarget = target.Transform.Position - Transform.Position;
        if (KeepUpright)
            toTarget = new Vector3(toTarget.X, 0f, toTarget.Z);
        if (toTarget.LengthSquared < MathUtil.Epsilon * MathUtil.Epsilon)
            return;
        Quaternion goal = Quaternion.LookRotation(toTarget.Normalized, Vector3.Up);
        Transform.Rotation = TurnSpeed <= MathUtil.Epsilon
            ? goal
            : Quaternion.Slerp(Transform.Rotation, goal, MathUtil.Clamp(frameTime.DeltaTime * TurnSpeed, 0f, 1f));
    }

    public void BehaviourFixedUpdate(in FrameTime frameTime)
    {
    }
}

public sealed class FollowComponent : Component, ISceneBehaviour
{
    [InspectorTooltip("Object to follow; drag from the Hierarchy.")]
    public EntityReference Target { get; set; }
    [InspectorRange(0f, 20f)]
    [InspectorTooltip("Higher values follow tighter; 0 snaps instantly.")]
    public float Smoothness { get; set; } = 5f;
    public Vector3 Offset { get; set; } = Vector3.Zero;
    public bool FollowRotation { get; set; }

    public void BehaviourUpdate(in FrameTime frameTime)
    {
        if (Owner.Scene.FindById(Target.Id) is not { } target)
            return;
        Vector3 goal = target.Transform.Position + target.Transform.Rotation.Rotate(Offset);
        float blend = Smoothness <= MathUtil.Epsilon
            ? 1f
            : MathUtil.Clamp(frameTime.DeltaTime * Smoothness, 0f, 1f);
        Transform.Position = Vector3.Lerp(Transform.Position, goal, blend);
        if (FollowRotation)
            Transform.Rotation = Quaternion.Slerp(Transform.Rotation, target.Transform.Rotation, blend);
    }

    public void BehaviourFixedUpdate(in FrameTime frameTime)
    {
    }
}

public sealed class LifetimeComponent : Component, ISceneBehaviour
{
    [InspectorRange(0.01f, 600f)]
    public float Seconds { get; set; } = 3f;

    private float _elapsed;

    public void BehaviourUpdate(in FrameTime frameTime)
    {
        _elapsed += frameTime.DeltaTime;
        if (_elapsed >= Seconds)
            SceneBehavioursModule.RequestDestroy(Owner);
    }

    public void BehaviourFixedUpdate(in FrameTime frameTime)
    {
    }
}
