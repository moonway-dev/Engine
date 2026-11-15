using Engine.Mathematics;
using Engine.Physics;
using Xunit;

namespace Engine.Tests.Physics;

public class PhysicsWorldTests
{
    [Fact]
    public void DynamicBody_FallsUnderGravity()
    {
        using PhysicsWorld world = new(new Vector3(0f, -9.81f, 0f));
        var handle = world.AddDynamic(new Vector3(0f, 10f, 0f), Quaternion.Identity, ColliderShape.Box, new Vector3(0.5f, 0.5f, 0.5f), 1f);
        Vector3 start = world.GetBodyPosition(handle);
        Assert.Equal(10f, start.Y);

        for (int i = 0; i < 60; i++)
            world.Step(1f / 60f);

        Vector3 afterOneSecond = world.GetBodyPosition(handle);
        Assert.True(afterOneSecond.Y < 6f, $"Body should fall, but Y is {afterOneSecond.Y}");
    }

    [Fact]
    public void DynamicBody_RestsOnStaticGround()
    {
        using PhysicsWorld world = new(new Vector3(0f, -9.81f, 0f));
        world.AddStatic(new Vector3(0f, 0f, 0f), Quaternion.Identity, ColliderShape.Box, new Vector3(10f, 0.5f, 10f));
        var handle = world.AddDynamic(new Vector3(0f, 3f, 0f), Quaternion.Identity, ColliderShape.Box, new Vector3(0.5f, 0.5f, 0.5f), 1f);

        for (int i = 0; i < 240; i++)
            world.Step(1f / 60f);

        Vector3 position = world.GetBodyPosition(handle);
        Assert.True(position.Y > 0.2f && position.Y < 1.2f, $"Body should rest near ground, but Y is {position.Y}");
    }

    [Fact]
    public void SphereBody_RollsAndSettles()
    {
        using PhysicsWorld world = new(new Vector3(0f, -9.81f, 0f));
        world.AddStatic(new Vector3(0f, 0f, 0f), Quaternion.Identity, ColliderShape.Box, new Vector3(20f, 0.5f, 20f));
        var handle = world.AddDynamic(new Vector3(0f, 2f, 0f), Quaternion.Identity, ColliderShape.Sphere, new Vector3(0.4f, 0.4f, 0.4f), 2f);

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
        world.AddStatic(new Vector3(0f, 0f, 0f), Quaternion.Identity, ColliderShape.Box, new Vector3(10f, 0.5f, 10f));
        var first = world.AddDynamic(new Vector3(0f, 1.5f, 0f), Quaternion.Identity, ColliderShape.Box, new Vector3(0.5f, 0.5f, 0.5f), 1f);
        var second = world.AddDynamic(new Vector3(0f, 2.6f, 0f), Quaternion.Identity, ColliderShape.Box, new Vector3(0.5f, 0.5f, 0.5f), 1f);

        for (int i = 0; i < 300; i++)
            world.Step(1f / 60f);

        float firstY = world.GetBodyPosition(first).Y;
        float secondY = world.GetBodyPosition(second).Y;
        Assert.True(secondY > firstY + 0.4f, $"Boxes should stack, but Ys are {firstY} and {secondY}");
    }
}
