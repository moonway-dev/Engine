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
        ground.AddComponent<RigidBodyComponent>().Kind = RigidBodyKind.Static;
        BoxColliderComponent collider = ground.AddComponent<BoxColliderComponent>();
        collider.Size = new Vector3(24, 0.25f, 24);
        collider.Center = new Vector3(0, -0.125f, 0);
        return ground;
    }

    private GameObject Crate()
    {
        GameObject crate = _scene.CreateObject("Crate");
        crate.Transform.Position = new Vector3(0, 4.5f, 0);
        crate.AddComponent<RigidBodyComponent>();
        crate.AddComponent<BoxColliderComponent>().Size = new Vector3(1.4f);
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
        Assert.InRange(pose.Position.X, ground.Transform.Position.X - 0.0001f, ground.Transform.Position.X + 0.0001f);
        Assert.InRange(pose.Position.Y, ground.Transform.Position.Y - 0.0001f, ground.Transform.Position.Y + 0.0001f);
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
        Assert.InRange(crate.Transform.Position.Y, 9.8f, 10f);
        Assert.InRange(crate.Transform.Position.X, 4.999f, 5.001f);
    }

    [Fact]
    public void DynamicTransformRotationPreservesAngularVelocity()
    {
        GameObject crate = Crate();
        _physics.Update(_time);
        BepuPhysics.BodyHandle handle = new(0);
        _physics.World.Simulation.Bodies[handle].Velocity.Angular = new System.Numerics.Vector3(0f, 0f, 2f);
        Quaternion rotation = Quaternion.FromAxisAngle(Vector3.UnitY, 0.5f);
        crate.Transform.Rotation = rotation;

        _physics.Update(_time);

        System.Numerics.Vector3 angularVelocity = _physics.World.Simulation.Bodies[handle].Velocity.Angular;
        Assert.InRange(angularVelocity.Length(), 1.99f, 2f);
        Assert.Equal(rotation, _physics.World.GetBodyRotation(handle));
    }

    [Fact]
    public void DynamicTransformEditKeepsBodyMovingUnderGravity()
    {
        GameObject crate = Crate();
        _physics.Update(_time);
        crate.Transform.Position = new Vector3(0f, 10f, 0f);

        Step(1);

        Assert.InRange(crate.Transform.Position.Y, 9.99f, 10f);
        Assert.True(_physics.World.Simulation.Bodies.ActiveSet.Count > 0);
    }

    [Fact]
    public void ResizingDynamicColliderPreservesLinearAndAngularVelocity()
    {
        GameObject crate = Crate();
        _physics.Update(_time);
        BepuPhysics.BodyHandle handle = new(0);
        _physics.World.Simulation.Bodies[handle].Velocity.Linear = new System.Numerics.Vector3(1f, -3f, 2f);
        _physics.World.Simulation.Bodies[handle].Velocity.Angular = new System.Numerics.Vector3(0f, 2f, 0f);
        crate.GetComponent<BoxColliderComponent>()!.Size = new Vector3(2f);

        _physics.Update(_time);

        System.Numerics.Vector3 linear = _physics.World.Simulation.Bodies[handle].Velocity.Linear;
        System.Numerics.Vector3 angular = _physics.World.Simulation.Bodies[handle].Velocity.Angular;
        Assert.InRange(linear.X, 0.99f, 1.01f);
        Assert.InRange(linear.Y, -3.01f, -2.99f);
        Assert.InRange(linear.Z, 1.99f, 2.01f);
        Assert.InRange(angular.Y, 1.99f, 2.01f);
    }

    [Fact]
    public void ColliderResizeAndKindChangesReplaceExistingBody()
    {
        Ground();
        GameObject crate = Crate();
        Step(300);
        RigidBodyComponent body = crate.GetComponent<RigidBodyComponent>()!;
        crate.GetComponent<BoxColliderComponent>()!.Size = new Vector3(2f);
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
    public void ColliderScaleChangeRebuildsShapeUsingObjectScaleProduct()
    {
        Ground();
        GameObject crate = _scene.CreateObject("ScaledCrate");
        crate.Transform.Position = new Vector3(0f, 4f, 0f);
        crate.Transform.LocalScale = new Vector3(1f, 1.5f, 1f);
        crate.AddComponent<RigidBodyComponent>();
        BoxColliderComponent collider = crate.AddComponent<BoxColliderComponent>();

        Step(300);

        Assert.InRange(crate.Transform.Position.Y, -0.02f, 0.02f);
        collider.ColliderScale = new Vector3(1f, 2f, 1f);
        crate.Transform.Position = new Vector3(0f, 3f, 0f);

        Step(180);

        Assert.InRange(crate.Transform.Position.Y, 0.73f, 0.77f);
    }

    [Fact]
    public void CapsuleWithMatchingVisibleBoundsRestsOnGround()
    {
        Ground();
        GameObject capsuleGuy = _scene.CreateObject("CapsuleGuy");
        capsuleGuy.Transform.Position = new Vector3(0f, 2.6f, 0f);
        capsuleGuy.Transform.LocalScale = new Vector3(0.5f, 1.07f, 0.5f);
        capsuleGuy.AddComponent<RigidBodyComponent>();
        CapsuleColliderComponent collider = capsuleGuy.AddComponent<CapsuleColliderComponent>();
        collider.Radius = 1f;
        collider.Height = 2f;

        Step(300);

        float visibleBottom = capsuleGuy.Transform.Position.Y - 1.07f;
        Assert.InRange(visibleBottom, -0.78f, -0.72f);
    }

    [Fact]
    public void CapsuleWithSmallInitialTiltTipsOver()
    {
        Ground();
        GameObject capsuleGuy = _scene.CreateObject("CapsuleGuy");
        capsuleGuy.Transform.Position = new Vector3(0f, 2.6f, 0f);
        capsuleGuy.Transform.LocalScale = new Vector3(0.5f, 1.07f, 0.5f);
        capsuleGuy.Transform.Rotation = Quaternion.FromAxisAngle(Vector3.UnitZ, 0.08f);
        capsuleGuy.AddComponent<RigidBodyComponent>();
        CapsuleColliderComponent collider = capsuleGuy.AddComponent<CapsuleColliderComponent>();
        collider.Radius = 1f;
        collider.Height = 2f;

        Step(300);

        Assert.True(Vector3.Dot(capsuleGuy.Transform.Up, Vector3.Up) < 0.95f);
    }

    [Fact]
    public void BodyWithoutColliderIsNotSimulated()
    {
        GameObject floating = _scene.CreateObject("Floating");
        floating.Transform.Position = new Vector3(0, 5f, 0);
        floating.AddComponent<RigidBodyComponent>();
        Step(60);
        Assert.Equal(5f, floating.Transform.Position.Y);
        Assert.Equal(0, _physics.World.Simulation.Bodies.ActiveSet.Count);
    }

    [Fact]
    public void MeshColliderUsesObjectScaleAndSupportsFallingBodies()
    {
        Vector3[] vertices =
        [
            new(-0.5f, 0f, -0.5f),
            new(0.5f, 0f, -0.5f),
            new(0.5f, 0f, 0.5f),
            new(-0.5f, 0f, 0.5f),
        ];
        uint[] indices = [0, 1, 2, 0, 2, 3];
        _physics.MeshDataResolver = key => key == "plane" ? new PhysicsMeshData(vertices, indices) : null;
        GameObject ground = _scene.CreateObject("MeshGround");
        ground.Transform.LocalScale = new Vector3(4f, 1f, 4f);
        ground.AddComponent<RigidBodyComponent>().Kind = RigidBodyKind.Static;
        ground.AddComponent<MeshColliderComponent>().ResourceKey = "plane";
        GameObject crate = _scene.CreateObject("Crate");
        crate.Transform.Position = new Vector3(1.5f, 2f, 0f);
        crate.AddComponent<RigidBodyComponent>();
        crate.AddComponent<BoxColliderComponent>().Size = new Vector3(0.5f);

        Step(240);

        Assert.InRange(crate.Transform.Position.Y, 0.2f, 0.4f);
        Assert.InRange(crate.Transform.Position.X, 1.49f, 1.51f);
    }

    [Fact]
    public void ColliderWithoutRigidBodyCreatesStaticCollision()
    {
        GameObject ground = _scene.CreateObject("ColliderGround");
        ground.Transform.Position = new Vector3(0f, -0.5f, 0f);
        ground.AddComponent<BoxColliderComponent>().Size = new Vector3(10f, 1f, 10f);
        GameObject crate = _scene.CreateObject("DynamicCrate");
        crate.Transform.Position = new Vector3(0f, 3f, 0f);
        crate.AddComponent<RigidBodyComponent>();
        crate.AddComponent<BoxColliderComponent>().Size = Vector3.One;

        Step(240);

        Assert.InRange(crate.Transform.Position.Y, 0.48f, 0.52f);
        Assert.Equal(1, _physics.World.Simulation.Statics.Count);
    }

    [Fact]
    public void OffsetCompoundBodyKeepsTransformAtAuthoredOrigin()
    {
        GameObject crate = _scene.CreateObject("OffsetBody");
        crate.Transform.Position = new Vector3(2f, 3f, 4f);
        crate.AddComponent<RigidBodyComponent>();
        crate.AddComponent<BoxColliderComponent>().Center = new Vector3(1.5f, 0f, 0f);
        _physics.Update(_time);

        Vector3 centerOfMass = _physics.World.GetBodyPosition(new BepuPhysics.BodyHandle(0));
        Assert.Equal(new Vector3(3.5f, 3f, 4f), centerOfMass);
        Assert.Equal(new Vector3(2f, 3f, 4f), crate.Transform.Position);

        Quaternion rotation = Quaternion.FromAxisAngle(Vector3.UnitY, 0.8f);
        crate.Transform.Rotation = rotation;
        _physics.Update(_time);

        Vector3 rotatedCenterOfMass = _physics.World.GetBodyPosition(new BepuPhysics.BodyHandle(0));
        Vector3 expected = crate.Transform.Position + rotation.Rotate(new Vector3(1.5f, 0f, 0f));
        Assert.True((rotatedCenterOfMass - expected).Length < 0.0001f);
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
