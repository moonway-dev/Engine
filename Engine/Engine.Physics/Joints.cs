using BepuPhysics;
using Engine.Mathematics;
using Engine.Scene;

namespace Engine.Physics;

public abstract class PhysicsJointComponent : Component
{
    [InspectorTooltip("Connected body; drag a Hierarchy object with a Rigidbody here.")]
    public EntityReference ConnectedBody { get; set; }

    internal abstract string BuildSignature();

    internal abstract ConstraintHandle[] CreateConstraints(
        PhysicsWorld world,
        BodyHandle self,
        BodyHandle connected,
        Transform selfTransform,
        Transform connectedTransform);
}

[InspectorTooltip("Ball joint that keeps two anchors together while allowing free rotation.")]
public sealed class BallJointComponent : PhysicsJointComponent
{
    [InspectorTooltip("Anchor on this object, in its local space.")]
    public Vector3 LocalAnchor { get; set; }
    [InspectorTooltip("Anchor on the connected body, in its local space.")]
    public Vector3 ConnectedLocalAnchor { get; set; }

    internal override string BuildSignature() => $"Ball|{ConnectedBody.Id}|{LocalAnchor}|{ConnectedLocalAnchor}";

    internal override ConstraintHandle[] CreateConstraints(
        PhysicsWorld world,
        BodyHandle self,
        BodyHandle connected,
        Transform selfTransform,
        Transform connectedTransform)
    {
        Vector3 anchorA = selfTransform.Position + selfTransform.Rotation.Rotate(LocalAnchor);
        Vector3 anchorB = connectedTransform.Position + connectedTransform.Rotation.Rotate(ConnectedLocalAnchor);
        return world.AddBallJoint(self, connected, anchorA, anchorB);
    }
}

[InspectorTooltip("Welds two bodies together, preserving their relative pose at creation.")]
public sealed class FixedJointComponent : PhysicsJointComponent
{
    internal override string BuildSignature() => $"Fixed|{ConnectedBody.Id}";

    internal override ConstraintHandle[] CreateConstraints(
        PhysicsWorld world,
        BodyHandle self,
        BodyHandle connected,
        Transform selfTransform,
        Transform connectedTransform) => world.AddFixedJoint(self, connected);
}

[InspectorTooltip("Rotates around a single axis with an optional motor.")]
public sealed class HingeJointComponent : PhysicsJointComponent
{
    [InspectorTooltip("Anchor on this object, in its local space.")]
    public Vector3 LocalAnchor { get; set; }
    [InspectorTooltip("Anchor on the connected body, in its local space.")]
    public Vector3 ConnectedLocalAnchor { get; set; }
    [InspectorTooltip("Hinge axis in this object's local space.")]
    public Vector3 LocalAxis { get; set; } = Vector3.Up;
    public bool UseMotor { get; set; }
    [InspectorRange(-3600f, 3600f)]
    [InspectorTooltip("Target angular velocity of this body around the hinge axis, degrees per second.")]
    public float TargetVelocityDegreesPerSecond { get; set; } = 180f;
    [InspectorRange(0f, 100f)]
    [InspectorTooltip("How aggressively the motor reaches the target velocity.")]
    public float MotorStrength { get; set; } = 8f;

    internal override string BuildSignature() =>
        $"Hinge|{ConnectedBody.Id}|{LocalAnchor}|{ConnectedLocalAnchor}|{LocalAxis}";

    internal override ConstraintHandle[] CreateConstraints(
        PhysicsWorld world,
        BodyHandle self,
        BodyHandle connected,
        Transform selfTransform,
        Transform connectedTransform)
    {
        Vector3 anchorA = selfTransform.Position + selfTransform.Rotation.Rotate(LocalAnchor);
        Vector3 anchorB = connectedTransform.Position + connectedTransform.Rotation.Rotate(ConnectedLocalAnchor);
        Vector3 axis = LocalAxis;
        if (axis.LengthSquared < MathUtil.Epsilon * MathUtil.Epsilon)
            axis = Vector3.Up;
        return world.AddHingeJoint(self, connected, anchorA, anchorB, axis.Normalized);
    }

    internal void ApplyMotor(PhysicsModule module, float deltaTime)
    {
        if (!UseMotor || MotorStrength <= 0f || Owner is not { } owner
            || Owner.Scene.FindById(ConnectedBody.Id) is not { } connected)
            return;
        if (!module.TryGetJointBody(owner, out BodyHandle bodyA) || !module.TryGetJointBody(connected, out BodyHandle bodyB))
            return;
        Vector3 axis = Owner.Transform.Rotation.Rotate(LocalAxis);
        if (axis.LengthSquared < MathUtil.Epsilon * MathUtil.Epsilon)
            return;
        axis = axis.Normalized;
        float target = TargetVelocityDegreesPerSecond * MathF.PI / 180f;
        Vector3 velocityA = module.GetJointBodyAngularVelocity(bodyA);
        Vector3 velocityB = module.GetJointBodyAngularVelocity(bodyB);
        float current = Vector3.Dot(velocityA - velocityB, axis);
        float blend = MathUtil.Clamp(deltaTime * MotorStrength, 0f, 1f);
        float delta = (target - current) * blend;
        Vector3 anchorWorld = Owner.Transform.Position + Owner.Transform.Rotation.Rotate(LocalAnchor);
        Vector3 deltaOmega = axis * delta;
        Vector3 offsetA = anchorWorld - module.GetJointBodyPosition(bodyA);
        Vector3 deltaLinearA = -Vector3.Cross(deltaOmega, offsetA);
        module.SetJointBodyVelocity(
            bodyA,
            module.GetJointBodyLinearVelocity(bodyA) + deltaLinearA,
            velocityA + deltaOmega);
    }
}

[InspectorTooltip("Soft spring that pulls two anchors toward a target distance.")]
public sealed class SpringJointComponent : PhysicsJointComponent
{
    [InspectorTooltip("Anchor on this object, in its local space.")]
    public Vector3 LocalAnchor { get; set; }
    [InspectorTooltip("Anchor on the connected body, in its local space.")]
    public Vector3 ConnectedLocalAnchor { get; set; }
    [InspectorRange(0f, 500f)]
    [InspectorTooltip("Distance the spring tries to maintain.")]
    public float TargetDistance { get; set; } = 1f;
    [InspectorRange(0.1f, 60f)]
    [InspectorTooltip("Spring frequency; higher is stiffer.")]
    public float Frequency { get; set; } = 6f;
    [InspectorRange(0f, 1f)]
    public float DampingRatio { get; set; } = 0.6f;

    internal override string BuildSignature() =>
        $"Spring|{ConnectedBody.Id}|{LocalAnchor}|{ConnectedLocalAnchor}|{TargetDistance}|{Frequency}|{DampingRatio}";

    internal override ConstraintHandle[] CreateConstraints(
        PhysicsWorld world,
        BodyHandle self,
        BodyHandle connected,
        Transform selfTransform,
        Transform connectedTransform)
    {
        Vector3 anchorA = selfTransform.Position + selfTransform.Rotation.Rotate(LocalAnchor);
        Vector3 anchorB = connectedTransform.Position + connectedTransform.Rotation.Rotate(ConnectedLocalAnchor);
        return world.AddSpringJoint(self, connected, anchorA, anchorB, TargetDistance, Frequency, DampingRatio);
    }
}

[InspectorTooltip("Ball joint with cone (swing) and twist limits for ragdoll-style limbs.")]
public sealed class CharacterJointComponent : PhysicsJointComponent
{
    [InspectorTooltip("Anchor on this object, in its local space.")]
    public Vector3 LocalAnchor { get; set; }
    [InspectorTooltip("Anchor on the connected body, in its local space.")]
    public Vector3 ConnectedLocalAnchor { get; set; }
    [InspectorTooltip("Twist axis in this object's local space.")]
    public Vector3 LocalTwistAxis { get; set; } = Vector3.Up;
    [InspectorRange(0f, 180f)]
    [InspectorTooltip("Half cone angle the limb can swing away from the twist axis.")]
    public float SwingLimitDegrees { get; set; } = 60f;
    [InspectorRange(0f, 180f)]
    [InspectorTooltip("Twist range around the twist axis.")]
    public float TwistLimitDegrees { get; set; } = 45f;

    internal override string BuildSignature() =>
        $"Character|{ConnectedBody.Id}|{LocalAnchor}|{ConnectedLocalAnchor}|{LocalTwistAxis}|{SwingLimitDegrees}|{TwistLimitDegrees}";

    internal override ConstraintHandle[] CreateConstraints(
        PhysicsWorld world,
        BodyHandle self,
        BodyHandle connected,
        Transform selfTransform,
        Transform connectedTransform)
    {
        Vector3 anchorA = selfTransform.Position + selfTransform.Rotation.Rotate(LocalAnchor);
        Vector3 anchorB = connectedTransform.Position + connectedTransform.Rotation.Rotate(ConnectedLocalAnchor);
        Vector3 twistAxis = LocalTwistAxis;
        if (twistAxis.LengthSquared < MathUtil.Epsilon * MathUtil.Epsilon)
            twistAxis = Vector3.Up;
        return world.AddCharacterJoint(self, connected, anchorA, anchorB, twistAxis.Normalized, SwingLimitDegrees, TwistLimitDegrees);
    }
}

[InspectorTooltip("Limits linear motion along this object's local X, Y and Z axes.")]
public sealed class ConfigurableJointComponent : PhysicsJointComponent
{
    public JointMotion XMotion { get; set; } = JointMotion.Locked;
    public JointMotion YMotion { get; set; } = JointMotion.Locked;
    public JointMotion ZMotion { get; set; } = JointMotion.Locked;
    [InspectorRange(0f, 50f)]
    [InspectorTooltip("Free travel distance for limited axes, in meters.")]
    public float LimitDistance { get; set; } = 1f;

    internal override string BuildSignature() =>
        $"Configurable|{ConnectedBody.Id}|{XMotion}|{YMotion}|{ZMotion}|{LimitDistance}";

    internal override ConstraintHandle[] CreateConstraints(
        PhysicsWorld world,
        BodyHandle self,
        BodyHandle connected,
        Transform selfTransform,
        Transform connectedTransform)
    {
        List<ConstraintHandle> handles = [];
        Vector3[] axes = [Vector3.Right, Vector3.Up, Vector3.Forward];
        JointMotion[] motions = [XMotion, YMotion, ZMotion];
        for (int index = 0; index < axes.Length; index++)
        {
            if (motions[index] == JointMotion.Free)
                continue;
            Vector3 worldAxis = selfTransform.Rotation.Rotate(axes[index]);
            float limit = motions[index] == JointMotion.Locked ? 0f : LimitDistance;
            handles.AddRange(world.AddLinearLimit(
                self,
                connected,
                worldAxis,
                selfTransform.Position,
                selfTransform.Position,
                -limit,
                limit));
        }
        return [.. handles];
    }
}

public enum JointMotion
{
    Free,
    Limited,
    Locked,
}

[InspectorTooltip("Constant force and torque applied every physics step.")]
public sealed class ConstantForceComponent : Component
{
    public Vector3 Force { get; set; }
    public Vector3 Torque { get; set; }
    public BehaviourSpace Space { get; set; } = BehaviourSpace.World;

    internal void Apply(PhysicsWorld world, BodyHandle body, Quaternion rotation, float deltaTime)
    {
        Vector3 force = Space == BehaviourSpace.World ? Force : rotation.Rotate(Force);
        Vector3 torque = Space == BehaviourSpace.World ? Torque : rotation.Rotate(Torque);
        if (force.LengthSquared > MathUtil.Epsilon * MathUtil.Epsilon)
            world.ApplyImpulse(body, force * deltaTime);
        if (torque.LengthSquared > MathUtil.Epsilon * MathUtil.Epsilon)
            world.ApplyAngularImpulse(body, torque * deltaTime);
    }
}
