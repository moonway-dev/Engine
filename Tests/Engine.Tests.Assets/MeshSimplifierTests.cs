using Engine.Assets;
using Engine.Mathematics;
using Engine.Rendering;
using Xunit;

namespace Engine.Tests.Assets;

public class MeshSimplifierTests
{
    [Fact]
    public void Simplify_ClosedSurface_CompactsGeometryAndPreservesManifoldEdges()
    {
        const int segments = 32;
        const int rings = 16;
        Vertex[] vertices = new Vertex[segments * rings];
        List<uint> indices = [];
        for (int x = 0; x < segments; x++)
        for (int y = 0; y < rings; y++)
        {
            float u = x * MathF.Tau / segments;
            float v = y * MathF.Tau / rings;
            vertices[x * rings + y] = new Vertex
            {
                Position = new Vector3((2 + MathF.Cos(v)) * MathF.Cos(u), MathF.Sin(v), (2 + MathF.Cos(v)) * MathF.Sin(u)),
            };
            uint a = (uint)(x * rings + y);
            uint b = (uint)(((x + 1) % segments) * rings + y);
            uint c = (uint)(((x + 1) % segments) * rings + (y + 1) % rings);
            uint d = (uint)(x * rings + (y + 1) % rings);
            indices.AddRange([a, b, c, a, c, d]);
        }
        MeshLodData result = MeshSimplifier.Simplify(vertices, [.. indices], 0.5f);
        Assert.True(result.Indices.Length < indices.Count);
        Assert.True(result.Vertices.Length < vertices.Length);
        Assert.True(result.Error > 0);
        Dictionary<(uint, uint), int> edges = [];
        for (int i = 0; i < result.Indices.Length; i += 3)
        {
            uint a = result.Indices[i], b = result.Indices[i + 1], c = result.Indices[i + 2];
            Assert.InRange(a, 0u, (uint)result.Vertices.Length - 1);
            Assert.NotEqual(a, b);
            Assert.NotEqual(a, c);
            Assert.NotEqual(b, c);
            Assert.True(Vector3.Cross(result.Vertices[b].Position - result.Vertices[a].Position,
                result.Vertices[c].Position - result.Vertices[a].Position).LengthSquared > 0);
            Add(a, b); Add(b, c); Add(c, a);
        }
        Assert.All(edges.Values, count => Assert.Equal(2, count));
        Assert.Equal(segments * rings, vertices.Length);

        void Add(uint a, uint b)
        {
            var key = a < b ? (a, b) : (b, a);
            edges[key] = edges.GetValueOrDefault(key) + 1;
        }
    }

    [Fact]
    public void Simplify_Tetrahedron_DoesNotCreateDuplicateFaces()
    {
        Vertex[] vertices =
        [
            new() { Position = new Vector3(1, 1, 1) },
            new() { Position = new Vector3(-1, -1, 1) },
            new() { Position = new Vector3(-1, 1, -1) },
            new() { Position = new Vector3(1, -1, -1) },
        ];
        uint[] indices = [0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3];
        MeshLodData result = MeshSimplifier.Simplify(vertices, indices, 0.5f);
        Assert.Equal(indices, result.Indices);
    }

    [Fact]
    public void Simplify_OpenQuad_PreservesBoundary()
    {
        Vertex[] vertices =
        [
            new() { Position = new Vector3(0, 0, 0) },
            new() { Position = new Vector3(1, 0, 0) },
            new() { Position = new Vector3(1, 1, 0) },
            new() { Position = new Vector3(0, 1, 0) },
        ];
        uint[] indices = [0, 1, 2, 0, 2, 3];
        MeshLodData result = MeshSimplifier.Simplify(vertices, indices, 0.5f);
        Assert.Equal(indices, result.Indices);
    }

    [Fact]
    public void Simplify_BudgetAndCancellation_AreEnforced()
    {
        Vertex[] vertices = new Vertex[3];
        uint[] indices = [0, 1, 2];
        Assert.Throws<InvalidOperationException>(() => MeshSimplifier.Simplify(vertices, indices, 0.5f, 1));
        Assert.Throws<OperationCanceledException>(() => MeshSimplifier.Simplify(vertices, indices, 0.5f,
            cancellationToken: new CancellationToken(true)));
    }
}
