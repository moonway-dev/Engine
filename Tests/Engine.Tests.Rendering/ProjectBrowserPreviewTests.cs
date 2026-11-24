using Camera = Engine.Rendering.Camera;
using Light = Engine.Rendering.Light;
using System.Reflection;
using Engine.Assets;
using Engine.Backends.Null;
using Engine.Core;
using Engine.Editor;
using Engine.Editor.Documents;
using Engine.Editor.Models;
using Engine.Editor.Panels;
using Engine.Mathematics;
using Engine.Platform;
using Engine.Rendering;
using Engine.RHI;
using Engine.Scene;
using ImGuiNET;
using Xunit;
using NumericsVector2 = System.Numerics.Vector2;

namespace Engine.Tests.Rendering;

public sealed class ProjectBrowserPreviewTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "EngineAssetPreviewTests", Guid.NewGuid().ToString("N"));
    private const string PixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aD1sAAAAASUVORK5CYII=";

    public ProjectBrowserPreviewTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData(50f, 88f, 1)]
    [InlineData(200f, 88f, 2)]
    [InlineData(400f, 88f, 4)]
    [InlineData(400f, 140f, 2)]
    [InlineData(400f, 32f, 1)]
    public void GridFitsWidthAndIncludesIncompleteLastRow(float width, float zoom, int columns)
    {
        ProjectBrowserLayout layout = ProjectBrowserLayout.Create(width, zoom, 14f, 9);
        Assert.Equal(columns, layout.Columns);
        Assert.Equal((9 + columns - 1) / columns, layout.Rows);
        Assert.True(layout.Position(columns - 1).X + layout.CellWidth <= width);
        Assert.Equal(layout.RowHeight, layout.Position(columns).Y);
        Assert.Equal(zoom <= 40f, layout.IsList);
    }

    [Fact]
    public void TextAndTextureFitWithoutWrappingOrDistortion()
    {
        Assert.Equal("Long…", ProjectBrowserLayout.Ellipsize("LongName", 5, value => value.Length));
        Assert.Equal(string.Empty, ProjectBrowserLayout.Ellipsize("Name", 0, value => value.Length));
        Assert.Equal("Name", ProjectBrowserLayout.Ellipsize("Name", 10, value => value.Length));
        Assert.Equal(new NumericsVector2(100, 50), ProjectBrowserLayout.FitImage(400, 200, new NumericsVector2(100)));
        ImageData resized = AssetPreviewCache.Resize(new ImageData(new byte[400 * 200 * 4], 400, 200, 4), 256);
        Assert.Equal(256, resized.Width);
        Assert.Equal(128, resized.Height);
    }

    [Fact]
    public void SearchAndFavoritesUseConsistentClassificationAndNavigation()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Materials"));
        Directory.CreateDirectory(Path.Combine(_root, "Cache"));
        File.WriteAllText(Path.Combine(_root, "Materials", "Red.mat"), "{}");
        File.WriteAllText(Path.Combine(_root, "Player.cs"), string.Empty);
        File.WriteAllText(Path.Combine(_root, "Red.obj"), string.Empty);
        File.WriteAllText(Path.Combine(_root, "Project.project"), "{}");
        File.WriteAllText(Path.Combine(_root, "Red.obj.meta"), "{}");
        ProjectBrowserModel model = new(_root);
        model.Refresh();
        Assert.Equal(AssetKind.Script, ProjectBrowserModel.Classify("Player.cs"));
        Assert.DoesNotContain(model.EnumerateVisible(), entry => entry.Name == "Cache" || entry.Name.EndsWith(".project") || entry.Name.EndsWith(".meta"));
        Assert.True(model.Navigate("Materials/../Materials"));
        Assert.Equal("Materials", model.CurrentDirectory);
        Assert.False(model.Navigate("../"));
        model.Favorites = FavoritesFilter.AllMaterials;
        model.Filter = "Red";
        Assert.Equal("Materials/Red.mat", Assert.Single(model.CurrentEntries()).RelativePath);
        model.Favorites = FavoritesFilter.None;
        Assert.Equal(2, model.CurrentEntries().Count);
    }

    [Theory]
    [InlineData(-1f, -1f, -1f, 1f, 1f, 1f)]
    [InlineData(100f, -2f, -0.1f, 200f, 2f, 0.1f)]
    [InlineData(-0.01f, -100f, -0.01f, 0.01f, 100f, 0.01f)]
    [InlineData(3f, 3f, 3f, 3f, 3f, 3f)]
    public void PreviewCameraContainsAllBounds(float x0, float y0, float z0, float x1, float y1, float z1)
    {
        var frame = AssetPreviewRenderer.Frame(new Vector3(x0, y0, z0), new Vector3(x1, y1, z1));
        Matrix4x4 matrix = frame.Camera.ViewProjection * frame.Model;
        foreach (float x in new[] { x0, x1 })
            foreach (float y in new[] { y0, y1 })
                foreach (float z in new[] { z0, z1 })
                {
                    float w = matrix.M41 * x + matrix.M42 * y + matrix.M43 * z + matrix.M44;
                    float nx = (matrix.M11 * x + matrix.M12 * y + matrix.M13 * z + matrix.M14) / w;
                    float ny = (matrix.M21 * x + matrix.M22 * y + matrix.M23 * z + matrix.M24) / w;
                    float nz = (matrix.M31 * x + matrix.M32 * y + matrix.M33 * z + matrix.M34) / w;
                    Assert.InRange(nx, -1f, 1f);
                    Assert.InRange(ny, -1f, 1f);
                    Assert.InRange(nz, 0f, 1f);
                }
    }

    [Fact]
    public void RetirementWaitsForFullFrameSlotCycle()
    {
        using GpuResourceRetirement retirement = new(3);
        DisposableCounter first = new();
        retirement.BeginFrame(0);
        retirement.Retire(first);
        retirement.BeginFrame(1);
        retirement.BeginFrame(2);
        retirement.BeginFrame(0);
        Assert.Equal(0, first.Count);
        retirement.BeginFrame(1);
        Assert.Equal(1, first.Count);
        retirement.Dispose();
        Assert.Equal(1, first.Count);
    }

    [Fact]
    public void OffscreenPreviewRecordsGeometryAndTonemapWithMaterialParameters()
    {
        using NullDevice device = new();
        using ImageBasedLightingResources ibl = ImageBasedLightingResources.Create(device, null, "unused");
        ILogger log = new LoggerFactory(new MemoryLogSink()).CreateLogger("PreviewTest");
        using AssetPreviewRenderer renderer = new(device, log, ShaderDirectory(), Path.Combine(_root, "Shaders"), ibl);
        Material material = new() { AlbedoColor = new Vector3(0.8f, 0.1f, 0.2f), Metallic = 1f, Roughness = 0.2f };
        using AssetPreviewRenderer.PreviewJob job = renderer.Prepare(null, material, false);
        using ITexture output = job.Output;
        using NullCommandBuffer commands = new();
        commands.Begin();
        job.Record(commands);
        commands.End();
        Assert.Equal(1u, commands.DrawIndexedCalls);
        Assert.Equal(1u, commands.DrawCalls);
        Assert.Equal(256u, output.Width);
        Assert.Equal(TextureFormat.R8G8B8A8Srgb, output.Format);
        MaterialData expected = material.ToData();
        byte[] expectedBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref expected, 1)).ToArray();
        Assert.Contains(device.BufferUploads, upload => upload.Data.AsSpan().SequenceEqual(expectedBytes));
    }

    [Fact]
    public async Task TextureCacheLoadsOnceUpdatesAndEvicts()
    {
        using NullDevice device = new();
        using GpuResourceRetirement retirement = new(2);
        MaterialAssetStore materials = new(_root);
        int registrations = 0;
        int removed = 0;
        using AssetPreviewCache cache = new(device, materials, _root, () => throw new InvalidOperationException(), _ => new IntPtr(++registrations), _ => removed++, retirement) { MaximumEntries = 1 };
        File.WriteAllBytes(Path.Combine(_root, "One.png"), Convert.FromBase64String(PixelPng));
        AssetEntry one = Entry("One.png", AssetKind.Texture);
        await Pump(cache, one);
        Assert.NotEqual(IntPtr.Zero, cache.Request(one).Texture);
        Assert.Equal(1, registrations);
        cache.PrepareFrame();
        Assert.Equal(1, registrations);
        File.SetLastWriteTimeUtc(one.FullPath, DateTime.UtcNow.AddSeconds(5));
        await Task.Delay(270);
        await Pump(cache, one);
        Assert.Equal(2, registrations);
        File.WriteAllBytes(Path.Combine(_root, "Two.png"), Convert.FromBase64String(PixelPng));
        await Pump(cache, Entry("Two.png", AssetKind.Texture));
        Assert.Equal(1, cache.Count);
        Assert.True(removed >= 2);
    }

    [Fact]
    public async Task InvalidTextureDoesNotRetryEveryFrame()
    {
        using NullDevice device = new();
        using GpuResourceRetirement retirement = new(2);
        using AssetPreviewCache cache = new(device, new MaterialAssetStore(_root), _root, () => throw new InvalidOperationException(), _ => new IntPtr(1), _ => { }, retirement);
        File.WriteAllText(Path.Combine(_root, "Broken.png"), "invalid");
        AssetEntry asset = Entry("Broken.png", AssetKind.Texture);
        await Pump(cache, asset);
        Assert.NotNull(cache.Request(asset).Error);
        for (int i = 0; i < 10; i++)
        {
            cache.BeginRequests();
            cache.Request(asset);
            cache.PrepareFrame();
        }
        Assert.Equal(0, cache.PendingCount);
    }

    [Fact]
    public async Task CancelledInvisiblePreviewResumesWhenVisibleAgain()
    {
        using NullDevice device = new();
        using GpuResourceRetirement retirement = new(2);
        using AssetPreviewCache cache = new(device, new MaterialAssetStore(_root), _root, () => throw new InvalidOperationException(), _ => new IntPtr(1), _ => { }, retirement);
        File.WriteAllBytes(Path.Combine(_root, "Delayed.png"), Convert.FromBase64String(PixelPng));
        AssetEntry entry = Entry("Delayed.png", AssetKind.Texture);
        cache.BeginRequests();
        cache.Request(entry);
        cache.BeginRequests();
        cache.BeginRequests();
        cache.PrepareFrame();
        Assert.Equal(0, cache.PendingCount);
        await Pump(cache, entry);
        Assert.NotEqual(IntPtr.Zero, cache.Request(entry).Texture);
    }

    [Fact]
    public void DensePreviewGeometryReducesWithoutChangingSource()
    {
        var sphere = MeshPrimitives.Sphere(1f, 640, 640);
        ModelData source = new(sphere.Vertices, sphere.Indices, []);
        ModelData reduced = ModelPreviewGeometry.Reduce(source);
        Assert.True(reduced.Vertices.Length < source.Vertices.Length);
        Assert.True(reduced.Indices.Length < source.Indices.Length);
        Assert.NotEmpty(reduced.Indices);
        Assert.All(reduced.Indices, index => Assert.True(index < reduced.Vertices.Length));
        Assert.Equal(sphere.Vertices.Length, source.Vertices.Length);
    }

    [Fact]
    public async Task MaterialPreviewUpdatesForParentAndTextureChanges()
    {
        using NullDevice device = new();
        using ImageBasedLightingResources ibl = ImageBasedLightingResources.Create(device, null, "unused");
        using GpuResourceRetirement retirement = new(2);
        MaterialAssetStore materials = new(_root) { RefreshInterval = TimeSpan.Zero };
        File.WriteAllBytes(Path.Combine(_root, "Map.png"), Convert.FromBase64String(PixelPng));
        MaterialAsset parent = new();
        parent.SetOverride(nameof(MaterialProperties.AlbedoTexture), new AssetReference("Map.png"));
        AssetReference parentReference = materials.Create("Parent.mat", parent);
        materials.Create("Variant.mat", new MaterialAsset { Parent = parentReference });
        int registrations = 0;
        ILogger log = new LoggerFactory(new MemoryLogSink()).CreateLogger("PreviewTest");
        using AssetPreviewCache cache = new(device, materials, _root, () => new AssetPreviewRenderer(device, log, ShaderDirectory(), Path.Combine(_root, "Shaders"), ibl), _ => new IntPtr(++registrations), _ => { }, retirement);
        AssetEntry entry = Entry("Variant.mat", AssetKind.Material);
        await Pump(cache, entry);
        using NullCommandBuffer commands = new();
        commands.Begin();
        cache.Record(commands);
        commands.End();
        Assert.Equal(1, registrations);
        parent.SetOverride(nameof(MaterialProperties.Metallic), 1f);
        materials.SetPreview(parentReference, parent);
        await Task.Delay(270);
        await Pump(cache, entry);
        commands.Begin();
        cache.Record(commands);
        commands.End();
        Assert.Equal(2, registrations);
        File.SetLastWriteTimeUtc(Path.Combine(_root, "Map.png"), DateTime.UtcNow.AddSeconds(5));
        await Task.Delay(270);
        await Pump(cache, entry);
        Assert.Equal(3, registrations);
    }

    [Fact]
    public void PanelRequestsOnlyVisibleTiles()
    {
        for (int i = 0; i < 200; i++)
            File.WriteAllText(Path.Combine(_root, $"Texture{i:D3}.png"), string.Empty);
        Engine.Scene.Scene scene = new("Browser");
        using SceneDocument document = new(new DelegatingSceneHost(scene), scene);
        using EditorContext context = new(document, _root);
        int requests = 0;
        ProjectBrowserPanel panel = new(context, previews: _ => { requests++; return default; });
        IntPtr imgui = ImGui.CreateContext();
        try
        {
            ImGuiIOPtr io = ImGui.GetIO();
            io.DisplaySize = new NumericsVector2(800, 600);
            io.DeltaTime = 1f / 60f;
            io.Fonts.AddFontDefault();
            io.Fonts.GetTexDataAsRGBA32(out IntPtr _, out int _, out int _, out int _);
            ImGui.NewFrame();
            ImGui.SetNextWindowPos(NumericsVector2.Zero);
            ImGui.SetNextWindowSize(new NumericsVector2(700, 400));
            panel.Draw();
            ImGui.Render();
            Assert.InRange(requests, 1, 50);
        }
        finally { ImGui.DestroyContext(imgui); }
    }

    private AssetEntry Entry(string name, AssetKind kind) => new(name, Path.Combine(_root, name), name, kind, []);

    private static async Task Pump(AssetPreviewCache cache, AssetEntry entry)
    {
        for (int i = 0; i < 200; i++)
        {
            cache.BeginRequests();
            cache.Request(entry);
            cache.PrepareFrame();
            if (cache.PendingCount == 0)
                return;
            await Task.Delay(5);
        }
        Assert.Fail("Preview did not finish loading.");
    }

    private static string ShaderDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Engine.sln")))
                return Path.Combine(directory.FullName, "Samples", "Basic3D", "Shaders");
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException();
    }

    public void Dispose() => Directory.Delete(_root, true);

    private sealed class DisposableCounter : IDisposable
    {
        public int Count { get; private set; }
        public void Dispose() => Count++;
    }
}
