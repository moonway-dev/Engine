using System.Reflection;
using Engine.Backends.Null;
using Engine.Mathematics;
using Engine.Rendering;
using Engine.RHI;
using Engine.Scene;
using Xunit;

namespace Engine.Tests.Rendering;

public sealed class MeshRenderingRulesTests
{
    [Theory]
    [InlineData(MeshRenderingMode.Full, true, true, 3)]
    [InlineData(MeshRenderingMode.Off, false, false, 0)]
    [InlineData(MeshRenderingMode.ShadowsOnly, false, true, 3)]
    [InlineData(MeshRenderingMode.RenderOnly, true, false, 1)]
    public void ModesControlCameraShadowAndReflectionVisibility(MeshRenderingMode mode, bool cameraVisible, bool castsShadows, int rayMask)
    {
        Assert.Equal(cameraVisible, mode.RendersCamera());
        Assert.Equal(castsShadows, mode.CastsShadows());
        Assert.Equal((byte)rayMask, mode.RayTracingMask());
        Assert.Equal(castsShadows, (mode.RayTracingMask() & MeshRenderingRules.ShadowRayMask) != 0);
        Assert.Equal(mode != MeshRenderingMode.Off, (mode.RayTracingMask() & MeshRenderingRules.SurfaceRayMask) != 0);
    }

    [Theory]
    [InlineData(MeshRenderingMode.Full)]
    [InlineData(MeshRenderingMode.Off)]
    [InlineData(MeshRenderingMode.ShadowsOnly)]
    [InlineData(MeshRenderingMode.RenderOnly)]
    public void RenderingModeSurvivesSceneRoundTripAndClone(MeshRenderingMode mode)
    {
        ComponentRegistry registry = new();
        registry.Register(() => new MeshRendererComponent());
        Scene.Scene scene = new("Rendering rules", registry);
        MeshRendererComponent renderer = scene.CreateObject("Mesh").AddComponent<MeshRendererComponent>();
        renderer.RenderingMode = mode;
        Assert.Equal(mode, Scene.Scene.FromJson(scene.ToJson(), registry).FindByName("Mesh")!.GetComponent<MeshRendererComponent>()!.RenderingMode);
        Assert.Equal(mode, scene.Clone().FindByName("Mesh")!.GetComponent<MeshRendererComponent>()!.RenderingMode);
    }

    [Fact]
    public void ExistingScenesDefaultToFullRendering()
    {
        Assert.Equal(MeshRenderingMode.Full, new MeshRendererComponent().RenderingMode);
        Assert.Equal(MeshRenderingMode.Full, new RenderItem { Mesh = null!, Material = null! }.RenderingMode);
        ComponentRegistry registry = new();
        registry.Register(() => new MeshRendererComponent());
        Scene.Scene scene = Scene.Scene.FromJson("""
            { "version": 1, "objects": [{ "name": "Mesh", "components": [{ "type": "MeshRenderer", "properties": { "ResourceKey": "cube" } }] }] }
            """, registry);
        Assert.Equal(MeshRenderingMode.Full, scene.FindByName("Mesh")!.GetComponent<MeshRendererComponent>()!.RenderingMode);
    }

    [Theory]
    [InlineData(MeshRenderingMode.Full, 1, 1)]
    [InlineData(MeshRenderingMode.Off, 0, 0)]
    [InlineData(MeshRenderingMode.ShadowsOnly, 0, 1)]
    [InlineData(MeshRenderingMode.RenderOnly, 1, 1)]
    public void ViewPreparationHidesShadowsOnlyButKeepsItInRayTracing(MeshRenderingMode mode, int visibleCount, int rtCount)
    {
        using NullDevice device = new();
        (Vertex[] vertices, uint[] indices) = MeshPrimitives.Cube();
        using Mesh mesh = new(device, vertices, indices, "Rendering rules test");
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Samples", "Basic3D", "Shaders", "PBR.slang")))
            directory = directory.Parent;
        string shaderDirectory = Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("Basic3D shaders not found."), "Samples", "Basic3D", "Shaders");
        RendererModule renderer = new(new RhiModule(new NullRHIFactory()), shaderDirectory);
        RenderItem item = new() { Mesh = mesh, Material = null!, Position = new Vector3(0f, 0f, -5f), RenderingMode = mode };
        Dictionary<MeshRendererComponent, RenderItem> sceneItems = (Dictionary<MeshRendererComponent, RenderItem>)typeof(RendererModule)
            .GetField("_sceneItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer)!;
        sceneItems.Add(new MeshRendererComponent(), item);
        typeof(RendererModule).GetField("_lastRtMasterEnabled", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(renderer, true);
        List<RayTracingDrawItem> visible = [];
        List<RayTracingDrawItem> traced = [];
        typeof(RendererModule).GetMethod("PrepareViewDrawItems", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(renderer, [visible, traced, new Camera(), new Size2D(640, 480)]);
        Assert.Equal(visibleCount, visible.Count);
        Assert.Equal(rtCount, traced.Count);
    }
}

public sealed class CapsulePrimitiveTests
{
    [Fact]
    public void CapsuleMeshMatchesColliderDimensionsAndHasFiniteNormals()
    {
        (Vertex[] vertices, uint[] indices) = MeshPrimitives.Capsule(0.35f, 1.8f);
        Assert.Equal(-0.9f, vertices.Min(vertex => vertex.Position.Y), 5);
        Assert.Equal(0.9f, vertices.Max(vertex => vertex.Position.Y), 5);
        Assert.Equal(0.35f, vertices.Max(vertex => vertex.Position.X), 5);
        Assert.All(vertices, vertex =>
        {
            Assert.True(float.IsFinite(vertex.Position.X) && float.IsFinite(vertex.Position.Y) && float.IsFinite(vertex.Position.Z));
            Assert.Equal(1f, vertex.Normal.Length, 5);
            Assert.True(float.IsFinite(vertex.Tangent.X) && float.IsFinite(vertex.Tangent.Y) && float.IsFinite(vertex.Tangent.Z));
        });
        Assert.NotEmpty(indices);
        Assert.Equal(0, indices.Length % 3);
        Assert.All(indices, index => Assert.InRange(index, 0u, (uint)vertices.Length - 1u));
    }

    [Theory]
    [InlineData(1.8f)]
    [InlineData(0.7f)]
    public void CapsuleHasRoundedCapsAndNoDegenerateTriangles(float height)
    {
        const float radius = 0.35f;
        float halfLength = height * 0.5f - radius;
        (Vertex[] vertices, uint[] indices) = MeshPrimitives.Capsule(radius, height);
        Assert.Single(vertices, vertex => vertex.Position.Y == height * 0.5f);
        Assert.Single(vertices, vertex => vertex.Position.Y == -height * 0.5f);
        Assert.All(vertices, vertex =>
        {
            Vector3 position = vertex.Position;
            float radialSquared = position.X * position.X + position.Z * position.Z;
            float capOffset = MathF.Max(0f, MathF.Abs(position.Y) - halfLength);
            Assert.Equal(radius * radius, radialSquared + capOffset * capOffset, 5);
        });
        Assert.Contains(vertices, vertex => vertex.Position.Y > halfLength && vertex.Position.Y < height * 0.5f);
        Assert.Contains(vertices, vertex => vertex.Position.Y < -halfLength && vertex.Position.Y > -height * 0.5f);
        for (int i = 0; i < indices.Length; i += 3)
        {
            Vertex a = vertices[indices[i]];
            Vertex b = vertices[indices[i + 1]];
            Vertex c = vertices[indices[i + 2]];
            Vector3 normal = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);
            Assert.True(normal.LengthSquared > 1e-12f);
            Assert.True(Vector3.Dot(normal, a.Normal + b.Normal + c.Normal) > 0f);
        }
    }

    [Fact]
    public void CapsuleRejectsInvalidDimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MeshPrimitives.Capsule(0f, 1.8f));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeshPrimitives.Capsule(0.5f, 0.8f));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeshPrimitives.Capsule(0.35f, 1.8f, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeshPrimitives.Capsule(0.35f, 1.8f, 24, 0));
    }
}
