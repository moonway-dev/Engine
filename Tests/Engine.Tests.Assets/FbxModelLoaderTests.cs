using Engine.Assets;
using Xunit;

namespace Engine.Tests.Assets;

public class FbxModelLoaderTests
{
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "triangle.fbx");

    [Fact]
    public void LoadModel_ImportsTrianglePositionsNormalsUvsAndNodeTransform()
    {
        ModelData model = FbxModelLoader.LoadModel(FixturePath);

        Assert.Equal(3, model.Vertices.Length);
        Assert.Equal(3, model.Indices.Length);
        Assert.All(model.Indices, index => Assert.InRange(index, 0u, (uint)model.Vertices.Length - 1));
        Assert.All(model.Vertices, vertex => Assert.Equal(1f, vertex.Normal.Length, 4));
        Assert.Contains(model.Vertices, vertex => vertex.UV == new Engine.Mathematics.Vector2(1f, 1f));
        Assert.Contains(model.Vertices, vertex => vertex.UV == new Engine.Mathematics.Vector2(0f, 1f));
        Assert.Contains(model.Vertices, vertex => vertex.UV == new Engine.Mathematics.Vector2(0f, 0f));
        Assert.All(model.Vertices, vertex => Assert.InRange(vertex.Position.X, 9.999f, 11.001f));
        Assert.Single(model.Parts);
        Assert.Equal("TriangleNode", model.Parts[0].Name);
        Assert.Equal(0, model.Parts[0].FirstIndex);
        Assert.Equal(3, model.Parts[0].IndexCount);
        var extracted = model.ExtractPart(model.Parts[0].Id);
        Assert.Equal(3, extracted.Vertices.Length);
        Assert.Equal(3, extracted.Indices.Length);
    }

    [Fact]
    public void LoadModel_CancelledImportStopsBeforeNativeImport()
    {
        Assert.Throws<OperationCanceledException>(() => FbxModelLoader.LoadModel(FixturePath, new CancellationToken(true)));
    }

    [Fact]
    public void ModelCache_ColdAndWarmLoadsUseFbxImporter()
    {
        string directory = Path.Combine(Path.GetTempPath(), "EngineFbxCacheTests", Guid.NewGuid().ToString("N"));
        try
        {
            ModelData cold = ModelCache.Load(FixturePath, directory, out bool first);
            ModelData warm = ModelCache.Load(FixturePath, directory, out bool second);

            Assert.False(first);
            Assert.True(second);
            Assert.Equal(cold.Indices, warm.Indices);
            Assert.Equal(cold.Parts, warm.Parts);
            Assert.Equal(cold.Vertices.Select(vertex => vertex.Position), warm.Vertices.Select(vertex => vertex.Position));
            Assert.Equal(cold.Vertices.Select(vertex => vertex.UV), warm.Vertices.Select(vertex => vertex.UV));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }
}
