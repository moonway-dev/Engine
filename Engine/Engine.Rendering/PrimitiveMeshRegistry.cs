namespace Engine.Rendering;

public static class PrimitiveMeshRegistry
{
    public delegate (Vertex[] Vertices, uint[] Indices) Factory();

    private static readonly Dictionary<string, Factory> Factories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ground"] = () => MeshPrimitives.Plane(24f, 24f),
        ["sphere"] = () => MeshPrimitives.Sphere(1f, 32, 20),
        ["capsule"] = () => MeshPrimitives.Capsule(0.35f, 1.8f),
        ["cylinder"] = () => MeshPrimitives.Cylinder(0.5f, 1f),
        ["car_body"] = () => MeshPrimitives.Box(1.9f, 1.1f, 6.2f),
        ["car_cabin"] = () => MeshPrimitives.Box(1.7f, 0.9f, 3.4f),
        ["wheel_cylinder"] = () => MeshPrimitives.Cylinder(0.6f, 0.44f),
        ["unit_box"] = () => MeshPrimitives.Box(1f, 1f, 1f),
        ["ramp_wedge"] = () => MeshPrimitives.Wedge(8f, 1.8f, 14f),
    };

    public static IEnumerable<string> Keys => Factories.Keys;

    public static bool TryGet(string key, out (Vertex[] Vertices, uint[] Indices) geometry)
    {
        if (Factories.TryGetValue(key, out Factory? factory))
        {
            geometry = factory();
            return true;
        }
        geometry = default;
        return false;
    }
}
