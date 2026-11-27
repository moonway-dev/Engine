using System.Reflection;
using System.Runtime.InteropServices;
using Engine.Backends.Null;
using Engine.Core;
using Engine.Mathematics;
using Engine.Rendering;
using Engine.RHI;
using Engine.Shaders;
using Xunit;

namespace Engine.Tests.Rendering;

public sealed class RtDenoiserTests
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

    [Fact]
    public void RecordedHistorySwapsAllThreeBuffersTogether()
    {
        using NullDevice device = new();
        using RtDenoiserHistory history = new();
        history.Recreate(device, new Size2D(32, 16));
        history.Prepare(new Size2D(16, 8));
        ITexture color = history.Color.WriteTexture;
        ITexture moments = history.Moments.WriteTexture;
        ITexture geometry = history.Geometry.WriteTexture;
        Assert.False(history.IsValid);
        history.Recorded = true;
        history.Commit();
        Assert.True(history.IsValid);
        Assert.Same(color, history.Color.ReadTexture);
        Assert.Same(moments, history.Moments.ReadTexture);
        Assert.Same(geometry, history.Geometry.ReadTexture);
        history.Prepare(new Size2D(16, 8));
        Assert.True(history.IsValid);
        Assert.False(history.Recorded);
    }

    [Fact]
    public void SkippedPassInvalidatesHistoryWithoutSwappingTextures()
    {
        using NullDevice device = new();
        using RtDenoiserHistory history = new();
        history.Recreate(device, new Size2D(16, 16));
        history.Prepare(new Size2D(16, 16));
        history.Recorded = true;
        history.Commit();
        ITexture texture = history.Color.ReadTexture;
        history.Prepare(new Size2D(16, 16));
        history.Commit();
        Assert.False(history.IsValid);
        Assert.Same(texture, history.Color.ReadTexture);
    }

    [Fact]
    public void AdaptiveResolutionChangeInvalidatesAccumulation()
    {
        using NullDevice device = new();
        using RtDenoiserHistory history = new();
        history.Recreate(device, new Size2D(64, 64));
        history.Prepare(new Size2D(64, 64));
        history.Recorded = true;
        history.Commit();
        history.Prepare(new Size2D(32, 32));
        Assert.False(history.IsValid);
        Assert.Equal(new Size2D(32, 32), history.SourceExtent);
        Assert.Equal(new Size2D(64, 64), history.Extent);
    }

    [Fact]
    public void ExternalColorHistoryIsResizedButNotOwned()
    {
        using NullDevice device = new();
        using TemporalHistory color = new();
        color.Recreate(device, new Size2D(16, 16));
        RtDenoiserHistory history = new(color);
        history.Recreate(device, new Size2D(32, 32));
        Assert.Equal(32UL, color.ReadTexture.Width);
        history.Dispose();
        color.Commit();
        Assert.True(color.IsValid);
    }

    [Fact]
    public void ReapplyingUnchangedResolutionDoesNotDiscardGiHistory()
    {
        using NullDevice device = new();
        RendererModule renderer = new(new RhiModule(new NullRHIFactory()), ShaderDirectory());
        TemporalHistory history = (TemporalHistory)typeof(RendererModule)
            .GetField("_sceneGiHistory", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer)!;
        using (history)
        {
            history.Recreate(device, new Size2D(16, 16));
            history.Commit();
            renderer.RayTracingResolutionScale = 1f;
            Assert.True(history.IsValid);
            renderer.RayTracingResolutionScale = 0.5f;
            Assert.False(history.IsValid);
        }
    }

    [Theory]
    [InlineData(0, "ShadowBlur", "ShadowStabilize")]
    [InlineData(2, "GiPreFilter", "GiTemporal")]
    public void DenoiserGraphFiltersCurrentFrameBeforeRecordingHistory(int effect, string firstPass, string temporalName)
    {
        using NullDevice device = new();
        using RenderGraph graph = new(device, new LoggerFactory(new MemoryLogSink()).CreateLogger("RenderGraph"));
        RendererModule renderer = new(new RhiModule(new NullRHIFactory()), ShaderDirectory());
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(string name, object value) => typeof(RendererModule).GetField(name, flags)!.SetValue(renderer, value);
        Set("_graph", graph);
        Set("_device", device);
        Set("_rtDepth", graph.DeclareTransientTexture("Depth", TextureFormat.D32Float, 32, 16));
        Set("_rtNormalRoughness", graph.DeclareTransientTexture("Normal", TextureFormat.R16G16B16A16Float, 32, 16));
        Set("_rtMotion", graph.DeclareTransientTexture("Motion", TextureFormat.R16G16B16A16Float, 32, 16));
        Set(effect == 0 ? "_rtVisibilityValidity" : "_rtGiValidity", graph.DeclareTransientTexture("Validity", TextureFormat.R16G16B16A16Float, 16, 8));
        using TemporalHistory gi = new();
        if (effect == 2)
        {
            gi.Recreate(device, new Size2D(32, 16));
            Set("_sceneGiHistory", gi);
            Set("_sceneGiHistoryRead", graph.ImportTexture("GiRead", gi.ReadTexture, ImageLayout.Undefined));
            Set("_sceneGiHistoryWrite", graph.ImportTexture("GiWrite", gi.WriteTexture, ImageLayout.Undefined));
        }
        var histories = (Dictionary<(bool, int), RtDenoiserHistory>)typeof(RendererModule).GetField("_rtDenoisers", flags)!.GetValue(renderer)!;
        try
        {
            RenderGraphResource source = graph.DeclareTransientTexture("TraceOutput", TextureFormat.R16G16B16A16Float, 16, 8);
            RenderGraphPass trace = graph.AddComputePass("Trace", (_, _) => { });
            trace.ShouldExecute = () => false;
            graph.WritesStorage(trace, source);
            RenderGraphPass composite = graph.AddComputePass("Composite", (_, _) => { });
            object?[] args = [false, effect, source, trace, composite, null];
            var output = (RenderGraphResource)typeof(RendererModule).GetMethod("AddRtDenoiseChain", flags)!.Invoke(renderer, args)!;
            var temporal = Assert.IsType<RenderGraphPass>(args[5]);
            Assert.Equal($"RtScene{temporalName}", temporal.Name);
            RenderGraphPass prefilter = Assert.Single(graph.Passes, pass => pass.Name == $"RtScene{firstPass}");
            Assert.True(prefilter.DeclarationIndex < temporal.DeclarationIndex);
            Assert.Same(trace.ShouldExecute, prefilter.ShouldExecute);
            Assert.Same(trace.ShouldExecute, temporal.ShouldExecute);
            Assert.Contains(prefilter.Accesses, access => access.Resource == source && access.Usage == ResourceUsageKind.Sampled);
            RenderResourceAccess filtered = Assert.Single(prefilter.Accesses, access => access.Usage == ResourceUsageKind.StorageWrite);
            Assert.Equal(new Size2D(32, 16), filtered.Resource.Description.Extent);
            Assert.Contains(temporal.Accesses, access => access.Resource == filtered.Resource && access.Usage == ResourceUsageKind.Sampled);
            RenderGraphResource moments = Assert.Single(temporal.Accesses, access => access.Resource.Name.EndsWith("MomentsWrite")).Resource;
            Assert.DoesNotContain(prefilter.Accesses, access => access.Resource == moments);
            Assert.Single(graph.Passes, pass => pass.Accesses.Any(access => access.Resource == moments && access.Usage == ResourceUsageKind.StorageWrite));
            Assert.Equal(effect == 0 ? 0 : 3, graph.Passes.Count(pass => pass.Accesses.Any(access => access.Resource == moments && access.Usage == ResourceUsageKind.Sampled)));
            Assert.Equal(new Size2D(32, 16), output.Description.Extent);
        }
        finally
        {
            foreach (RtDenoiserHistory history in histories.Values)
                history.Dispose();
        }
    }

    [Fact]
    public void TemporalDenoiserShaderAndHostConstantsAgree()
    {
        string cache = Path.Combine(Path.GetTempPath(), "EngineRtDenoiserTests", Guid.NewGuid().ToString("N"));
        try
        {
            SlangShaderCompiler compiler = new(cacheDirectory: cache);
            ShaderProgramDescription program = compiler.CompileProgram(Path.Combine(ShaderDirectory(), "RtTemporal.slang"),
                [(ShaderStage.Compute, "csMain")]);
            DescriptorSetLayout set = Assert.Single(program.Reflection.DescriptorSets);
            Assert.Equal(11, set.Bindings.Count);
            Assert.All(set.Bindings.Where(binding => binding.Binding < 8), binding => Assert.Equal(DescriptorType.SampledImage, binding.Type));
            Assert.All(set.Bindings.Where(binding => binding.Binding >= 8), binding => Assert.Equal(DescriptorType.StorageImage, binding.Type));
            Assert.Equal(24, Marshal.SizeOf<RtDenoiseConstants>());
            Assert.Equal(24u, Assert.Single(program.Reflection.PushConstants).Size);
            Assert.Equal(16, Marshal.OffsetOf<RtDenoiseConstants>(nameof(RtDenoiseConstants.HistoryValid)).ToInt32());
            ShaderProgramDescription prepass = compiler.CompileProgram(Path.Combine(ShaderDirectory(), "RtPrepass.slang"),
                [(ShaderStage.Vertex, "vsMain"), (ShaderStage.Fragment, "psMain")]);
            Assert.Equal(128u, Assert.Single(prepass.Reflection.PushConstants).Size);
        }
        finally
        {
            if (Directory.Exists(cache))
                Directory.Delete(cache, true);
        }
    }
}
