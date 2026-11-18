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
