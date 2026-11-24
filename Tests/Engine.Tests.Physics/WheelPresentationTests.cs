using Engine.Core;
using Engine.Mathematics;
using Engine.Physics;
using Engine.Scene;
using Xunit;

namespace Engine.Tests.Physics;

public sealed class WheelPresentationTests
{
    [Fact]
    public void GizmoRestCenterMatchesNeutralAnchorWithoutInventingScale()
    {
        Scene.Scene scene = new("Wheel geometry");
        GameObject chassis = scene.CreateObject("Car");
        chassis.AddComponent<Rigidbody>();
        chassis.Transform.Position = new Vector3(3f, 2f, -4f);
        chassis.Transform.Rotation = Quaternion.FromAxisAngle(Vector3.Up, 0.6f);
        chassis.Transform.LocalScale = new Vector3(2f, 3f, 4f);
        GameObject child = scene.CreateObject("Wheel");
        child.SetParent(chassis);
        WheelCollider wheel = child.AddComponent<WheelCollider>();
        wheel.LocalAnchor = new Vector3(0.735f, -0.2f, 1.58f);
        wheel.SuspensionDistance = 0.55f;
        wheel.Frequency = 0.92f;
        wheel.GizmoGravityMagnitude = 4f;
        var geometry = wheel.GizmoGeometry();
        Vector3 expected = chassis.Transform.Position + chassis.Transform.Rotation.Rotate(wheel.LocalAnchor);
        Assert.InRange(Vector3.Distance(expected, geometry.Center), 0f, 1e-5f);
        Vector3 top = WheelCollider.SuspensionTopWorld(chassis.Transform.Position, chassis.Transform.Rotation,
            wheel.LocalAnchor, wheel.SuspensionDistance, wheel.Frequency, 4f);
        Assert.Equal(top, geometry.Top);
        Assert.InRange(Vector3.Distance(geometry.Top, geometry.End), 0.5499f, 0.5501f);
    }

    [Fact]
    public void RenderWheelPoseDoesNotWriteSimulationTransformAndGizmoUsesIt()
    {
        Scene.Scene scene = new("Wheel presentation");
        GameObject chassis = scene.CreateObject("Car");
        chassis.AddComponent<Rigidbody>();
        GameObject child = scene.CreateObject("Wheel");
        child.SetParent(chassis);
        child.Transform.LocalPosition = new Vector3(0.7f, -0.2f, 1f);
        WheelCollider wheel = child.AddComponent<WheelCollider>();
        Vector3 simulationPosition = child.Transform.Position;
        Quaternion simulationRotation = child.Transform.Rotation;
        Vector3 anchor = new(2f, 3f, 4f);
        wheel.UpdateVisualOnly(child.Transform, Quaternion.Identity, anchor, Vector3.Down, 0.3f,
            Vector3.Up, Vector3.Forward, 0.4f, 1.7f, renderOnly: true);
        wheel.HasVisual = true;
        Assert.Equal(simulationPosition, child.Transform.Position);
        Assert.Equal(simulationRotation, child.Transform.Rotation);
        var geometry = wheel.GizmoGeometry();
        Assert.Equal(anchor + Vector3.Down * 0.3f, geometry.Center);
        child.Transform.RenderWorldMatrix.Decompose(out _, out Quaternion rotation, out _);
        Assert.Equal(rotation, geometry.Rotation);
    }

    [Fact]
    public void WheelFindsChassisThroughIntermediateObjects()
    {
        Scene.Scene scene = new("Nested wheel");
        GameObject chassis = scene.CreateObject("Car");
        chassis.AddComponent<Rigidbody>();
        GameObject axle = scene.CreateObject("Axle");
        axle.SetParent(chassis);
        GameObject child = scene.CreateObject("Wheel");
        child.SetParent(axle);
        WheelCollider wheel = child.AddComponent<WheelCollider>();
        Assert.Same(chassis, wheel.ResolveChassis());
    }

    [Fact]
    public void StandardComponentsUseShortNamesAndAcceptLegacyKeys()
    {
        ComponentRegistry registry = new();
        registry.Register(() => new Rigidbody());
        registry.Register(() => new WheelCollider());
        registry.Register(() => new Engine.Scene.Camera());
        registry.Register(() => new Engine.Scene.Light());
        Assert.Equal("Rigidbody", typeof(Rigidbody).Name);
        Assert.Equal("WheelCollider", typeof(WheelCollider).Name);
        Assert.IsType<Rigidbody>(registry.Create("RigidBodyComponent"));
        Assert.IsType<WheelCollider>(registry.Create("WheelColliderComponent"));
        Assert.IsType<Engine.Scene.Camera>(registry.Create("Engine.Scene.CameraComponent"));
        Assert.IsType<Engine.Scene.Light>(registry.Create("Light"));
        Assert.DoesNotContain(registry.RegisteredKeys, key => key.EndsWith("Component"));
    }
}
