using Engine.Mathematics;

namespace Engine.Scene;

[InspectorTooltip("Perspective camera used by the Game view and rendering.")]
public sealed class CameraComponent : Component
{
    [InspectorTooltip("Vertical field of view in radians. Smaller values zoom in, larger values show more of the scene.")]
    [InspectorRange(0.17f, 2.1f)]
    public float FieldOfViewY { get; set; } = MathUtil.DegToRad * 60f;

    [InspectorTooltip("Distance to the near clipping plane. Objects closer than this are not rendered.")]
    [InspectorRange(0.01f, 10f)]
    public float NearPlane { get; set; } = 0.1f;

    [InspectorTooltip("Distance to the far clipping plane. Objects farther than this are not rendered.")]
    [InspectorRange(1f, 2000f)]
    public float FarPlane { get; set; } = 500f;
}

public enum LightKind
{
    Directional,
    Point,
}

[InspectorTooltip("Scene light. Directional lights light the whole scene, point lights light around their position.")]
public sealed class LightComponent : Component
{
    [InspectorTooltip("Directional acts like the sun, Point emits light in all directions from the object position.")]
    public LightKind Kind { get; set; } = LightKind.Directional;

    [InspectorTooltip("Light color.")]
    [InspectorColor]
    public Vector3 Color { get; set; } = new(1f, 1f, 1f);

    [InspectorTooltip("Light strength. Higher values make surfaces brighter.")]
    [InspectorRange(0f, 10f)]
    public float Intensity { get; set; } = 1f;

    [InspectorTooltip("Point light reach in world units. Has no effect on directional lights.")]
    [InspectorRange(0f, 500f)]
    public float Range { get; set; } = 20f;
}
