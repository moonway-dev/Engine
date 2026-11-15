using Engine.Mathematics;

namespace Engine.Scene;

public sealed class CameraComponent : Component
{
    public float FieldOfViewY { get; set; } = MathUtil.DegToRad * 60f;

    public float NearPlane { get; set; } = 0.1f;

    public float FarPlane { get; set; } = 500f;
}

public enum LightKind
{
    Directional,
    Point,
}

public sealed class LightComponent : Component
{
    public LightKind Kind { get; set; } = LightKind.Directional;

    public Vector3 Color { get; set; } = new(1f, 1f, 1f);

    public float Intensity { get; set; } = 1f;

    public float Range { get; set; } = 20f;
}
