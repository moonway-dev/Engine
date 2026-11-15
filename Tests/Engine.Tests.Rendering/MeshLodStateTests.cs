using Engine.Backends.Null;
using Engine.Mathematics;
using Engine.Rendering;
using Xunit;

namespace Engine.Tests.Rendering;

public class MeshLodStateTests
{
    private static (Vertex[] Vertices, uint[] Indices) Grid(int cells)
    {
        Vertex[] vertices = new Vertex[(cells + 1) * (cells + 1)];
        for (int x = 0; x <= cells; x++)
        for (int y = 0; y <= cells; y++)
            vertices[x * (cells + 1) + y] = new Vertex { Position = new Vector3(x, y, 0f) };
        List<uint> indices = [];
        for (int x = 0; x < cells; x++)
        for (int y = 0; y < cells; y++)
        {
            uint a = (uint)(x * (cells + 1) + y);
            uint b = (uint)((x + 1) * (cells + 1) + y);
            uint c = (uint)((x + 1) * (cells + 1) + y + 1);
            uint d = (uint)(x * (cells + 1) + y + 1);
            indices.AddRange([a, b, c, a, c, d]);
        }
        return (vertices, [.. indices]);
    }

    private static Mesh GridMesh(NullDevice device, int cells)
    {
        (Vertex[] vertices, uint[] indices) = Grid(cells);
        return new Mesh(device, vertices, indices, "LodTest");
    }

    private static uint[] ReducedIndices(uint[] source, int triangleCount) => source[..(triangleCount * 3)];

    [Fact]
    public void Select_WithoutLods_ReturnsZero()
    {
        using NullDevice device = new();
        using Mesh mesh = GridMesh(device, 1);
        MeshLodState state = new();
        Assert.Equal(0, state.Select(mesh, new MeshLodSettings(), 100f, 1f, 320f));
    }

    [Fact]
    public void Select_OffMode_AlwaysReturnsBaseLevel()
    {
        using NullDevice device = new();
        (Vertex[] vertices, uint[] indices) = Grid(8);
        using Mesh mesh = new(device, vertices, indices, "LodTest");
        mesh.AddLod(vertices, ReducedIndices(indices, indices.Length / 6), 0.01f);
        mesh.AddLod(vertices, ReducedIndices(indices, indices.Length / 12), 0.05f);
        MeshLodState state = new();
        Assert.Equal(0, state.Select(mesh, new MeshLodSettings(MeshLodMode.Off), 1000f, 1f, 1f));
    }

    [Fact]
    public void Select_ForcedMode_ClampsToAvailableLevels()
    {
        using NullDevice device = new();
        (Vertex[] vertices, uint[] indices) = Grid(8);
        using Mesh mesh = new(device, vertices, indices, "LodTest");
        mesh.AddLod(vertices, ReducedIndices(indices, indices.Length / 6), 0.01f);
        MeshLodState state = new();
        Assert.Equal(1, state.Select(mesh, new MeshLodSettings(MeshLodMode.Forced, ForcedLevel: 7), 100f, 1f, 320f));
    }

    [Fact]
    public void Select_AutoMode_TightensLevelWithSmallPixelBudget()
    {
        using NullDevice device = new();
        (Vertex[] vertices, uint[] indices) = Grid(8);
        using Mesh mesh = new(device, vertices, indices, "LodTest");
        mesh.AddLod(vertices, ReducedIndices(indices, indices.Length / 6), 0.5f);
        mesh.AddLod(vertices, ReducedIndices(indices, indices.Length / 12), 1f);
        MeshLodState state = new();
        Assert.Equal(2, state.Select(mesh, new MeshLodSettings(MeshLodMode.Auto), 1000f, 0.001f, 2f));
        Assert.Equal(0, state.Select(mesh, new MeshLodSettings(MeshLodMode.Auto), 1000000f, 1f, 100000f));
    }

    [Fact]
    public void Select_MeshGrowsOverTime_AdaptsLevel()
    {
        using NullDevice device = new();
        (Vertex[] vertices, uint[] indices) = Grid(8);
        using Mesh mesh = new(device, vertices, indices, "LodTest");
        MeshLodState state = new();
        MeshLodSettings settings = new(MeshLodMode.Auto);
        Assert.Equal(0, state.Select(mesh, settings, 1000f, 0.001f, 2f));
        mesh.AddLod(vertices, ReducedIndices(indices, indices.Length / 6), 0.5f);
        Assert.Equal(1, state.Select(mesh, settings, 1000f, 0.001f, 2f));
        mesh.AddLod(vertices, ReducedIndices(indices, indices.Length / 12), 1f);
        Assert.Equal(2, state.Select(mesh, settings, 1000f, 0.001f, 2f));
    }
}

public class MeshLodProjectionTests
{
    [Fact]
    public void PixelsPerUnit_MatchesPerspectiveProjection()
    {
        Camera camera = new()
        {
            Position = new Vector3(0f, 0f, 10f),
            FieldOfViewY = MathF.PI / 2f,
        };
        float pixelsPerUnit = MeshLodProjection.PixelsPerUnit(camera, new Size2D(1000, 1000), Vector3.Zero);
        Assert.Equal(50f, pixelsPerUnit, 3);
    }

    [Fact]
    public void PixelsPerUnit_ZeroExtent_ReturnsZero()
    {
        Camera camera = new() { Position = new Vector3(0f, 0f, 10f) };
        Assert.Equal(0f, MeshLodProjection.PixelsPerUnit(camera, Size2D.Zero, Vector3.Zero));
    }

    [Fact]
    public void PixelsPerUnit_UsesNearestBoundsDepthWithoutDoubleScale()
    {
        using NullDevice device = new();
        Vertex[] vertices =
        [
            new() { Position = new Vector3(-2f, -1f, -1f) },
            new() { Position = new Vector3(2f, 1f, 1f) },
        ];
        using Mesh mesh = new(device, vertices, [0u, 1u, 0u], "BoundsLodTest");
        Camera camera = new()
        {
            Position = new Vector3(0f, 0f, 10f),
            FieldOfViewY = MathF.PI / 2f,
        };
        Matrix4x4 model = Matrix4x4.CreateScale(3f);
        float pixelsPerUnit = MeshLodProjection.PixelsPerUnit(camera, new Size2D(1000, 1000), mesh, model);
        Assert.Equal(100f / 1.4f, pixelsPerUnit, 3);
    }

    [Fact]
    public void PixelsPerUnit_BoundsIntersectingNearPlane_ForcesFinestLevel()
    {
        using NullDevice device = new();
        Vertex[] vertices =
        [
            new() { Position = new Vector3(-1f, -1f, -1f) },
            new() { Position = new Vector3(1f, 1f, 1f) },
        ];
        using Mesh mesh = new(device, vertices, [0u, 1u, 0u, 0u, 1u, 1u], "NearLodTest");
        Camera camera = new()
        {
            Position = Vector3.Zero,
            FieldOfViewY = MathF.PI / 2f,
            NearPlane = 0.1f,
        };
        float pixelsPerUnit = MeshLodProjection.PixelsPerUnit(camera, new Size2D(1000, 1000), mesh, Matrix4x4.CreateTranslation(new Vector3(0f, 0f, -0.5f)));
        MeshLodState state = new();
        mesh.AddLod(vertices, [0u, 0u, 0u], 0.001f);
        Assert.Equal(float.MaxValue, pixelsPerUnit);
        Assert.Equal(0, state.Select(mesh, new MeshLodSettings(), pixelsPerUnit, 1f, 320f));
    }

    [Fact]
    public void ScaleOf_TakesLargestAxis()
    {
        Matrix4x4 matrix = Matrix4x4.CreateScale(new Vector3(2f, 5f, 3f));
        Assert.Equal(5f, MeshLodProjection.ScaleOf(matrix), 3);
    }
}
