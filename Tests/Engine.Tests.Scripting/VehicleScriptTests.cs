using System.Reflection;
using Engine.Mathematics;
using Engine.Physics;
using Engine.Scene;
using Engine.Scripting;
using Xunit;

namespace Engine.Tests.Scripting;

public sealed class VehicleScriptTests : IDisposable
{
    private readonly Scene.Scene _scene = new("Vehicle script tests");

    private T Attach<T>(GameObject owner) where T : ScriptBehaviour, new()
    {
        Script script = owner.AddComponent<Script>();
        T behaviour = new();
        typeof(ScriptBehaviour).GetProperty(nameof(ScriptBehaviour.Component))!.SetValue(behaviour, script);
        return behaviour;
    }

    private static void SetTime(float delta, float fixedDelta = 1f / 60f)
    {
        typeof(ScriptTime).GetProperty(nameof(ScriptTime.DeltaTime))!.SetValue(null, delta);
        typeof(ScriptTime).GetProperty(nameof(ScriptTime.FixedDeltaTime))!.SetValue(null, fixedDelta);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(144)]
    public void CameraTranslationTracksRenderPoseWithoutFrameDependentLag(int fps)
    {
        GameObject target = _scene.CreateObject("Car");
        GameObject owner = _scene.CreateObject("Camera");
        owner.AddComponent<Engine.Scene.Camera>();
        ChaseCamera camera = Attach<ChaseCamera>(owner);
        camera.Target = new EntityReference(target.Id);
        Vector3 position = Vector3.Zero;
        ScriptPhysics.InterpolatedPoseGetter = _ => (position, Quaternion.Identity);
        ScriptPhysics.LinearVelocityGetter = _ => new Vector3(20f, 0f, 0f);
        ScriptPhysics.SphereCaster = (_, _, _, _, _) => null;
        camera.OnStart();
        camera.OnEnable();
        SetTime(1f / fps);
        camera.OnUpdate();
        Vector3 offset = owner.Transform.Position - position;
        for (int frame = 1; frame <= fps * 2; frame++)
        {
            position = new Vector3(20f * frame / fps, 0f, 0f);
            camera.OnUpdate();
            Assert.InRange(Vector3.Distance(owner.Transform.Position - position, offset), 0f, 1e-4f);
        }
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(144)]
    public void CameraOrbitReturnUsesElapsedTimeInsteadOfFrameCount(int fps)
    {
        GameObject target = _scene.CreateObject("Car");
        GameObject owner = _scene.CreateObject("Camera");
        owner.AddComponent<Engine.Scene.Camera>();
        ChaseCamera camera = Attach<ChaseCamera>(owner);
        camera.Target = new EntityReference(target.Id);
        camera.OrbitIdleDelay = 0.3f;
        ScriptPhysics.InterpolatedPoseGetter = _ => (Vector3.Zero, Quaternion.Identity);
        camera.OnStart();
        typeof(ChaseCamera).GetField("_orbitYaw", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(camera, 1f);
        SetTime(1f / fps);
        for (int frame = 0; frame < fps * 2; frame++)
            camera.OnUpdate();
        float yaw = (float)typeof(ChaseCamera).GetField("_orbitYaw", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(camera)!;
        Assert.InRange(MathF.Abs(yaw - MathF.Exp(-camera.OrbitReturnSpeed * 1.7f)), 0f, 1e-4f);
    }

    [Fact]
    public void CameraCollisionUsesSphereRadiusAndIgnoresVehicle()
    {
        GameObject target = _scene.CreateObject("Car");
        GameObject owner = _scene.CreateObject("Camera");
        owner.AddComponent<Engine.Scene.Camera>();
        ChaseCamera camera = Attach<ChaseCamera>(owner);
        camera.Target = new EntityReference(target.Id);
        camera.CollisionMargin = 0.1f;
        ScriptPhysics.InterpolatedPoseGetter = _ => (Vector3.Zero, Quaternion.Identity);
        bool queried = false;
        ScriptPhysics.SphereCaster = (origin, direction, distance, radius, ignore) =>
        {
            queried = true;
            Assert.Equal(camera.CollisionRadius, radius);
            Assert.Same(target, ignore);
            return new ScriptRayHit(origin + direction * 3f, -direction, 3f);
        };
        camera.OnStart();
        SetTime(1f / 60f);
        camera.OnUpdate();
        Assert.True(queried);
        Assert.InRange(Vector3.Distance(owner.Transform.Position, camera.LookOffset), 2.8999f, 2.9001f);
    }

    [Fact]
    public void VehicleForcesRunOnlyAtFixedRate()
    {
        GameObject owner = _scene.CreateObject("Car");
        GameObject child = _scene.CreateObject("Wheel");
        child.SetParent(owner);
        WheelCollider wheel = child.AddComponent<WheelCollider>();
        wheel.LocalAnchor = new Vector3(0.7f, -0.2f, 1.5f);
        CarController controller = Attach<CarController>(owner);
        List<Vector3> impulses = [];
        ScriptPhysics.BodyChecker = _ => true;
        ScriptPhysics.LinearVelocityGetter = _ => new Vector3(20f, 0f, 0f);
        ScriptPhysics.ImpulseApplier = (_, impulse) => impulses.Add(impulse);
        controller.OnStart();
        SetTime(1f / 144f, 0.02f);
        for (int frame = 0; frame < 10; frame++)
            controller.OnUpdate();
        Assert.Empty(impulses);
        typeof(CarController).GetField("_throttle", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(controller, 1f);
        controller.OnFixedUpdate();
        Vector3 impulse = Assert.Single(impulses);
        Assert.InRange(impulse.X, -5.201f, -5.199f);
        Assert.InRange(impulse.Y, -17.601f, -17.599f);
        Assert.Equal(controller.MotorTorque, wheel.MotorTorque);
        controller.OnDisable();
        Assert.Equal(0f, wheel.MotorTorque);
    }

    public void Dispose()
    {
        ScriptPhysics.InterpolatedPoseGetter = null;
        ScriptPhysics.LinearVelocityGetter = null;
        ScriptPhysics.SphereCaster = null;
        ScriptPhysics.ImpulseApplier = null;
        ScriptPhysics.BodyChecker = null;
        SetTime(1f / 60f);
    }
}
