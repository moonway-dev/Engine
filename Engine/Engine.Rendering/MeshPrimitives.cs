using Engine.Mathematics;

namespace Engine.Rendering;

public static class MeshPrimitives
{
    public static (Vertex[] Vertices, uint[] Indices) Box(float width, float height, float depth)
    {
        float hx = width * 0.5f;
        float hy = height * 0.5f;
        float hz = depth * 0.5f;
        List<Vertex> vertices = [];
        List<uint> indices = [];

        void AddQuad(Vector3 c0, Vector3 c1, Vector3 c2, Vector3 c3, Vector3 normal, Vector2 uv0, Vector2 uv1)
        {
            uint start = (uint)vertices.Count;
            vertices.Add(new Vertex { Position = c0, Normal = normal, UV = uv0 });
            vertices.Add(new Vertex { Position = c1, Normal = normal, UV = uv1 });
            vertices.Add(new Vertex { Position = c2, Normal = normal, UV = new Vector2((uv0.X + uv1.X) * 0.5f, (uv0.Y + uv1.Y) * 0.5f) });
            vertices.Add(new Vertex { Position = c3, Normal = normal, UV = new Vector2(uv1.X, uv0.Y) });
            indices.AddRange([start, start + 1, start + 2, start, start + 2, start + 3]);
        }

        AddQuad(new Vector3(-hx, -hy, -hz), new Vector3(hx, -hy, -hz), new Vector3(hx, hy, -hz), new Vector3(-hx, hy, -hz), new Vector3(0, 0, -1), new Vector2(0, 0), new Vector2(width / height, height));
        AddQuad(new Vector3(hx, -hy, hz), new Vector3(-hx, -hy, hz), new Vector3(-hx, hy, hz), new Vector3(hx, hy, hz), new Vector3(0, 0, 1), new Vector2(0, 0), new Vector2(width / height, height));
        AddQuad(new Vector3(hx, -hy, -hz), new Vector3(hx, -hy, hz), new Vector3(hx, hy, hz), new Vector3(hx, hy, -hz), new Vector3(1, 0, 0), new Vector2(0, 0), new Vector2(depth / height, height));
        AddQuad(new Vector3(-hx, -hy, hz), new Vector3(-hx, -hy, -hz), new Vector3(-hx, hy, -hz), new Vector3(-hx, hy, hz), new Vector3(-1, 0, 0), new Vector2(0, 0), new Vector2(depth / height, height));
        AddQuad(new Vector3(-hx, hy, -hz), new Vector3(hx, hy, -hz), new Vector3(hx, hy, hz), new Vector3(-hx, hy, hz), new Vector3(0, 1, 0), new Vector2(0, 0), new Vector2(width / depth, depth));
        AddQuad(new Vector3(-hx, -hy, hz), new Vector3(hx, -hy, hz), new Vector3(hx, -hy, -hz), new Vector3(-hx, -hy, -hz), new Vector3(0, -1, 0), new Vector2(0, 0), new Vector2(width / depth, depth));
        Vertex[] resultVertices = [.. vertices];
        uint[] resultIndices = [.. indices];
        MeshTangents.Generate(resultVertices, resultIndices);
        return (resultVertices, resultIndices);
    }

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

    public static (Vertex[] Vertices, uint[] Indices) Cylinder(float radius = 0.5f, float height = 1f, int segments = 24)
    {
        if (!float.IsFinite(radius) || radius <= 0f)
            throw new ArgumentOutOfRangeException(nameof(radius));
        if (!float.IsFinite(height) || height <= 0f)
            throw new ArgumentOutOfRangeException(nameof(height));
        if (segments < 3)
            throw new ArgumentOutOfRangeException(nameof(segments));
        List<Vertex> vertices = [];
        List<uint> indices = [];
        float halfHeight = height * 0.5f;
        for (int side = 0; side <= segments; side++)
        {
            float u = (float)side / segments;
            float theta = u * MathUtil.TwoPi;
            float sin = MathF.Sin(theta);
            float cos = MathF.Cos(theta);
            Vector3 normal = new(cos, 0f, sin);
            vertices.Add(new Vertex { Position = new Vector3(cos * radius, -halfHeight, sin * radius), Normal = normal, UV = new Vector2(u, 0f), Tangent = new Vector4(new Vector3(-sin, 0f, cos), 1f) });
            vertices.Add(new Vertex { Position = new Vector3(cos * radius, halfHeight, sin * radius), Normal = normal, UV = new Vector2(u, 1f), Tangent = new Vector4(new Vector3(-sin, 0f, cos), 1f) });
        }
        for (int side = 0; side < segments; side++)
        {
            uint baseIndex = (uint)(side * 2);
            indices.AddRange([baseIndex, baseIndex + 1, baseIndex + 2]);
            indices.AddRange([baseIndex + 1, baseIndex + 3, baseIndex + 2]);
        }
        foreach (float sign in new[] { -1f, 1f })
        {
            Vector3 normal = new(0f, sign, 0f);
            uint centerIndex = (uint)vertices.Count;
            vertices.Add(new Vertex { Position = new Vector3(0f, sign * halfHeight, 0f), Normal = normal, UV = new Vector2(0.5f, 0.5f), Tangent = new Vector4(1f, 0f, 0f, 1f) });
            uint firstRing = (uint)vertices.Count;
            for (int side = 0; side <= segments; side++)
            {
                float theta = (float)side / segments * MathUtil.TwoPi;
                vertices.Add(new Vertex { Position = new Vector3(MathF.Cos(theta) * radius, sign * halfHeight, MathF.Sin(theta) * radius), Normal = normal, UV = new Vector2(0.5f + MathF.Cos(theta) * 0.5f, 0.5f - MathF.Sin(theta) * 0.5f), Tangent = new Vector4(1f, 0f, 0f, 1f) });
            }
            for (int side = 0; side < segments; side++)
            {
                uint left = firstRing + (uint)side;
                uint right = firstRing + (uint)(side + 1);
                if (sign > 0f)
                    indices.AddRange([centerIndex, right, left]);
                else
                    indices.AddRange([centerIndex, left, right]);
            }
        }
        Vertex[] resultVertices = [.. vertices];
        uint[] resultIndices = [.. indices];
        MeshTangents.Generate(resultVertices, resultIndices);
        return (resultVertices, resultIndices);
    }

    public static (Vertex[] Vertices, uint[] Indices) Wedge(float width = 4f, float height = 2f, float depth = 4f)
    {
        if (!float.IsFinite(width) || width <= 0f)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (!float.IsFinite(height) || height <= 0f)
            throw new ArgumentOutOfRangeException(nameof(height));
        if (!float.IsFinite(depth) || depth <= 0f)
            throw new ArgumentOutOfRangeException(nameof(depth));
        float hw = width * 0.5f;
        float hd = depth * 0.5f;
        List<Vertex> vertices = [];
        List<uint> indices = [];

        void AddQuad(Vector3 c0, Vector3 c1, Vector3 c2, Vector3 c3, Vector3 normal, Vector2 uv0, Vector2 uv1)
        {
            uint start = (uint)vertices.Count;
            vertices.Add(new Vertex { Position = c0, Normal = normal, UV = uv0 });
            vertices.Add(new Vertex { Position = c1, Normal = normal, UV = uv1 });
            vertices.Add(new Vertex { Position = c2, Normal = normal, UV = new Vector2((uv0.X + uv1.X) * 0.5f, (uv0.Y + uv1.Y) * 0.5f) });
            vertices.Add(new Vertex { Position = c3, Normal = normal, UV = new Vector2(uv1.X, uv0.Y) });
            indices.AddRange([start, start + 1, start + 2, start, start + 2, start + 3]);
        }

        void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
        {
            uint start = (uint)vertices.Count;
            vertices.Add(new Vertex { Position = a, Normal = normal, UV = new Vector2(0f, 0f) });
            vertices.Add(new Vertex { Position = b, Normal = normal, UV = new Vector2(1f, 1f) });
            vertices.Add(new Vertex { Position = c, Normal = normal, UV = new Vector2(1f, 0f) });
            indices.AddRange([start, start + 1, start + 2]);
        }

        AddQuad(
            new Vector3(-hw, 0f, hd), new Vector3(hw, 0f, hd), new Vector3(hw, 0f, -hd), new Vector3(-hw, 0f, -hd),
            new Vector3(0f, -1f, 0f), new Vector2(0f, 0f), new Vector2(width / depth, 1f));
        AddQuad(
            new Vector3(-hw, 0f, -hd), new Vector3(hw, 0f, -hd), new Vector3(hw, height, -hd), new Vector3(-hw, height, -hd),
            new Vector3(0f, 0f, -1f), new Vector2(0f, 0f), new Vector2(width / height, height));
        AddQuad(
            new Vector3(-hw, 0f, hd), new Vector3(-hw, height, -hd), new Vector3(hw, height, -hd), new Vector3(hw, 0f, hd),
            new Vector3(0f, depth, height).Normalized, new Vector2(0f, 0f), new Vector2(width / height, height / depth + 1f));
        AddTriangle(new Vector3(hw, 0f, hd), new Vector3(hw, height, -hd), new Vector3(hw, 0f, -hd), new Vector3(1f, 0f, 0f));
        AddTriangle(new Vector3(-hw, 0f, hd), new Vector3(-hw, 0f, -hd), new Vector3(-hw, height, -hd), new Vector3(-1f, 0f, 0f));
        Vertex[] resultVertices = [.. vertices];
        uint[] resultIndices = [.. indices];
        MeshTangents.Generate(resultVertices, resultIndices);
        return (resultVertices, resultIndices);
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
