using Engine.Core;
using Engine.Mathematics;
using Engine.Physics;
using Engine.Scene;
using Xunit;

namespace Engine.Tests.Physics;

public sealed class PhysicsModuleTests : IDisposable
{
    private readonly PhysicsModule _physics = new();
    private readonly Scene.Scene _scene = new("Physics tests");
    private readonly FrameTime _time = new(1f / 60f, 1f / 60f, 0, 1f / 60f, 0);

    public PhysicsModuleTests()
    {
        _physics.Initialize(ApplicationBuilder.Create().Build().Context);
        _physics.AttachScene(_scene);
    }

    private GameObject Ground()
    {
        GameObject ground = _scene.CreateObject("Ground");
        ground.Transform.Position = new Vector3(0, -0.75f, 0);
        RigidBodyComponent body = ground.AddComponent<RigidBodyComponent>();
        body.Kind = RigidBodyKind.Static;
        body.Size = new Vector3(24, 0.25f, 24);
        body.Center = new Vector3(0, -0.125f, 0);
        return ground;
    }

    private GameObject Crate()
    {
        GameObject crate = _scene.CreateObject("Crate");
        crate.Transform.Position = new Vector3(0, 4.5f, 0);
        crate.AddComponent<RigidBodyComponent>().Size = new Vector3(1.4f);
        return crate;
    }

    private void Step(int count)
    {
        for (int i = 0; i < count; i++)
            _physics.FixedUpdate(_time);
    }

    [Fact]
    public void CrateRestsWithBottomOnVisibleGround()
    {
        Ground();
        GameObject crate = Crate();
        Step(300);
        Assert.InRange(crate.Transform.Position.Y - 0.7f, -0.77f, -0.73f);
    }

    [Fact]
    public void MovingGroundWakesSettledCrate()
    {
        GameObject ground = Ground();
        GameObject crate = Crate();
        Step(300);
        ground.Transform.Position -= new Vector3(0, 2, 0);
        Step(180);
        Assert.InRange(crate.Transform.Position.Y - 0.7f, -2.77f, -2.73f);
    }

    [Fact]
    public void RotatingGroundChangesStaticColliderPose()
    {
        GameObject ground = Ground();
        _physics.Update(_time);
        Quaternion rotation = Quaternion.FromAxisAngle(Vector3.UnitZ, 0.4f);
        ground.Transform.Rotation = rotation;
        _physics.Update(_time);
        var pose = _physics.World.Simulation.Statics[new BepuPhysics.StaticHandle(0)].Pose;
        Assert.InRange(pose.Orientation.Z, rotation.Z - 0.0001f, rotation.Z + 0.0001f);
        Vector3 center = ground.Transform.Position + rotation.Rotate(new Vector3(0, -0.125f, 0));
        Assert.InRange(pose.Position.X, center.X - 0.0001f, center.X + 0.0001f);
    }

    [Fact]
    public void UpdatesDoNotAdvanceSimulationOrDuplicateBodies()
    {
        GameObject crate = Crate();
        for (int i = 0; i < 500; i++)
            _physics.Update(_time);
        Assert.Equal(4.5f, crate.Transform.Position.Y);
        Assert.Equal(1, _physics.World.Simulation.Bodies.ActiveSet.Count);
        Step(60);
        Assert.True(crate.Transform.Position.Y < 1f);
    }

    [Fact]
    public void DynamicTransformEditIsNotOverwritten()
    {
        GameObject crate = Crate();
        Step(30);
        crate.Transform.Position = new Vector3(5, 10, 2);
        Step(1);
        Assert.InRange(crate.Transform.Position.Y, 9.98f, 10f);
        Assert.InRange(crate.Transform.Position.X, 4.999f, 5.001f);
    }

    [Fact]
    public void ColliderResizeAndKindChangesReplaceExistingBody()
    {
        Ground();
        GameObject crate = Crate();
        Step(300);
        RigidBodyComponent body = crate.GetComponent<RigidBodyComponent>()!;
        body.Size = new Vector3(2f);
        crate.Transform.Position = new Vector3(0, 3, 0);
        Step(180);
        Assert.InRange(crate.Transform.Position.Y, 0.23f, 0.27f);
        body.Kind = RigidBodyKind.Static;
        _physics.Update(_time);
        Assert.Equal(2, _physics.World.Simulation.Statics.Count);
        body.Kind = RigidBodyKind.Dynamic;
        _physics.Update(_time);
        Assert.Equal(1, _physics.World.Simulation.Statics.Count);
    }

    [Fact]
    public void SceneReplacementClearsOldBodies()
    {
        Ground();
        Crate();
        Step(1);
        _physics.AttachScene(new Scene.Scene("Empty"));
        Step(1);
        Assert.Equal(0, _physics.World.Simulation.Statics.Count);
        Assert.Equal(0, _physics.World.Simulation.Bodies.ActiveSet.Count);
    }

    [Fact]
    public void RotatingGroundMakesSettledCrateMove()
    {
        GameObject ground = Ground();
        GameObject crate = Crate();
        Step(300);
        ground.Transform.Rotation = Quaternion.FromAxisAngle(Vector3.UnitZ, 0.5f);
        Step(120);
        Assert.True(MathF.Abs(crate.Transform.Position.X) > 0.2f);
    }

    [Fact]
    public void KinematicRotationTracksTransform()
    {
        GameObject crate = Crate();
        crate.GetComponent<RigidBodyComponent>()!.Kind = RigidBodyKind.Kinematic;
        _physics.Update(_time);
        Quaternion rotation = Quaternion.FromAxisAngle(Vector3.UnitY, 0.7f);
        crate.Transform.Rotation = rotation;
        _physics.Update(_time);
        Quaternion actual = _physics.World.GetBodyRotation(new BepuPhysics.BodyHandle(0));
        Assert.True((actual.Rotate(Vector3.UnitX) - rotation.Rotate(Vector3.UnitX)).Length < 0.0001f);
    }

    public void Dispose() => _physics.Shutdown();
}
