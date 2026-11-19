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

    public static (Vertex[] Vertices, uint[] Indices) Capsule(float radius = 0.35f, float height = 1.8f, int segments = 24, int hemisphereRings = 8)
    {
        if (!float.IsFinite(radius) || radius <= 0f)
            throw new ArgumentOutOfRangeException(nameof(radius));
        if (!float.IsFinite(height) || height < radius * 2f)
            throw new ArgumentOutOfRangeException(nameof(height));
        if (segments < 3)
            throw new ArgumentOutOfRangeException(nameof(segments));
        if (hemisphereRings < 1)
            throw new ArgumentOutOfRangeException(nameof(hemisphereRings));
        float halfLength = height * 0.5f - radius;
        List<Vertex> vertices =
        [
            new Vertex
            {
                Position = Vector3.Up * (height * 0.5f),
                Normal = Vector3.Up,
                UV = new Vector2(0.5f, 1f),
            },
        ];
        List<uint> indices = [];
        for (int ring = 1; ring <= hemisphereRings; ring++)
            AddRing(ring * MathUtil.HalfPi / hemisphereRings, halfLength);
        for (int ring = halfLength > 0f ? 0 : 1; ring < hemisphereRings; ring++)
            AddRing(MathUtil.HalfPi + ring * MathUtil.HalfPi / hemisphereRings, -halfLength);
        int ringCount = (vertices.Count - 1) / (segments + 1);
        for (int x = 0; x < segments; x++)
            indices.AddRange([0u, (uint)(x + 2), (uint)(x + 1)]);
        for (int ring = 0; ring < ringCount - 1; ring++)
        {
            for (int x = 0; x < segments; x++)
            {
                uint a = (uint)(1 + ring * (segments + 1) + x);
                uint b = a + 1;
                uint c = a + (uint)segments + 1;
                uint d = c + 1;
                indices.AddRange([a, b, c, b, d, c]);
            }
        }
        uint bottom = (uint)vertices.Count;
        vertices.Add(new Vertex
        {
            Position = Vector3.Down * (height * 0.5f),
            Normal = Vector3.Down,
            UV = new Vector2(0.5f, 0f),
        });
        for (int x = 0; x < segments; x++)
        {
            uint a = (uint)(1 + (ringCount - 1) * (segments + 1) + x);
            indices.AddRange([a, a + 1, bottom]);
        }
        Vertex[] result = [.. vertices];
        uint[] resultIndices = [.. indices];
        MeshTangents.Generate(result, resultIndices);
        return (result, resultIndices);

        void AddRing(float phi, float offset)
        {
            for (int x = 0; x <= segments; x++)
            {
                float u = (float)x / segments;
                float theta = u * MathUtil.TwoPi;
                Vector3 normal = new(MathF.Sin(phi) * MathF.Cos(theta), MathF.Cos(phi), MathF.Sin(phi) * MathF.Sin(theta));
                Vector3 position = normal * radius + Vector3.Up * offset;
                vertices.Add(new Vertex
                {
                    Position = position,
                    Normal = normal,
                    UV = new Vector2(u, position.Y / height + 0.5f),
                    Tangent = new Vector4(new Vector3(-MathF.Sin(theta), 0f, MathF.Cos(theta)), 1f),
                });
            }
        }
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
