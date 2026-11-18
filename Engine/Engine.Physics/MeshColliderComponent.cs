using Engine.Mathematics;
using Engine.Scene;

namespace Engine.Physics;

public sealed class MeshColliderComponent : ColliderComponent
{
    [InspectorTooltip("Path to the model whose triangles form the collision shape.")]
    public string ResourceKey { get; set; } = string.Empty;

    internal override PhysicsColliderSpec CreateSpec(Vector3 scale, Func<string, PhysicsMeshData?>? meshResolver)
    {
        Vector3 effectiveScale = GetEffectiveScale(scale);
        return new PhysicsColliderSpec(
            PhysicsColliderKind.Mesh,
            Center * effectiveScale,
            Rotation,
            effectiveScale,
            0f,
            ResourceKey,
            string.IsNullOrWhiteSpace(ResourceKey) ? null : meshResolver?.Invoke(ResourceKey));
    }
}
