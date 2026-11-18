using BepuPhysics;
using Engine.Mathematics;
using Engine.Scene;

namespace Engine.Physics;

[InspectorTooltip("Rigid body simulated by the physics engine. The transform follows the simulation.")]
public sealed class RigidBodyComponent : Component
{
    [InspectorTooltip("Dynamic bodies fall and collide, Static bodies stay fixed in place.")]
    public RigidBodyKind Kind { get; set; } = RigidBodyKind.Dynamic;

    [InspectorTooltip("Mass in kilograms used by Dynamic bodies. Affects collision response.")]
    [InspectorRange(0.001f, 1000f)]
    public float Mass { get; set; } = 1f;

    [InspectorTooltip("Whether the body is currently simulated. Uncheck to freeze it until something wakes it up.")]
    public bool IsAwake { get; set; } = true;

    internal BodyHandle Body { get; set; }

    internal StaticHandle Static { get; set; }

    internal bool HasBody { get; set; }

    internal Vector3 LastSyncedPosition { get; set; }
}
