using Engine.Mathematics;
using Engine.Scene;

namespace Engine.Physics;

public sealed class SphereColliderComponent : ColliderComponent
{
    [InspectorTooltip("Sphere radius in world units before object scale.")]
    [InspectorRange(0.01f, 100f)]
    public float Radius { get; set; } = 0.5f;

    internal override PhysicsColliderSpec CreateSpec(Vector3 scale, Func<string, PhysicsMeshData?>? meshResolver)
    {
        Vector3 effectiveScale = GetEffectiveScale(scale);
        return new PhysicsColliderSpec(
            PhysicsColliderKind.Sphere,
            Center * effectiveScale,
            Rotation,
            Vector3.Zero,
            MathF.Max(0f, Radius) * MathF.Max(effectiveScale.X, MathF.Max(effectiveScale.Y, effectiveScale.Z)),
            string.Empty,
            null);
    }

    public override void OnDrawGizmos()
    {
        Transform owner = Owner.Transform;
        PhysicsColliderSpec spec = CreateSpec(AbsScale(owner.Scale), null);
        DebugDraw.DrawWireSphere(owner.Position + owner.Rotation.Rotate(spec.Center), spec.Radius);
    }
}
