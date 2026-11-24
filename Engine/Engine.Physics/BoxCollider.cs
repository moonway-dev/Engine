using Engine.Mathematics;
using Engine.Scene;

namespace Engine.Physics;

public sealed class BoxCollider : Collider
{
    [InspectorTooltip("Full box size in world units before object scale.")]
    public Vector3 Size { get; set; } = new(1f, 1f, 1f);

    internal override PhysicsColliderSpec CreateSpec(Vector3 scale, Func<string, PhysicsMeshData?>? meshResolver)
    {
        Vector3 effectiveScale = GetEffectiveScale(scale);
        return new PhysicsColliderSpec(
            PhysicsColliderKind.Box,
            Center * effectiveScale,
            Rotation,
            new Vector3(
                MathF.Abs(Size.X * effectiveScale.X),
                MathF.Abs(Size.Y * effectiveScale.Y),
                MathF.Abs(Size.Z * effectiveScale.Z)),
            0f,
            string.Empty,
            null);
    }

    public override void OnDrawGizmos()
    {
        Transform owner = Owner.Transform;
        PhysicsColliderSpec spec = CreateSpec(AbsScale(owner.Scale), null);
        DebugDraw.DrawWireBox(owner.Position + owner.Rotation.Rotate(spec.Center), spec.Size, owner.Rotation * spec.Rotation);
    }
}
