using BepuPhysics;
using Engine.Core;
using Engine.Mathematics;
using Engine.Physics;
using Engine.Scene;
using Xunit;

namespace Engine.Tests.Physics;

public sealed class JointComponentTests : IDisposable
{
    private readonly PhysicsModule _physics = new();
    private readonly Scene.Scene _scene = new("Joint tests");
    private readonly FrameTime _time = new(1f / 60f, 1f / 60f, 0, 1f / 60f, 0);

    public JointComponentTests()
    {
        _physics.Initialize(ApplicationBuilder.Create().Build().Context);
        _physics.AttachScene(_scene);
    }

    private GameObject Crate(string name, Vector3 position, Quaternion? rotation = null)
    {
        GameObject crate = _scene.CreateObject(name);
        crate.Transform.Position = position;
        crate.Transform.Rotation = rotation ?? Quaternion.Identity;
        crate.AddComponent<RigidBodyComponent>().Kind = RigidBodyKind.Kinematic;
        crate.AddComponent<BoxColliderComponent>().Size = new Vector3(1f);
        return crate;
    }

    private GameObject DynamicCrate(string name, Vector3 position, Quaternion? rotation = null)
    {
        GameObject crate = Crate(name, position, rotation);
        crate.GetComponent<RigidBodyComponent>()!.Kind = RigidBodyKind.Dynamic;
        return crate;
    }

    private void Step(int count)
    {
        for (int i = 0; i < count; i++)
            _physics.FixedUpdate(_time);
    }

    private static float Distance(GameObject a, GameObject b) => Vector3.Distance(a.Transform.Position, b.Transform.Position);

    [Fact]
    public void BallJointKeepsAnchorsTogether()
    {
        GameObject a = DynamicCrate("A", new Vector3(0f, 6f, 0f));
        GameObject b = DynamicCrate("B", new Vector3(2f, 6f, 0f));
        BallJointComponent? joint = a.AddComponent<BallJointComponent>();
        Assert.NotNull(joint);
        joint!.ConnectedBody = new EntityReference(b.Id);
        joint.LocalAnchor = new Vector3(0.5f, 0f, 0f);
        joint.ConnectedLocalAnchor = new Vector3(-0.5f, 0f, 0f);
        Step(150);
        Vector3 anchorA = a.Transform.Position + a.Transform.Rotation.Rotate(joint.LocalAnchor);
        Vector3 anchorB = b.Transform.Position + b.Transform.Rotation.Rotate(joint.ConnectedLocalAnchor);
        Assert.InRange(Vector3.Distance(anchorA, anchorB), 0f, 0.12f);
    }

    [Fact]
    public void FixedJointPreservesRelativePose()
    {
        Quaternion rotationB = Quaternion.FromAxisAngle(Vector3.UnitZ, 0.7f) * Quaternion.FromAxisAngle(Vector3.UnitY, 1.1f);
        GameObject a = DynamicCrate("A", new Vector3(0f, 6f, 0f));
        GameObject b = DynamicCrate("B", new Vector3(0f, 7.5f, 0f), rotationB);
        FixedJointComponent joint = a.AddComponent<FixedJointComponent>();
        joint.ConnectedBody = new EntityReference(b.Id);
        Step(60);
        Vector3 offset = b.Transform.Position - a.Transform.Position;
        Quaternion relative = a.Transform.Rotation.Inverse * b.Transform.Rotation;
        Step(240);
        Vector3 offsetAfter = b.Transform.Position - a.Transform.Position;
        Quaternion relativeAfter = a.Transform.Rotation.Inverse * b.Transform.Rotation;
        float offsetDrift = Vector3.Distance(offset, offsetAfter);
        float orientationDot = MathF.Abs(
            relative.X * relativeAfter.X
            + relative.Y * relativeAfter.Y
            + relative.Z * relativeAfter.Z
            + relative.W * relativeAfter.W);
        Assert.True(offsetDrift <= 0.12f, $"offset drift {offsetDrift}");
        Assert.True(orientationDot > 0.985f, $"orientation drift {orientationDot}");
    }

    [Fact]
    public void HingeJointKeepsAnchorWithinTolerance()
    {
        GameObject a = DynamicCrate("A", new Vector3(0f, 6f, 0f));
        GameObject b = DynamicCrate("B", new Vector3(1f, 6f, 0f));
        HingeJointComponent joint = a.AddComponent<HingeJointComponent>();
        joint.ConnectedBody = new EntityReference(b.Id);
        joint.LocalAnchor = new Vector3(0.5f, 0f, 0f);
        joint.ConnectedLocalAnchor = new Vector3(-0.5f, 0f, 0f);
        joint.LocalAxis = Vector3.Up;
        Step(180);
        Vector3 anchorA = a.Transform.Position + a.Transform.Rotation.Rotate(joint.LocalAnchor);
        Vector3 anchorB = b.Transform.Position + b.Transform.Rotation.Rotate(joint.ConnectedLocalAnchor);
        Assert.InRange(Vector3.Distance(anchorA, anchorB), 0f, 0.15f);
    }

    [Fact]
    public void HingeMotorDrivesRelativeAngularVelocity()
    {
        GameObject a = DynamicCrate("A", new Vector3(0f, 6f, 0f));
        GameObject b = Crate("B", new Vector3(1.05f, 6f, 0f));
        HingeJointComponent joint = a.AddComponent<HingeJointComponent>();
        joint.ConnectedBody = new EntityReference(b.Id);
        joint.LocalAnchor = new Vector3(0.525f, 0f, 0f);
        joint.ConnectedLocalAnchor = new Vector3(-0.525f, 0f, 0f);
        joint.LocalAxis = Vector3.Up;
        joint.UseMotor = true;
        joint.TargetVelocityDegreesPerSecond = 360f;
        joint.MotorStrength = 20f;
        Step(120);
        Vector3 relative = _physics.World.GetBodyAngularVelocity(_physics.TryGetBodyHandle(a, out BodyHandle handleA) ? handleA : default);
        Assert.True(relative.Y > 1f, $"relative angular velocity {relative}");
    }

    [Fact]
    public void SpringJointPullsBodiesTowardTargetDistance()
    {
        GameObject a = DynamicCrate("A", new Vector3(-2f, 6f, 0f));
        GameObject b = DynamicCrate("B", new Vector3(2f, 6f, 0f));
        SpringJointComponent joint = a.AddComponent<SpringJointComponent>();
        joint.ConnectedBody = new EntityReference(b.Id);
        joint.TargetDistance = 1f;
        joint.Frequency = 4f;
        Step(180);
        float distance = Distance(a, b);
        Assert.True(distance < 3.6f, $"spring did not pull bodies together: {distance}");
    }

    [Fact]
    public void ConfigurableJointLocksOffsetAxis()
    {
        GameObject a = DynamicCrate("A", new Vector3(0f, 6f, 0f));
        GameObject b = DynamicCrate("B", new Vector3(1f, 6f, 0f));
        ConfigurableJointComponent joint = a.AddComponent<ConfigurableJointComponent>();
        joint.ConnectedBody = new EntityReference(b.Id);
        joint.XMotion = JointMotion.Locked;
        joint.YMotion = JointMotion.Locked;
        joint.ZMotion = JointMotion.Locked;
        Step(180);
        Vector3 offset = b.Transform.Position - a.Transform.Position;
        Assert.InRange(offset.Length, 0.85f, 1.15f);
    }

    [Fact]
    public void CharacterJointKeepsLimbAttached()
    {
        GameObject torso = DynamicCrate("Torso", new Vector3(0f, 6f, 0f));
        GameObject limb = DynamicCrate("Limb", new Vector3(0f, 4.8f, 0f));
        CharacterJointComponent joint = torso.AddComponent<CharacterJointComponent>();
        joint.ConnectedBody = new EntityReference(limb.Id);
        joint.LocalAnchor = new Vector3(0f, -0.5f, 0f);
        joint.ConnectedLocalAnchor = new Vector3(0f, 0.7f, 0f);
        Step(180);
        Assert.InRange(Distance(torso, limb), 1.0f, 1.3f);
    }

    [Fact]
    public void JointRemovedWhenConnectedBodyCleared()
    {
        GameObject a = DynamicCrate("A", new Vector3(0f, 6f, 0f));
        GameObject b = DynamicCrate("B", new Vector3(2f, 6f, 0f));
        BallJointComponent joint = a.AddComponent<BallJointComponent>();
        joint.ConnectedBody = new EntityReference(b.Id);
        joint.LocalAnchor = new Vector3(0.5f, 0f, 0f);
        joint.ConnectedLocalAnchor = new Vector3(-0.5f, 0f, 0f);
        Step(5);
        int constraintsBefore = _physics.World.Simulation.Solver.CountConstraints();
        Assert.True(constraintsBefore > 0);
        joint.ConnectedBody = EntityReference.Empty;
        Step(2);
        int constraintsAfter = _physics.World.Simulation.Solver.CountConstraints();
        Assert.True(constraintsAfter < constraintsBefore, $"joint was not removed: {constraintsBefore} -> {constraintsAfter}");
    }

    [Fact]
    public void WheelColliderDrivesChassisForward()
    {
        GameObject ground = _scene.CreateObject("Ground");
        ground.AddComponent<RigidBodyComponent>().Kind = RigidBodyKind.Static;
        BoxColliderComponent floor = ground.AddComponent<BoxColliderComponent>();
        floor.Size = new Vector3(60f, 0.25f, 400f);
        GameObject chassis = _scene.CreateObject("Chassis");
        chassis.Transform.Position = new Vector3(0f, 0.6f, 0f);
        chassis.AddComponent<RigidBodyComponent>().Mass = 400f;
        BoxColliderComponent body = chassis.AddComponent<BoxColliderComponent>();
        body.Size = new Vector3(2f, 0.4f, 3f);
        foreach ((float x, float z) in new[] { (-0.9f, -1.1f), (0.9f, -1.1f), (-0.9f, 1.1f), (0.9f, 1.1f) })
        {
            GameObject wheelObject = _scene.CreateObject($"Wheel{ x }_{ z }", chassis);
            wheelObject.Transform.LocalPosition = new Vector3(x, -0.1f, z);
            WheelColliderComponent wheel = wheelObject.AddComponent<WheelColliderComponent>();
            wheel.Radius = 0.3f;
            wheel.SuspensionDistance = 0.4f;
            wheel.MotorTorque = 400f;
        }
        Step(160);
        Vector3 position = chassis.Transform.Position;
        Assert.True(float.IsFinite(position.Y) && position.Y > 0f, $"chassis fell through the ground: {position}");
        Assert.True(MathF.Abs(position.X) + MathF.Abs(position.Z) > 0.3f, $"wheels did not drive the chassis: {position}");
    }

    [Fact]
    public void ConstantForceAcceleratesBody()
    {
        GameObject a = DynamicCrate("A", new Vector3(0f, 6f, 0f));
        ConstantForceComponent force = a.AddComponent<ConstantForceComponent>();
        force.Force = new Vector3(10f, 0f, 0f);
        float mass = a.GetComponent<RigidBodyComponent>()!.Mass;
        Step(60);
        float velocityX = _physics.World.GetBodyLinearVelocity(_physics.TryGetBodyHandle(a, out BodyHandle handle) ? handle : default).X;
        Assert.InRange(velocityX, 9.2f, 10.8f);
    }

    public void Dispose() => _physics.Shutdown();
}
