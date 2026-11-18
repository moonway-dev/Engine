using Engine.Mathematics;
using Engine.Scene;

namespace Engine.Physics;

public enum PhysicsColliderKind
{
    Box,
    Sphere,
    Capsule,
    Mesh,
}

public sealed record PhysicsColliderSpec(
    PhysicsColliderKind Kind,
    Vector3 Center,
    Quaternion Rotation,
    Vector3 Size,
    float Radius,
    string MeshKey,
    PhysicsMeshData? Mesh);

public abstract class ColliderComponent : Component
{
    [InspectorTooltip("Collider offset from the object origin in local space.")]
    public Vector3 Center { get; set; } = Vector3.Zero;

    [InspectorTooltip("Collider rotation in local space.")]
    public Quaternion Rotation { get; set; } = Quaternion.Identity;

    [InspectorTooltip("Extra scale applied to the collider shape on top of the object scale.")]
    public Vector3 ColliderScale { get; set; } = Vector3.One;

    protected Vector3 GetEffectiveScale(Vector3 objectScale) => new(
        MathF.Abs(objectScale.X * ColliderScale.X),
        MathF.Abs(objectScale.Y * ColliderScale.Y),
        MathF.Abs(objectScale.Z * ColliderScale.Z));

    internal abstract PhysicsColliderSpec CreateSpec(Vector3 scale, Func<string, PhysicsMeshData?>? meshResolver);
}
