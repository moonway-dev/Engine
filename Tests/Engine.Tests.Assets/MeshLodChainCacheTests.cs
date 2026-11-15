using System.Text;
using Engine.Assets;
using Engine.Mathematics;
using Engine.Rendering;
using Xunit;

namespace Engine.Tests.Assets;

public sealed class MeshLodChainCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "EngineLodCacheTests", Guid.NewGuid().ToString("N"));
    private readonly string _source;
    private readonly string _directory;

    public MeshLodChainCacheTests()
    {
        _source = Path.Combine(_root, "Models", "sphere.obj");
        _directory = Path.Combine(_root, "Cache", "Models");
        Directory.CreateDirectory(Path.GetDirectoryName(_source)!);
        Vertex[] vertices = new Vertex[4];
        vertices[0] = new Vertex { Position = new Vector3(0f, 0f, 0f) };
        vertices[1] = new Vertex { Position = new Vector3(1f, 0f, 0f) };
        vertices[2] = new Vertex { Position = new Vector3(0f, 1f, 0f) };
        vertices[3] = new Vertex { Position = new Vector3(0f, 0f, 1f) };
        StringBuilder text = new();
        foreach (Vertex vertex in vertices)
            text.AppendLine($"v {vertex.Position.X} {vertex.Position.Y} {vertex.Position.Z}");
        text.AppendLine("f 1 2 3");
        text.AppendLine("f 1 2 4");
        text.AppendLine("f 1 3 4");
        text.AppendLine("f 2 3 4");
        File.WriteAllText(_source, text.ToString());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void SaveAndLoad_RoundTripsLevels()
    {
        Vertex[] vertices = new Vertex[64 * 32];
        List<uint> indices = [];
        for (int x = 0; x < 64; x++)
        for (int y = 0; y < 32; y++)
        {
            float u = x * MathF.Tau / 64;
            float v = y * MathF.Tau / 32;
            vertices[x * 32 + y] = new Vertex
            {
                Position = new Vector3((2 + MathF.Cos(v)) * MathF.Cos(u), MathF.Sin(v), (2 + MathF.Cos(v)) * MathF.Sin(u)),
            };
            uint a = (uint)(x * 32 + y);
            uint b = (uint)(((x + 1) % 64) * 32 + y);
            uint c = (uint)(((x + 1) % 64) * 32 + (y + 1) % 32);
            uint d = (uint)(x * 32 + (y + 1) % 32);
            indices.AddRange([a, b, c, a, c, d]);
        }
        MeshLodChainOptions options = new(MaxLevels: 2, Ratio: 0.5f);
        MeshLodData[] chain = [.. MeshLodChainGenerator.Generate(vertices, [.. indices], options)];

        MeshLodChainCache.Save(_source, string.Empty, options, _directory, chain);
        IReadOnlyList<MeshLodData>? loaded = MeshLodChainCache.TryLoad(_source, string.Empty, options, _directory);

        Assert.NotNull(loaded);
        Assert.Equal(chain.Length, loaded!.Count);
        for (int i = 0; i < chain.Length; i++)
        {
            Assert.Equal(chain[i].Vertices.Length, loaded[i].Vertices.Length);
            Assert.Equal(chain[i].Indices.Length, loaded[i].Indices.Length);
            Assert.Equal(chain[i].Error, loaded[i].Error);
            Assert.Equal(chain[i].Vertices, loaded[i].Vertices);
            Assert.Equal(chain[i].Indices, loaded[i].Indices);
        }
    }

    [Fact]
    public void TryLoad_MissingCache_ReturnsNull()
    {
        Assert.Null(MeshLodChainCache.TryLoad(_source, string.Empty, new MeshLodChainOptions(), _directory));
    }

    [Fact]
    public void TryLoad_ChangedOptions_ReturnsNull()
    {
        MeshLodChainOptions options = new(MaxLevels: 1, Ratio: 0.5f);
        MeshLodData[] chain =
        [
            new(new[] { SingleVertex() }, [0u, 0u, 0u], 1f),
        ];
        MeshLodChainCache.Save(_source, string.Empty, options, _directory, chain);
        Assert.Null(MeshLodChainCache.TryLoad(_source, string.Empty, options with { MaxLevels = 2 }, _directory));
    }

    [Fact]
    public void TryLoad_ChangedBudget_ReturnsNull()
    {
        MeshLodChainOptions options = new(MaxLevels: 1, Ratio: 0.5f);
        MeshLodData[] chain = [new(new[] { SingleVertex() }, [0u, 0u, 0u], 1f)];
        MeshLodChainCache.Save(_source, string.Empty, options, _directory, chain);
        Assert.Null(MeshLodChainCache.TryLoad(_source, string.Empty, options with { MemoryBudget = options.MemoryBudget - 1 }, _directory));
    }

    [Fact]
    public void TryLoad_SameLengthAndTimestampButChangedContent_ReturnsNull()
    {
        MeshLodChainOptions options = new(MaxLevels: 1, Ratio: 0.5f);
        MeshLodData[] chain = [new(new[] { SingleVertex() }, [0u, 0u, 0u], 1f)];
        MeshLodChainCache.Save(_source, string.Empty, options, _directory, chain);
        FileInfo info = new(_source);
        long length = info.Length;
        DateTime timestamp = info.LastWriteTimeUtc;
        byte[] original = File.ReadAllBytes(_source);
        byte[] changed = (byte[])original.Clone();
        int index = Array.FindIndex(changed, value => value == (byte)'0');
        Assert.True(index >= 0);
        changed[index] = (byte)'9';
        File.WriteAllBytes(_source, changed);
        File.SetLastWriteTimeUtc(_source, timestamp);
        Assert.Equal(length, new FileInfo(_source).Length);
        Assert.Null(MeshLodChainCache.TryLoad(_source, string.Empty, options, _directory));
    }

    [Fact]
    public void TryLoad_ChangedSource_ReturnsNull()
    {
        MeshLodChainOptions options = new(MaxLevels: 1, Ratio: 0.5f);
        MeshLodData[] chain =
        [
            new(new[] { SingleVertex() }, [0u, 0u, 0u], 1f),
        ];
        MeshLodChainCache.Save(_source, string.Empty, options, _directory, chain);
        File.AppendAllText(_source, "# changed\n");
        Assert.Null(MeshLodChainCache.TryLoad(_source, string.Empty, options, _directory));
    }

    private static Vertex SingleVertex() => new() { Position = new Vector3(0f, 0f, 0f) };
}
