using Engine.Mathematics;

namespace Engine.Rendering;

public static class MeshPrimitives
{
    public static (Vertex[] Vertices, uint[] Indices) Cube(float size = 1f)
    {
        float half = size * 0.5f;
        List<Vertex> vertices = [];
        List<uint> indices = [];

        AddQuad(vertices, indices,
            new Vector3(-half, -half, -half), new Vector3(half, -half, -half), new Vector3(half, half, -half), new Vector3(-half, half, -half),
            new Vector3(0f, 0f, -1f), new Vector3(0f, 0f, 1f), new Vector2(1f, 0f), new Vector2(0f, -1f));
        AddQuad(vertices, indices,
            new Vector3(half, -half, half), new Vector3(-half, -half, half), new Vector3(-half, half, half), new Vector3(half, half, half),
            new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, -1f), new Vector2(1f, 0f), new Vector2(0f, -1f));
        AddQuad(vertices, indices,
            new Vector3(half, -half, -half), new Vector3(half, -half, half), new Vector3(half, half, half), new Vector3(half, half, -half),
            new Vector3(1f, 0f, 0f), new Vector3(-1f, 0f, 0f), new Vector2(1f, 0f), new Vector2(0f, -1f));
        AddQuad(vertices, indices,
            new Vector3(-half, -half, half), new Vector3(-half, -half, -half), new Vector3(-half, half, -half), new Vector3(-half, half, half),
            new Vector3(-1f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector2(1f, 0f), new Vector2(0f, -1f));
        AddQuad(vertices, indices,
            new Vector3(-half, half, -half), new Vector3(half, half, -half), new Vector3(half, half, half), new Vector3(-half, half, half),
            new Vector3(0f, 1f, 0f), new Vector3(0f, -1f, 0f), new Vector2(1f, 0f), new Vector2(0f, -1f));
        AddQuad(vertices, indices,
            new Vector3(-half, -half, half), new Vector3(half, -half, half), new Vector3(half, -half, -half), new Vector3(-half, -half, -half),
            new Vector3(0f, -1f, 0f), new Vector3(0f, 1f, 0f), new Vector2(1f, 0f), new Vector2(0f, -1f));
        Vertex[] result = [.. vertices];
        uint[] resultIndices = [.. indices];
        MeshTangents.Generate(result, resultIndices);
        return (result, resultIndices);
    }

    public static (Vertex[] Vertices, uint[] Indices) Sphere(float radius = 1f, int segments = 24, int rings = 16)
    {
        List<Vertex> vertices = [];
        List<uint> indices = [];

        for (int y = 0; y <= rings; y++)
        {
            float v = (float)y / rings;
            float phi = v * MathUtil.Pi;
            for (int x = 0; x <= segments; x++)
            {
                float u = (float)x / segments;
                float theta = u * MathUtil.TwoPi;
                Vector3 normal = new(
                    MathF.Sin(phi) * MathF.Cos(theta),
                    MathF.Cos(phi),
                    MathF.Sin(phi) * MathF.Sin(theta));
                vertices.Add(new Vertex
                {
                    Position = normal * radius,
                    Normal = normal,
                    UV = new Vector2(u, 1f - v),
                    Tangent = new Vector4(new Vector3(-MathF.Sin(theta), 0f, MathF.Cos(theta)), 1f),
                });
            }
        }

        for (int y = 0; y < rings; y++)
        {
            for (int x = 0; x < segments; x++)
            {
                uint a = (uint)(y * (segments + 1) + x);
                uint b = a + 1;
                uint c = a + (uint)segments + 1;
                uint d = c + 1;
                indices.AddRange([a, b, c, b, d, c]);
            }
        }
        Vertex[] result = [.. vertices];
        uint[] resultIndices = [.. indices];
        MeshTangents.Generate(result, resultIndices);
        return (result, resultIndices);
    }

    public static (Vertex[] Vertices, uint[] Indices) Plane(float width = 20f, float depth = 20f)
    {
        float halfWidth = width * 0.5f;
        float halfDepth = depth * 0.5f;
        Vector3 normal = Vector3.Up;
        Vertex[] vertices =
        [
            new Vertex { Position = new Vector3(-halfWidth, 0f, -halfDepth), Normal = normal, UV = new Vector2(0f, 0f) },
            new Vertex { Position = new Vector3(halfWidth, 0f, -halfDepth), Normal = normal, UV = new Vector2(1f, 0f) },
            new Vertex { Position = new Vector3(halfWidth, 0f, halfDepth), Normal = normal, UV = new Vector2(1f, 1f) },
            new Vertex { Position = new Vector3(-halfWidth, 0f, halfDepth), Normal = normal, UV = new Vector2(0f, 1f) },
        ];
        uint[] indices = [0, 1, 2, 0, 2, 3];
        MeshTangents.Generate(vertices, indices);
        return (vertices, indices);
    }

    private static void AddQuad(
        List<Vertex> vertices,
        List<uint> indices,
        Vector3 cornerA,
        Vector3 cornerB,
        Vector3 cornerC,
        Vector3 cornerD,
        Vector3 normal,
        Vector3 tangent,
        Vector2 uvA,
        Vector2 uvC)
    {
        uint start = (uint)vertices.Count;
        vertices.Add(new Vertex { Position = cornerA, Normal = normal, UV = uvA, Tangent = new Vector4(tangent, 1f) });
        vertices.Add(new Vertex { Position = cornerB, Normal = normal, UV = uvC with { Y = uvA.Y }, Tangent = new Vector4(tangent, 1f) });
        vertices.Add(new Vertex { Position = cornerC, Normal = normal, UV = uvC, Tangent = new Vector4(tangent, 1f) });
        vertices.Add(new Vertex { Position = cornerD, Normal = normal, UV = uvA with { X = uvC.X }, Tangent = new Vector4(tangent, 1f) });
        indices.AddRange([start, start + 2, start + 1, start, start + 3, start + 2]);
    }
}
