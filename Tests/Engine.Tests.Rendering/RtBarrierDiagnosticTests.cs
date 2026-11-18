using Engine.Backends.Null;
using Engine.Core;
using Engine.Mathematics;
using Engine.RHI;
using Engine.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace Engine.Tests.Rendering;

public class RtBarrierDiagnosticTests
{
    private readonly ITestOutputHelper _output;

    public RtBarrierDiagnosticTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void DepthWrite_ThenRayTracingRead_EmitsLayoutTransition()
    {
        using NullDevice device = new();
        using RenderGraph graph = new(device, new LoggerFactory(new MemoryLogSink()).CreateLogger("RenderGraph"));

        RenderGraphResource depth = graph.DeclareTransientTexture("Depth", new TextureDescription
        {
            Extent = new Size2D(64, 64),
            Format = TextureFormat.D32Float,
        });
        RenderGraphPass prepass = graph.AddGraphicsPass("Prepass", (_, _) => { });
        graph.WritesDepth(prepass, depth, clear: true, 1f);
        RenderGraphPass trace = graph.AddRayTracingPass("Trace", (_, _) => { });
        graph.ReadsSampled(trace, depth);

        graph.Compile();

        foreach (RenderGraphBarrier barrier in prepass.BarriersBefore)
            _output.WriteLine($"prepass barrier: {barrier.Resource.Name} {barrier.OldLayout} -> {barrier.NewLayout}");
        foreach (RenderGraphBarrier barrier in trace.BarriersBefore)
            _output.WriteLine($"trace barrier: {barrier.Resource.Name} {barrier.OldLayout} -> {barrier.NewLayout}");

        RenderGraphBarrier[] barriers = [.. trace.BarriersBefore];
        Assert.Contains(barriers, barrier =>
            ReferenceEquals(barrier.Resource, depth)
            && barrier.OldLayout == ImageLayout.DepthAttachmentOptimal
            && barrier.NewLayout == ImageLayout.DepthStencilReadOnlyOptimal);
    }
}
