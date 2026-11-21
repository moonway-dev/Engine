using Engine.Mathematics;
using Engine.Scene;

namespace Engine.Physics;

public sealed class CapsuleColliderComponent : ColliderComponent
{
    [InspectorTooltip("Capsule radius in world units before object scale.")]
    [InspectorRange(0.01f, 100f)]
    public float Radius { get; set; } = 0.35f;

    [InspectorTooltip("Capsule height in world units before object scale.")]
    [InspectorRange(0.01f, 100f)]
    public float Height { get; set; } = 1.5f;

    internal override PhysicsColliderSpec CreateSpec(Vector3 scale, Func<string, PhysicsMeshData?>? meshResolver)
    {
        Vector3 effectiveScale = GetEffectiveScale(scale);
        return new PhysicsColliderSpec(
            PhysicsColliderKind.Capsule,
            Center * effectiveScale,
            Rotation,
            new Vector3(0f, MathF.Max(0f, Height) * effectiveScale.Y, 0f),
            MathF.Max(0f, Radius) * MathF.Max(effectiveScale.X, MathF.Max(effectiveScale.Z, 0f)),
            string.Empty,
            null);
    }

    public override void OnDrawGizmos()
    {
        Transform owner = Owner.Transform;
        PhysicsColliderSpec spec = CreateSpec(AbsScale(owner.Scale), null);
        float cylinderLength = MathF.Max(spec.Size.Y - spec.Radius * 2f, 0f);
        DebugDraw.DrawWireCapsule(owner.Position + owner.Rotation.Rotate(spec.Center), owner.Rotation * spec.Rotation, spec.Radius, cylinderLength);
    }
}
