using Engine.Assets;
using Engine.Mathematics;
using Engine.Rendering;
using Xunit;

namespace Engine.Tests.Assets;

public class MeshLodChainTests
{
    private static (Vertex[] Vertices, uint[] Indices) Sphere(int segments, int rings)
    {
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
        return (vertices, [.. indices]);
    }

    [Fact]
    public void Generate_BuildsChainWithDecreasingTrianglesAndCumulativeErrors()
    {
        (Vertex[] vertices, uint[] indices) = Sphere(48, 32);
        MeshLodData[] chain = [.. MeshLodChainGenerator.Generate(vertices, indices, new MeshLodChainOptions(MaxLevels: 3, Ratio: 0.5f))];
        Assert.Equal(3, chain.Length);
        int previous = indices.Length / 3;
        float previousError = 0f;
        foreach (MeshLodData level in chain)
        {
            Assert.True(level.Indices.Length / 3 < previous);
            Assert.True(level.Error >= previousError);
            Assert.All(level.Indices, index => Assert.InRange(index, 0u, (uint)level.Vertices.Length - 1));
            previous = level.Indices.Length / 3;
            previousError = level.Error;
        }
    }

    [Fact]
    public void Generate_RespectsTriangleFloor()
    {
        (Vertex[] vertices, uint[] indices) = Sphere(32, 20);
        const int floor = 64;
        MeshLodData[] chain = [.. MeshLodChainGenerator.Generate(vertices, indices, new MeshLodChainOptions(MaxLevels: 8, Ratio: 0.25f, MinTriangles: floor))];
        Assert.NotEmpty(chain);
        Assert.True(chain[^1].Indices.Length / 3 >= floor / 2);
        Assert.True(chain[0].Indices.Length / 3 < indices.Length / 3);
    }

    [Fact]
    public void Generate_TinyMesh_ReturnsEmptyChain()
    {
        (Vertex[] vertices, uint[] indices) = Sphere(4, 3);
        MeshLodData[] chain = [.. MeshLodChainGenerator.Generate(vertices, indices, new MeshLodChainOptions())];
        Assert.Empty(chain);
    }

    [Fact]
    public void Generate_ExpiredSharedTimeBudget_ThrowsWithoutReturningPartialChain()
    {
        (Vertex[] vertices, uint[] indices) = Sphere(48, 32);
        Assert.Throws<MeshLodGenerationTimeoutException>(() => MeshLodChainGenerator.Generate(
            vertices,
            indices,
            new MeshLodChainOptions(MaxLevels: 4, TimeBudget: TimeSpan.Zero)));
    }

    [Fact]
    public void Generate_DoesNotMutateSource()
    {
        (Vertex[] vertices, uint[] indices) = Sphere(48, 32);
        Vertex[] sourceVertices = (Vertex[])vertices.Clone();
        uint[] sourceIndices = (uint[])indices.Clone();
        MeshLodChainGenerator.Generate(vertices, indices, new MeshLodChainOptions(MaxLevels: 2));
        Assert.Equal(sourceVertices, vertices);
        Assert.Equal(sourceIndices, indices);
    }
}
