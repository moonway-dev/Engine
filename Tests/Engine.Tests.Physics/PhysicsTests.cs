using BepuPhysics;
using Engine.Mathematics;
using Engine.Physics;
using Engine.Scene;
using Xunit;

namespace Engine.Tests.Physics;

public class PhysicsWorldTests
{
    private static PhysicsColliderSpec BoxSpec(Vector3 size) => new(
        PhysicsColliderKind.Box, Vector3.Zero, Quaternion.Identity, size, 0f, string.Empty, null);

    private static PhysicsColliderSpec SphereSpec(float radius) => new(
        PhysicsColliderKind.Sphere, Vector3.Zero, Quaternion.Identity, Vector3.Zero, radius, string.Empty, null);

    [Fact]
    public void DynamicBody_FallsUnderGravity()
    {
        using PhysicsWorld world = new(new Vector3(0f, -9.81f, 0f));
        var handle = world.AddDynamic(new Vector3(0f, 10f, 0f), Quaternion.Identity, [BoxSpec(new Vector3(0.5f, 0.5f, 0.5f))], 1f);
        Vector3 start = world.GetBodyPosition(handle);
        Assert.Equal(10f, start.Y);

        for (int i = 0; i < 60; i++)
            world.Step(1f / 60f);

        Vector3 afterOneSecond = world.GetBodyPosition(handle);
        Assert.InRange(afterOneSecond.Y, 4.9f, 5.4f);
    }

    [Fact]
    public void SphereFreeFallMatchesGravityAndExpectedVelocity()
    {
        using PhysicsWorld world = new(new Vector3(0f, -9.81f, 0f));
        var handle = world.AddDynamic(new Vector3(0f, 10f, 0f), Quaternion.Identity, [SphereSpec(0.4f)], 1f);

        for (int i = 0; i < 60; i++)
            world.Step(1f / 60f);

        Assert.InRange(world.GetBodyPosition(handle).Y, 4.8f, 5.2f);
        Assert.InRange(world.GetBodyLinearVelocity(handle).Y, -9.7f, -9.3f);
    }

    [Fact]
    public void GravityIsConsistentAcrossSimulationStepSizes()
    {
        using PhysicsWorld standardStepWorld = new(new Vector3(0f, -9.81f, 0f));
        using PhysicsWorld smallerStepWorld = new(new Vector3(0f, -9.81f, 0f));
        var standardStepBody = standardStepWorld.AddDynamic(
            new Vector3(0f, 10f, 0f), Quaternion.Identity, [BoxSpec(new Vector3(0.5f))], 1f);
        var smallerStepBody = smallerStepWorld.AddDynamic(
            new Vector3(0f, 10f, 0f), Quaternion.Identity, [BoxSpec(new Vector3(0.5f))], 1f);

        for (int i = 0; i < 60; i++)
            standardStepWorld.Step(1f / 60f);
        for (int i = 0; i < 120; i++)
            smallerStepWorld.Step(1f / 120f);

        float standardStepY = standardStepWorld.GetBodyPosition(standardStepBody).Y;
        float smallerStepY = smallerStepWorld.GetBodyPosition(smallerStepBody).Y;
        Assert.InRange(MathF.Abs(standardStepY - smallerStepY), 0f, 0.05f);
    }

    [Fact]
    public void ObjWoundMeshSupportsBodyFromOutside()
    {
        using PhysicsWorld world = new(new Vector3(0f, -9.81f, 0f));
        Vector3[] vertices =
        [
            new(-0.7f, -0.7f, -0.7f),
            new(0.7f, -0.7f, -0.7f),
            new(0.7f, 0.7f, -0.7f),
            new(-0.7f, 0.7f, -0.7f),
            new(-0.7f, -0.7f, 0.7f),
            new(0.7f, -0.7f, 0.7f),
            new(0.7f, 0.7f, 0.7f),
            new(-0.7f, 0.7f, 0.7f),
        ];
        uint[] indices =
        [
            0, 1, 2, 0, 2, 3,
            1, 5, 6, 1, 6, 2,
            5, 4, 7, 5, 7, 6,
            4, 0, 3, 4, 3, 7,
            2, 6, 7, 2, 7, 3,
            4, 5, 1, 4, 1, 0,
        ];
        PhysicsMeshData mesh = new(vertices, indices);
        PhysicsColliderSpec meshSpec = new(
            PhysicsColliderKind.Mesh, Vector3.Zero, Quaternion.Identity, Vector3.One, 0f, "cube", mesh);
        world.AddStatic(new Vector3(0f, 0.7f, 0f), Quaternion.Identity, [meshSpec]);
        var body = world.AddDynamic(new Vector3(0f, 3f, 0f), Quaternion.Identity, [BoxSpec(new Vector3(0.5f))], 1f);

        for (int i = 0; i < 180; i++)
            world.Step(1f / 60f);

        Assert.InRange(world.GetBodyPosition(body).Y, 1.6f, 1.7f);
    }

    [Fact]
    public void DynamicBody_RestsOnStaticGround()
    {
        using PhysicsWorld world = new(new Vector3(0f, -9.81f, 0f));
        world.AddStatic(new Vector3(0f, 0f, 0f), Quaternion.Identity, [BoxSpec(new Vector3(10f, 0.5f, 10f))]);
        var handle = world.AddDynamic(new Vector3(0f, 3f, 0f), Quaternion.Identity, [BoxSpec(new Vector3(0.5f, 0.5f, 0.5f))], 1f);

        for (int i = 0; i < 240; i++)
            world.Step(1f / 60f);

        Vector3 position = world.GetBodyPosition(handle);
        Assert.True(position.Y > 0.2f && position.Y < 1.2f, $"Body should rest near ground, but Y is {position.Y}");
    }

    [Fact]
    public void SphereBody_RollsAndSettles()
    {
        using PhysicsWorld world = new(new Vector3(0f, -9.81f, 0f));
        world.AddStatic(new Vector3(0f, 0f, 0f), Quaternion.Identity, [BoxSpec(new Vector3(20f, 0.5f, 20f))]);
        var handle = world.AddDynamic(new Vector3(0f, 2f, 0f), Quaternion.Identity, [SphereSpec(0.4f)], 2f);

        for (int i = 0; i < 300; i++)
            world.Step(1f / 60f);

        Vector3 position = world.GetBodyPosition(handle);
        Assert.True(position.Y < 1.5f, $"Sphere should settle on ground, but Y is {position.Y}");
        Assert.True(position.Y > 0f, $"Sphere should not fall through, but Y is {position.Y}");
    }

    [Fact]
    public void TwoDroppingBoxes_DoNotInterpenetrate()
    {
        using PhysicsWorld world = new(new Vector3(0f, -9.81f, 0f));
        world.AddStatic(new Vector3(0f, 0f, 0f), Quaternion.Identity, [BoxSpec(new Vector3(10f, 0.5f, 10f))]);
        var first = world.AddDynamic(new Vector3(0f, 1.5f, 0f), Quaternion.Identity, [BoxSpec(new Vector3(0.5f, 0.5f, 0.5f))], 1f);
        var second = world.AddDynamic(new Vector3(0f, 2.6f, 0f), Quaternion.Identity, [BoxSpec(new Vector3(0.5f, 0.5f, 0.5f))], 1f);

        for (int i = 0; i < 60; i++)
            world.Step(1f / 60f);

        Assert.InRange(world.GetBodyPosition(first).Y, 0.45f, 0.65f);
        Assert.InRange(world.GetBodyPosition(second).Y, 0.95f, 1.2f);

        for (int i = 60; i < 300; i++)
            world.Step(1f / 60f);

        float firstY = world.GetBodyPosition(first).Y;
        float secondY = world.GetBodyPosition(second).Y;
        Assert.InRange(firstY, 0.45f, 0.65f);
        Assert.InRange(secondY, 0.95f, 1.2f);
    }

    [Fact]
    public void CompoundBody_RestsOnGround()
    {
        using PhysicsWorld world = new(new Vector3(0f, -9.81f, 0f));
        world.AddStatic(new Vector3(0f, 0f, 0f), Quaternion.Identity, [BoxSpec(new Vector3(10f, 0.5f, 10f))]);
        PhysicsColliderSpec first = new(
            PhysicsColliderKind.Box, new Vector3(-0.4f, 0f, 0f), Quaternion.Identity, new Vector3(0.5f, 0.5f, 0.5f), 0f, string.Empty, null);
        PhysicsColliderSpec second = new(
            PhysicsColliderKind.Sphere, new Vector3(0.4f, 0f, 0f), Quaternion.Identity, Vector3.Zero, 0.25f, string.Empty, null);
        var handle = world.AddDynamic(new Vector3(0f, 3f, 0f), Quaternion.Identity, [first, second], 2f);

        for (int i = 0; i < 240; i++)
            world.Step(1f / 60f);

        Vector3 position = world.GetBodyPosition(handle);
        Assert.True(position.Y > 0.2f && position.Y < 1.2f, $"Compound body should rest near ground, but Y is {position.Y}");
    }
}

public sealed class PhysicsWorldRayCastTests
{
    private static PhysicsColliderSpec BoxSpec(Vector3 size) => new(
        PhysicsColliderKind.Box, Vector3.Zero, Quaternion.Identity, size, 0f, string.Empty, null);

    [Fact]
    public void RayCast_HitsStaticBoxTopSurface()
    {
        using PhysicsWorld world = new(new Vector3(0f, -9.81f, 0f));
        world.AddStatic(new Vector3(0f, -0.25f, 0f), Quaternion.Identity, [BoxSpec(new Vector3(8f, 0.5f, 8f))]);

        Assert.True(world.RayCast(new Vector3(0f, 5f, 0f), Vector3.Down, 10f, out PhysicsRayHit hit));
        Assert.InRange(hit.Distance, 4.9f, 5.1f);
        Assert.InRange(hit.Normal.Y, 0.99f, 1.01f);
        Assert.False(world.RayCast(new Vector3(20f, 5f, 0f), Vector3.Down, 10f, out PhysicsRayHit miss));
    }

    [Fact]
    public void RayCast_IgnoresExcludedBody()
    {
        using PhysicsWorld world = new(new Vector3(0f, -9.81f, 0f));
        StaticHandle floor = world.AddStatic(new Vector3(0f, -0.25f, 0f), Quaternion.Identity, [BoxSpec(new Vector3(8f, 0.5f, 8f))]);
        BodyHandle crate = world.AddDynamic(new Vector3(0f, 2f, 0f), Quaternion.Identity, [BoxSpec(new Vector3(1f, 1f, 1f))], 1f);

        Assert.True(world.RayCast(new Vector3(0f, 5f, 0f), Vector3.Down, 10f, out PhysicsRayHit hit));
        Assert.InRange(hit.Distance, 2.4f, 2.6f);

        Assert.True(world.RayCast(new Vector3(0f, 5f, 0f), Vector3.Down, 10f, out PhysicsRayHit ignored, crate));
        Assert.InRange(ignored.Distance, 4.9f, 5.1f);
        _ = floor;
    }

    [Fact]
    public void WheelSphereProbe_TouchesBumpBeforeCenterCrossesItsEdge()
    {
        using PhysicsWorld world = new(new Vector3(0f, -9.81f, 0f));
        world.AddStatic(new Vector3(0f, -0.5f, 0f), Quaternion.Identity, [BoxSpec(new Vector3(40f, 1f, 40f))]);
        world.AddStatic(new Vector3(-0.5f, 0.1f, 0f), Quaternion.Identity, [BoxSpec(new Vector3(1f, 0.2f, 1f))]);

        const float Radius = 0.6f;
        const float Suspension = 0.3f;
        const float AnchorHeight = Radius + Suspension;
        Vector3 down = new(0f, -1f, 0f);
        float wheelCenterX = -1.2f;

        float centerPenetration = 0f;
        if (world.RayCast(new Vector3(wheelCenterX, AnchorHeight, 0f), down, Suspension + Radius + 0.05f, out PhysicsRayHit center))
            centerPenetration = Suspension - (center.Distance - Radius);

        bool sphereHit = world.SweepSphere(new Vector3(wheelCenterX, AnchorHeight, 0f), down, Suspension + 0.05f, Radius, out PhysicsSweepHit sweep);
        float sphereCompression = sphereHit ? MathF.Max(0f, Suspension - sweep.Distance) : 0f;

        Assert.InRange(centerPenetration, -1e-4f, 1e-4f);
        Assert.True(sphereHit, "The sphere sweep should touch the bump while the center column still sees only the floor.");
        Assert.True(sphereCompression > 0.03f,
            $"Sphere contact should start before the center crosses the edge, but compression is {sphereCompression:F3}");
        Assert.True(sphereCompression <= Suspension + 1e-3f,
            $"Sphere compression {sphereCompression:F3} must stay within the suspension travel {Suspension}");
        Assert.InRange(sweep.Normal.Y, 0.2f, 1.01f);
    }

    [Fact]
    public void BodySettingsApplyGravityDampingAndAxisConstraints()
    {
        using PhysicsWorld world = new(new Vector3(0f, -9.81f, 0f));
        BodyHandle handle = world.AddDynamic(new Vector3(0f, 5f, 0f), Quaternion.Identity, [BoxSpec(Vector3.One)], 1f);
        world.ConfigureBody(handle, new RigidbodySettings(
            LinearDamping: 1f,
            AngularDamping: 0f,
            UseGravity: false,
            DetectCollisions: true,
            Constraints: RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezeRotation));
        world.SetBodyVelocity(handle, new Vector3(3f, 0f, 0f));

        for (int i = 0; i < 60; i++)
            world.Step(1f / 60f);

        Assert.Equal(0f, world.GetBodyPosition(handle).X, 4);
        Assert.Equal(5f, world.GetBodyPosition(handle).Y, 4);
        Assert.Equal(0f, world.GetBodyLinearVelocity(handle).X, 4);
        Assert.Equal(Quaternion.Identity, world.GetBodyRotation(handle));
        Assert.Equal(0f, world.GetBodyAngularVelocity(handle).Length, 4);
        Assert.Equal(1f, world.GetBodySettings(handle).LinearDamping);
    }

    [Fact]
    public void PartialRotationConstraintAllowsOnlyUnfrozenLocalAxis()
    {
        using PhysicsWorld world = new(new Vector3(0f, 0f, 0f));
        Quaternion initialRotation = Quaternion.FromAxisAngle(Vector3.UnitY, MathF.PI * 0.5f);
        BodyHandle handle = world.AddDynamic(Vector3.Zero, initialRotation, [BoxSpec(Vector3.One)], 1f);
        world.ConfigureBody(handle, new RigidbodySettings(
            LinearDamping: 0f,
            AngularDamping: 0f,
            UseGravity: false,
            Constraints: RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ));
        Vector3 worldAngularVelocity = initialRotation.Rotate(Vector3.UnitY);
        world.SetBodyVelocity(handle, Vector3.Zero, worldAngularVelocity);

        for (int i = 0; i < 60; i++)
            world.Step(1f / 60f);

        Quaternion expected = initialRotation * Quaternion.FromAxisAngle(Vector3.UnitY, 1f);
        Quaternion actual = world.GetBodyRotation(handle);
        Assert.InRange(Quaternion.Angle(expected, actual), 0f, 0.01f);
        Vector3 localAngularVelocity = actual.Inverse.Rotate(world.GetBodyAngularVelocity(handle));
        Assert.InRange(MathF.Abs(localAngularVelocity.X), 0f, 0.001f);
        Assert.InRange(localAngularVelocity.Y, 0.99f, 1.01f);
        Assert.InRange(MathF.Abs(localAngularVelocity.Z), 0f, 0.001f);
    }

    [Fact]
    public void PositionConstraintLocksOnlySelectedWorldAxis()
    {
        using PhysicsWorld world = new(new Vector3(0f, -9.81f, 0f));
        BodyHandle handle = world.AddDynamic(new Vector3(2f, 5f, 3f), Quaternion.Identity, [BoxSpec(Vector3.One)], 1f);
        world.ConfigureBody(handle, new RigidbodySettings(
            LinearDamping: 0f,
            AngularDamping: 0f,
            UseGravity: true,
            Constraints: RigidbodyConstraints.FreezePositionX));
        world.SetBodyVelocity(handle, new Vector3(4f, 0f, 2f));

        for (int i = 0; i < 60; i++)
            world.Step(1f / 60f);

        Vector3 position = world.GetBodyPosition(handle);
        Assert.Equal(2f, position.X, 4);
        Assert.InRange(position.Y, -0.02f, 0.02f);
        Assert.InRange(position.Z, 4.9f, 5.1f);
    }

    [Fact]
    public void BodyCanDisableGravityAndKeepLinearVelocityWithoutDamping()
    {
        using PhysicsWorld world = new(new Vector3(0f, -9.81f, 0f));
        BodyHandle handle = world.AddDynamic(new Vector3(0f, 5f, 0f), Quaternion.Identity, [BoxSpec(Vector3.One)], 1f);
        world.ConfigureBody(handle, new RigidbodySettings(0f, 0f, UseGravity: false));
        world.SetBodyVelocity(handle, new Vector3(2f, 0f, 0f));

        for (int i = 0; i < 60; i++)
            world.Step(1f / 60f);

        Assert.InRange(world.GetBodyPosition(handle).X, 1.99f, 2.01f);
        Assert.Equal(5f, world.GetBodyPosition(handle).Y, 4);
        Assert.Equal(2f, world.GetBodyLinearVelocity(handle).X, 4);
    }

    [Fact]
    public void CollisionDetectionSettingsAreStoredOnBody()
    {
        using PhysicsWorld world = new(new Vector3(0f, -9.81f, 0f));
        BodyHandle handle = world.AddDynamic(new Vector3(0f, 5f, 0f), Quaternion.Identity, [BoxSpec(Vector3.One)], 1f);
        world.ConfigureBody(handle, new RigidbodySettings(CollisionDetection: RigidbodyCollisionDetection.Continuous));

        Assert.Equal(RigidbodyCollisionDetection.Continuous, world.GetBodySettings(handle).CollisionDetection);
    }

    [Fact]
    public void ContinuousCollisionPreventsFastBodyPassingThroughThinWall()
    {
        using PhysicsWorld world = new(Vector3.Zero);
        PhysicsColliderSpec wall = new(
            PhysicsColliderKind.Box, Vector3.Zero, Quaternion.Identity, new Vector3(0.1f, 10f, 10f), 0f, string.Empty, null);
        world.AddStatic(Vector3.Zero, Quaternion.Identity, [wall]);
        PhysicsColliderSpec sphere = new(
            PhysicsColliderKind.Sphere, Vector3.Zero, Quaternion.Identity, Vector3.Zero, 0.25f, string.Empty, null);
        BodyHandle handle = world.AddDynamic(new Vector3(-5f, 0f, 0f), Quaternion.Identity, [sphere], 1f);
        world.ConfigureBody(handle, new RigidbodySettings(
            LinearDamping: 0f,
            AngularDamping: 0f,
            UseGravity: false,
            CollisionDetection: RigidbodyCollisionDetection.Continuous));
        world.SetBodyVelocity(handle, new Vector3(600f, 0f, 0f));

        world.Step(1f / 60f);

        Assert.True(world.GetBodyPosition(handle).X < 0f);
        Assert.True(world.GetBodyLinearVelocity(handle).X < 600f);
    }

    [Fact]
    public void ApplyImpulse_ChangesLinearVelocityByImpulseOverMass()
    {
        using PhysicsWorld world = new(new Vector3(0f, -9.81f, 0f));
        BodyHandle handle = world.AddDynamic(new Vector3(0f, 5f, 0f), Quaternion.Identity, [BoxSpec(new Vector3(1f, 1f, 1f))], 2f);

        world.ApplyImpulse(handle, new Vector3(0f, 10f, 0f));
        Vector3 velocity = world.GetBodyLinearVelocity(handle);
        Assert.InRange(velocity.Y, 4.9f, 5.1f);
        Assert.Equal(2f, world.GetBodyMass(handle), 2f);
    }

    [Fact]
    public void ApplyAngularImpulse_SpinsBody()
    {
        using PhysicsWorld world = new(new Vector3(0f, -9.81f, 0f));
        BodyHandle handle = world.AddDynamic(new Vector3(0f, 5f, 0f), Quaternion.Identity, [BoxSpec(new Vector3(1f, 1f, 1f))], 1f);

        world.ApplyAngularImpulse(handle, new Vector3(0f, 2f, 0f));
        Assert.NotEqual(Vector3.Zero, world.GetBodyAngularVelocity(handle));
    }
}

public sealed class WheelSuspensionProbeTests
{
    private static PhysicsColliderSpec BoxSpec(Vector3 size) => new(
        PhysicsColliderKind.Box, Vector3.Zero, Quaternion.Identity, size, 0f, string.Empty, null);

    private const float Radius = 0.6f;
    private const float Suspension = 0.42f;

    private static PhysicsWorld CreateGroundWorld()
    {
        PhysicsWorld world = new(new Vector3(0f, -9.81f, 0f));
        world.AddStatic(new Vector3(0f, -1f, 0f), Quaternion.Identity, [BoxSpec(new Vector3(40f, 2f, 40f))]);
        return world;
    }

    private static SuspensionProbeState Probe(PhysicsWorld world, Vector3 anchor, out float travel, out Vector3 normal)
    {
        return WheelCollider.ProbeSupport(world, null, anchor, Vector3.Up, Suspension, Radius, out travel, out normal, out _);
    }

    [Fact]
    public void Probe_FlatGroundReturnsSupportAtRestTravel()
    {
        using PhysicsWorld world = CreateGroundWorld();
        SuspensionProbeState state = Probe(world, new Vector3(0f, 0.9f, 0f), out float travel, out Vector3 normal);
        Assert.Equal(SuspensionProbeState.Supported, state);
        Assert.InRange(travel, 0.29f, 0.31f);
        Assert.InRange(normal.Y, 0.99f, 1.01f);
    }

    [Fact]
    public void Probe_AirborneFarAboveGround()
    {
        using PhysicsWorld world = CreateGroundWorld();
        SuspensionProbeState state = Probe(world, new Vector3(0f, 2.5f, 0f), out _, out _);
        Assert.Equal(SuspensionProbeState.Airborne, state);
    }

    [Fact]
    public void Probe_OnBumpTopUsesBumpSurface()
    {
        using PhysicsWorld world = CreateGroundWorld();
        world.AddStatic(new Vector3(-0.5f, 0.1f, 0f), Quaternion.Identity, [BoxSpec(new Vector3(1f, 0.2f, 1f))]);
        SuspensionProbeState state = Probe(world, new Vector3(-0.5f, 1.0f, 0f), out float travel, out Vector3 normal);
        Assert.Equal(SuspensionProbeState.Supported, state);
        Assert.InRange(travel, 0.19f, 0.21f);
        Assert.InRange(normal.Y, 0.99f, 1.01f);
    }

    [Fact]
    public void Probe_ApproachingBumpCornerGivesSmoothCornerSupport()
    {
        using PhysicsWorld world = CreateGroundWorld();
        world.AddStatic(new Vector3(-0.5f, 0.1f, 0f), Quaternion.Identity, [BoxSpec(new Vector3(1f, 0.2f, 1f))]);
        SuspensionProbeState state = Probe(world, new Vector3(-1.3f, 0.9f, 0f), out float travel, out Vector3 normal);
        Assert.Equal(SuspensionProbeState.Supported, state);
        Assert.InRange(travel, 0.14f, 0.27f);
        Assert.InRange(normal.Y, 0.6f, 0.95f);
    }

    [Fact]
    public void Probe_BumpApproachTravelIsContinuousAcrossTheEdge()
    {
        using PhysicsWorld world = CreateGroundWorld();
        world.AddStatic(new Vector3(-0.5f, 0.1f, 0f), Quaternion.Identity, [BoxSpec(new Vector3(1f, 0.2f, 1f))]);
        float previous = 0.3f;
        for (float x = -1.8f; x <= -0.6f; x += 0.02f)
        {
            SuspensionProbeState state = Probe(world, new Vector3(x, 0.9f, 0f), out float travel, out _);
            Assert.Equal(SuspensionProbeState.Supported, state);
            Assert.True(MathF.Abs(travel - previous) < 0.05f,
                $"Travel jumped from {previous:F3} to {travel:F3} at x={x:F2}");
            previous = travel;
        }
    }

    [Fact]
    public void Probe_WallAheadStillSupportedByGround()
    {
        using PhysicsWorld world = CreateGroundWorld();
        world.AddStatic(new Vector3(5f, 0.9f, 0f), Quaternion.Identity, [BoxSpec(new Vector3(1.2f, 1.8f, 1.2f))]);
        SuspensionProbeState state = Probe(world, new Vector3(4.0f, 0.9f, 0f), out float travel, out Vector3 normal);
        Assert.Equal(SuspensionProbeState.Supported, state);
        Assert.InRange(travel, 0.29f, 0.31f);
        Assert.InRange(normal.Y, 0.99f, 1.01f);
    }

    [Fact]
    public void Probe_HangingBesideTallWallOverVoidIsBlocked()
    {
        using PhysicsWorld world = new(new Vector3(0f, -9.81f, 0f));
        world.AddStatic(new Vector3(5f, 1.5f, 0f), Quaternion.Identity, [BoxSpec(new Vector3(1.2f, 3f, 1.2f))]);
        SuspensionProbeState state = Probe(world, new Vector3(4.0f, 2.0f, 0f), out _, out _);
        Assert.Equal(SuspensionProbeState.Blocked, state);
    }

    [Fact]
    public void Probe_NextToWallStillSupportedByGround()
    {
        using PhysicsWorld world = CreateGroundWorld();
        world.AddStatic(new Vector3(5f, 0.9f, 0f), Quaternion.Identity, [BoxSpec(new Vector3(1.2f, 1.8f, 1.2f))]);
        SuspensionProbeState state = Probe(world, new Vector3(3.4f, 0.9f, 0f), out float travel, out Vector3 normal);
        Assert.Equal(SuspensionProbeState.Supported, state);
        Assert.InRange(travel, 0.29f, 0.31f);
        Assert.InRange(normal.Y, 0.99f, 1.01f);
    }
}
