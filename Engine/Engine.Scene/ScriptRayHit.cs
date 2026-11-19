namespace Engine.Scene;

using Engine.Mathematics;

public readonly record struct ScriptRayHit(Vector3 Point, Vector3 Normal, float Distance);
