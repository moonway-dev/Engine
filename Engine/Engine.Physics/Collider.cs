using Engine.Mathematics;
using Engine.Scene;

namespace Engine.Physics;

public enum PhysicsColliderKind
{
    Box,
    Sphere,
    Capsule,
    Cylinder,
    Mesh,
}

public sealed record PhysicsColliderSpec(
    PhysicsColliderKind Kind,
    Vector3 Center,
    Quaternion Rotation,
    Vector3 Size,
    float Radius,
    string MeshKey,
    PhysicsMeshData? Mesh,
    float WeightScale = 1f,
    float Length = 0f,
    int Layer = -1);

public abstract class Collider : Component
{
    public override bool AllowMultiplePerObject => true;

    [InspectorLayer]
    [InspectorTooltip("Physics layer index from Project Settings; the collision matrix decides which layers interact.")]
    public int PhysicsLayer { get; set; }

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

    protected static Vector3 AbsScale(Vector3 scale) => new(
        MathF.Abs(scale.X),
        MathF.Abs(scale.Y),
        MathF.Abs(scale.Z));

    internal abstract PhysicsColliderSpec CreateSpec(Vector3 scale, Func<string, PhysicsMeshData?>? meshResolver);
}
