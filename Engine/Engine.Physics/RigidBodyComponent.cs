using BepuPhysics;
using Engine.Mathematics;
using Engine.Scene;

namespace Engine.Physics;

public sealed class RigidBodyComponent : Component
{
    public RigidBodyKind Kind { get; set; } = RigidBodyKind.Dynamic;

    public ColliderShape Shape { get; set; } = ColliderShape.Box;

    public Vector3 Size { get; set; } = new(0.5f, 0.5f, 0.5f);

    public Vector3 Center { get; set; } = Vector3.Zero;

    public float Mass { get; set; } = 1f;

    public bool IsAwake { get; set; } = true;

    internal BodyHandle Body { get; set; }

    internal StaticHandle Static { get; set; }

    internal bool HasBody { get; set; }

    internal Vector3 LastSyncedPosition { get; set; }
}
