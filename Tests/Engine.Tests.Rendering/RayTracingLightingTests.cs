using System.Reflection;
using System.Runtime.InteropServices;
using Engine.Backends.Null;
using Engine.Core;
using Engine.Mathematics;
using Engine.Rendering;
using Engine.RHI;
using Engine.Scene;
using Engine.Shaders;
using Xunit;
using Camera = Engine.Rendering.Camera;

namespace Engine.Tests.Rendering;

public sealed class RayTracingLightingTests
{
    private static string ShaderDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string path = Path.Combine(directory.FullName, "Samples", "Basic3D", "Shaders");
            if (File.Exists(Path.Combine(path, "PBR.slang")))
                return path;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Basic3D shaders were not found.");
    }

    private static IEnumerable<uint[]> Instructions(byte[] spirV)
    {
        uint[] words = MemoryMarshal.Cast<byte, uint>(spirV).ToArray();
        for (int offset = 5; offset < words.Length;)
        {
            int count = (int)(words[offset] >> 16);
            Assert.True(count > 0 && offset + count <= words.Length);
            yield return words[offset..(offset + count)];
            offset += count;
        }
    }

    [Theory]
    [InlineData("RtReflections.slang")]
    [InlineData("RtGi.slang")]
    public void HitLightingTracesVisibilityFromRayGenerationWithoutRecursion(string filename)
    {
        string cache = Path.Combine(Path.GetTempPath(), "EngineRtLightingTests", Guid.NewGuid().ToString("N"));
        try
        {
            SlangShaderCompiler compiler = new(cacheDirectory: cache);
            ShaderProgramDescription program = compiler.CompileProgram(Path.Combine(ShaderDirectory(), filename),
                [(ShaderStage.RayGeneration, "rayGen"), (ShaderStage.Miss, "missMain"), (ShaderStage.ClosestHit, "closestHitMain")],
                new Dictionary<string, string?> { ["RT_MATERIAL_TEXTURE_COUNT"] = "12" });
            List<uint[]> instructions = Instructions(program.Stages.Single(stage => stage.Stage == ShaderStage.RayGeneration).SpirV).ToList();
            Dictionary<uint, uint> constants = instructions.Where(word => (word[0] & 0xffff) == 43 && word.Length == 4)
                .ToDictionary(word => word[2], word => word[3]);
            List<uint[]> traces = instructions.Where(word => (word[0] & 0xffff) == 4445).ToList();
            Assert.Contains(traces, word => constants[word[3]] == MeshRenderingRules.SurfaceRayMask);
            Assert.Contains(traces, word => constants[word[3]] == MeshRenderingRules.ShadowRayMask && constants[word[2]] == 13u);
            Assert.DoesNotContain(Instructions(program.Stages.Single(stage => stage.Stage == ShaderStage.ClosestHit).SpirV),
                word => (word[0] & 0xffff) == 4445);
            DescriptorSetLayout set = program.Reflection.DescriptorSets.Single(layout => layout.SetIndex == 0);
            uint lightBinding = filename == "RtGi.slang" ? 8u : 5u;
            Assert.Equal(ShaderStages.RayGeneration, Assert.Single(set.Bindings, binding => binding.Binding == lightBinding).Stages);
            if (filename == "RtGi.slang")
                Assert.DoesNotContain(set.Bindings, binding => binding.Binding == 10u);
            else
                Assert.Equal(DescriptorType.SampledImage, Assert.Single(set.Bindings, binding => binding.Binding == 10u).Type);
        }
        finally
        {
            if (Directory.Exists(cache))
                Directory.Delete(cache, true);
        }
    }

    [Theory]
    [InlineData(false, false, 4)]
    [InlineData(true, false, 5)]
    [InlineData(false, true, 1)]
    [InlineData(true, true, 1)]
    public void TransparentShaderCannotWriteOpaqueLightingOrMotionAttachments(bool temporal, bool transparent, int expectedOutputs)
    {
        string cache = Path.Combine(Path.GetTempPath(), "EngineRtLightingTests", Guid.NewGuid().ToString("N"));
        try
        {
            SlangShaderCompiler compiler = new(cacheDirectory: cache);
            Dictionary<string, string?> defines = new() { ["SPECULAR_MRT"] = "1", ["RT_SHADOWS"] = "1" };
            if (temporal)
                defines["TEMPORAL_MRT"] = "1";
            if (transparent)
                defines["TRANSPARENT_PASS"] = "1";
            ShaderProgramDescription program = compiler.CompileProgram(Path.Combine(ShaderDirectory(), "PBR.slang"),
                [(ShaderStage.Vertex, "vsMain"), (ShaderStage.Fragment, "psMain")], defines);
            List<uint[]> instructions = Instructions(program.Stages.Single(stage => stage.Stage == ShaderStage.Fragment).SpirV).ToList();
            HashSet<uint> outputs = instructions.Where(word => (word[0] & 0xffff) == 59 && word[3] == 3).Select(word => word[2]).ToHashSet();
            uint[] locations = instructions.Where(word => (word[0] & 0xffff) == 71 && word.Length == 4 && word[2] == 30 && outputs.Contains(word[1]))
                .Select(word => word[3]).Order().ToArray();
            Assert.Equal(Enumerable.Range(0, expectedOutputs).Select(index => (uint)index), locations);
        }
        finally
        {
            if (Directory.Exists(cache))
                Directory.Delete(cache, true);
        }
    }

    [Theory]
    [InlineData(false, 1f, 1)]
    [InlineData(true, 1f, 0)]
    [InlineData(false, 0.4f, 0)]
    public void TransparentMaterialsStayVisibleButDoNotEnterTheAccelerationStructure(bool transparent, float opacity, int tracedCount)
    {
        using NullDevice device = new();
        (Vertex[] vertices, uint[] indices) = MeshPrimitives.Cube();
        using Mesh mesh = new(device, vertices, indices, "RT material eligibility");
        RendererModule renderer = new(new RhiModule(new NullRHIFactory()), ShaderDirectory());
        Material material = new() { Transparent = transparent, Opacity = opacity };
        RenderItem item = new() { Mesh = mesh, Material = material, Position = new Vector3(0f, 0f, -5f) };
        Dictionary<MeshRenderer, RenderItem> items = (Dictionary<MeshRenderer, RenderItem>)typeof(RendererModule)
            .GetField("_sceneItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer)!;
        items.Add(new MeshRenderer(), item);
        SetField(renderer, "_lastRtMasterEnabled", true);
        List<RayTracingDrawItem> visible = [];
        List<RayTracingDrawItem> traced = [];
        typeof(RendererModule).GetMethod("PrepareViewDrawItems", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(renderer, [visible, traced, new Camera(), new Size2D(640, 480)]);
        Assert.Single(visible);
        Assert.Equal(tracedCount, traced.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GlassPassCompositesAfterLightingWithoutOverwritingDepthOrAuxiliaryAttachments(bool gameView)
    {
        using NullDevice device = new();
        using RenderGraph graph = new(device, new LoggerFactory(new MemoryLogSink()).CreateLogger("RT lighting"));
        RendererModule renderer = new(new RhiModule(new NullRHIFactory()), ShaderDirectory());
        SetField(renderer, "_graph", graph);
        Size2D extent = new(64, 64);
        RenderGraphResource color = graph.DeclareTransientTexture("Lit", new TextureDescription { Extent = extent, Format = TextureFormat.R16G16B16A16Float });
        RenderGraphResource depth = graph.DeclareTransientTexture("Depth", new TextureDescription { Extent = extent, Format = TextureFormat.D32Float });
        RenderGraphResource shadow = graph.DeclareTransientTexture("Shadow", new TextureDescription { Extent = extent, Format = TextureFormat.D32Float });
        RenderGraphPass forward = graph.AddGraphicsPass("Opaque", (_, _) => { });
        graph.WritesColor(forward, color, true);
        graph.WritesDepth(forward, depth, true);
        graph.WritesDepth(forward, shadow, true);
        RenderGraphPass bloom = graph.AddComputePass("Bloom", (_, _) => { });
        graph.ReadsSampled(bloom, color);
        SetField(renderer, gameView ? "_gameForwardPass" : "_forwardPass", forward);
        SetField(renderer, gameView ? "_gameDepth" : "_mainDepth", depth);
        SetField(renderer, gameView ? "_gameShadowDepth" : "_shadowDepth", shadow);
        typeof(RendererModule).GetMethod("AddTransparentPass", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(renderer, [gameView, color, bloom]);
        RenderGraphPass glass = graph.Passes.Single(pass => pass.Name.EndsWith("Transparent", StringComparison.Ordinal));
        RenderResourceAccess target = Assert.Single(glass.Accesses, access => access.Usage == ResourceUsageKind.ColorAttachmentWrite);
        Assert.NotSame(color, target.Resource);
        graph.ReadsSampled(bloom, target.Resource);
        RenderGraphPass copy = graph.Passes.Single(pass => pass.Name.EndsWith("TransparencyCopy", StringComparison.Ordinal));
        Assert.Contains(copy.Accesses, access => access.Resource == color && access.Usage == ResourceUsageKind.TransferRead);
        Assert.Contains(copy.Accesses, access => access.Resource == target.Resource && access.Usage == ResourceUsageKind.TransferWrite);
        Assert.False(target.ClearOnLoad);
        Assert.Contains(glass.Accesses, access => access.Resource == depth && access.Usage == ResourceUsageKind.DepthAttachmentRead);
        Assert.DoesNotContain(glass.Accesses, access => access.Usage == ResourceUsageKind.DepthAttachmentWrite);
        Assert.False(glass.ShouldExecute!());
        List<RayTracingDrawItem> draws = (List<RayTracingDrawItem>)typeof(RendererModule)
            .GetField(gameView ? "_gamePreparedDrawItems" : "_scenePreparedDrawItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer)!;
        draws.Add(new RayTracingDrawItem(new RenderItem { Mesh = null!, Material = new Material { Transparent = true } }, null!));
        Assert.True(glass.ShouldExecute!());
        Assert.Equal(new[] { "Opaque", copy.Name, glass.Name, "Bloom" }, graph.Compile().OrderedPasses.Select(pass => pass.Name));
    }

    [Fact]
    public void TransparentMaterialDescriptorUsesItsOwnShaderProgram()
    {
        using NullDevice device = new();
        using MaterialDefaultTextures defaults = MaterialDefaultTextures.Create(device);
        using ISampler sampler = device.CreateSampler(new SamplerDescription());
        using IShaderProgram program = device.CreateShaderProgram(new ShaderProgramDescription { Name = "Glass", Stages = [] });
        RendererModule renderer = new(new RhiModule(new NullRHIFactory()), ShaderDirectory());
        SetField(renderer, "_defaultMaterialTextures", defaults);
        SetField(renderer, "_linearSampler", sampler);
        using Material material = new()
        {
            Transparent = true,
            ParamsBuffer = device.CreateBuffer(new BufferDescription
            {
                Size = (ulong)Marshal.SizeOf<MaterialData>(),
                Usage = BufferUsage.Uniform,
                Memory = MemoryUsage.CpuToGpu,
            }),
        };
        material.TransparentSet = (IDescriptorSet)typeof(RendererModule)
            .GetMethod("CreateTransparentMaterialSet", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(renderer, [program, material])!;
        NullDescriptorSet set = Assert.IsType<NullDescriptorSet>(material.TransparentSet);
        Assert.Same(program, set.Program);
        Assert.Equal(1u, set.SetIndex);
        Assert.Null(material.Set);
        Assert.Null(material.RtSet);
    }

    private static void SetField(RendererModule renderer, string name, object value) =>
        typeof(RendererModule).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(renderer, value);
}
