using Engine.Backends.Null;
using Engine.Core;
using Engine.Mathematics;
using Engine.RHI;
using Engine.Rendering;
using Xunit;

namespace Engine.Tests.Rendering;

public class RenderGraphTests
{
    private static (RenderGraph Graph, NullDevice Device, NullCommandBuffer CommandBuffer) CreateGraph()
    {
        NullDevice device = new();
        ILogger log = new LoggerFactory(new MemoryLogSink()).CreateLogger("RenderGraph");
        RenderGraph graph = new(device, log);
        NullCommandBuffer commandBuffer = (NullCommandBuffer)device.CreateCommandBuffer();
        commandBuffer.Begin();
        return (graph, device, commandBuffer);
    }

    private static ITexture CreateTexture(
        NullDevice device,
        int width = 16,
        int height = 16,
        TextureFormat format = TextureFormat.R8G8B8A8UNorm,
        TextureUsage usage = TextureUsage.None) =>
        device.CreateTexture(new TextureDescription
        {
            Extent = new Size2D(width, height),
            Format = format,
            Usage = usage,
        });

    [Fact]
    public void Compile_RejectsReadBeforeWriteForTransientTexture()
    {
        (RenderGraph graph, _, _) = CreateGraph();
        using (graph)
        {
            RenderGraphResource texture = graph.DeclareTransientTexture("Transient", TextureFormat.R8G8B8A8UNorm, 32, 32);
            RenderGraphPass read = graph.AddGraphicsPass("Read", (_, _) => { });
            graph.ReadsShader(read, texture);

            RenderGraphValidationException exception = Assert.Throws<RenderGraphValidationException>(() => graph.Compile());

            Assert.Contains("Read", exception.Message);
            Assert.Contains("Transient", exception.Message);
            Assert.Contains("before", exception.Message);
        }
    }

    [Fact]
    public void Compile_AllowsImportedTextureReadWithoutGraphWriter()
    {
        (RenderGraph graph, NullDevice device, _) = CreateGraph();
        using ITexture imported = CreateTexture(device);
        using (graph)
        {
            RenderGraphResource texture = graph.ImportTexture("Imported", imported, ImageLayout.ShaderReadOnlyOptimal);
            RenderGraphPass read = graph.AddGraphicsPass("Read", (_, _) => { });
            graph.ReadsShader(read, texture);

            RenderGraphCompilation compilation = graph.Compile();

            Assert.Single(compilation.OrderedPasses);
            Assert.Empty(read.BarriersBefore);
        }
    }

    [Fact]
    public void Compile_RejectsConflictingAccessesWithinPass()
    {
        (RenderGraph graph, _, _) = CreateGraph();
        using (graph)
        {
            RenderGraphResource texture = graph.DeclareTransientTexture("Texture", TextureFormat.R8G8B8A8UNorm, 32, 32);
            RenderGraphPass pass = graph.AddComputePass("Conflict", (_, _) => { });
            graph.WritesStorage(pass, texture);
            graph.ReadsStorage(pass, texture);

            RenderGraphValidationException exception = Assert.Throws<RenderGraphValidationException>(() => graph.Compile());

            Assert.Contains("Conflict", exception.Message);
            Assert.Contains("Texture", exception.Message);
            Assert.Contains("StorageReadWrite", exception.Message);
        }
    }

    [Fact]
    public void Compile_AcceptsSingleStorageReadWriteDeclaration()
    {
        (RenderGraph graph, NullDevice device, _) = CreateGraph();
        using ITexture imported = CreateTexture(device, usage: TextureUsage.Storage);
        using (graph)
        {
            RenderGraphResource texture = graph.ImportTexture("Storage", imported, ImageLayout.General);
            RenderGraphPass pass = graph.AddComputePass("Update", (_, _) => { });
            graph.ReadsWritesStorage(pass, texture);

            RenderGraphCompilation compilation = graph.Compile();

            RenderGraphCompiledAccess access = Assert.Single(compilation.OrderedPasses[0].CompiledAccesses);
            Assert.True(access.Reads);
            Assert.True(access.Writes);
            Assert.Equal(ResourceAccess.ShaderRead | ResourceAccess.ShaderWrite, access.State.Access);
            Assert.Equal(PipelineStages.ComputeShader, access.State.Stages);
        }
    }

    [Fact]
    public void Compile_UsesStableTopologicalOrderForIndependentPasses()
    {
        (RenderGraph graph, _, _) = CreateGraph();
        using (graph)
        {
            RenderGraphResource a = graph.DeclareTransientTexture("A", TextureFormat.R8G8B8A8UNorm, 8, 8);
            RenderGraphResource b = graph.DeclareTransientTexture("B", TextureFormat.R8G8B8A8UNorm, 8, 8);
            RenderGraphPass aWrite = graph.AddGraphicsPass("A.Write", (_, _) => { });
            RenderGraphPass bWrite = graph.AddGraphicsPass("B.Write", (_, _) => { });
            RenderGraphPass aRead = graph.AddGraphicsPass("A.Read", (_, _) => { });
            graph.WritesColor(aWrite, a);
            graph.WritesColor(bWrite, b);
            graph.ReadsShader(aRead, a);

            RenderGraphCompilation compilation = graph.Compile();

            Assert.Equal(["A.Write", "B.Write", "A.Read"], compilation.OrderedPasses.Select(pass => pass.Name));
            Assert.Equal([0, 1, 2], compilation.OrderedPasses.Select(pass => pass.ExecutionIndex));
            Assert.Contains(aWrite, aRead.Dependencies);
            Assert.Empty(bWrite.Dependencies);
        }
    }

    [Fact]
    public void Compile_ExplicitDependencyCanReorderPassesStably()
    {
        (RenderGraph graph, _, _) = CreateGraph();
        using (graph)
        {
            RenderGraphPass first = graph.AddTransferPass("First", (_, _) => { });
            RenderGraphPass second = graph.AddTransferPass("Second", (_, _) => { });
            RenderGraphPass third = graph.AddTransferPass("Third", (_, _) => { });
            graph.DependsOn(first, third);

            RenderGraphCompilation compilation = graph.Compile();

            Assert.Equal(["Second", "Third", "First"], compilation.OrderedPasses.Select(pass => pass.Name));
            Assert.Contains(third, first.Dependencies);
        }
    }

    [Fact]
    public void Compile_RejectsDependencyCycle()
    {
        (RenderGraph graph, _, _) = CreateGraph();
        using (graph)
        {
            RenderGraphPass a = graph.AddTransferPass("A", (_, _) => { });
            RenderGraphPass b = graph.AddTransferPass("B", (_, _) => { });
            graph.DependsOn(a, b);
            graph.DependsOn(b, a);

            RenderGraphValidationException exception = Assert.Throws<RenderGraphValidationException>(() => graph.Compile());

            Assert.Contains("cycle", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("A", exception.Message);
            Assert.Contains("B", exception.Message);
        }
    }

    [Fact]
    public void Compile_ComputesFirstAndLastUseInExecutionOrder()
    {
        (RenderGraph graph, _, _) = CreateGraph();
        using (graph)
        {
            RenderGraphResource source = graph.DeclareTransientTexture("Source", TextureFormat.R8G8B8A8UNorm, 8, 8);
            RenderGraphResource result = graph.DeclareTransientTexture("Result", TextureFormat.R8G8B8A8UNorm, 8, 8);
            RenderGraphPass produce = graph.AddComputePass("Produce", (_, _) => { });
            RenderGraphPass consume = graph.AddComputePass("Consume", (_, _) => { });
            RenderGraphPass finish = graph.AddTransferPass("Finish", (_, _) => { });
            graph.WritesStorage(produce, source);
            graph.ReadsStorage(consume, source);
            graph.WritesStorage(consume, result);
            graph.ReadsTransfer(finish, result);

            graph.Compile();

            Assert.Equal(0, source.FirstUse);
            Assert.Equal(1, source.LastUse);
            Assert.Equal(1, result.FirstUse);
            Assert.Equal(2, result.LastUse);
        }
    }

    [Fact]
    public void Compile_DerivesTextureUsageFromAllDeclaredUses()
    {
        (RenderGraph graph, _, _) = CreateGraph();
        using (graph)
        {
            RenderGraphResource texture = graph.DeclareTransientTexture("Texture", new TextureDescription
            {
                Extent = new Size2D(8, 8),
                Format = TextureFormat.R8G8B8A8UNorm,
            });
            RenderGraphPass write = graph.AddComputePass("Write", (_, _) => { });
            RenderGraphPass sample = graph.AddGraphicsPass("Sample", (_, _) => { });
            RenderGraphPass copy = graph.AddTransferPass("Copy", (_, _) => { });
            graph.WritesStorage(write, texture);
            graph.ReadsShader(sample, texture);
            graph.ReadsTransfer(copy, texture);

            graph.Compile();

            Assert.Equal(
                TextureUsage.Storage | TextureUsage.Sampled | TextureUsage.TransferSource,
                texture.CompiledDescription.Usage);
        }
    }

    [Fact]
    public void Compile_AliasesExactDescriptionOnlyAfterLifetimeEnds()
    {
        (RenderGraph graph, _, _) = CreateGraph();
        using (graph)
        {
            RenderGraphResource first = graph.DeclareTransientTexture("First", new TextureDescription
            {
                Extent = new Size2D(16, 16),
                Format = TextureFormat.R8G8B8A8UNorm,
                Usage = TextureUsage.ColorAttachment | TextureUsage.Sampled,
            });
            RenderGraphResource overlapping = graph.DeclareTransientTexture("Overlapping", new TextureDescription
            {
                Extent = new Size2D(16, 16),
                Format = TextureFormat.R8G8B8A8UNorm,
                Usage = TextureUsage.ColorAttachment | TextureUsage.Sampled,
            });
            RenderGraphResource later = graph.DeclareTransientTexture("Later", new TextureDescription
            {
                Extent = new Size2D(16, 16),
                Format = TextureFormat.R8G8B8A8UNorm,
                Usage = TextureUsage.ColorAttachment | TextureUsage.Sampled,
            });
            RenderGraphPass firstWrite = graph.AddGraphicsPass("First.Write", (_, _) => { });
            RenderGraphPass overlappingWrite = graph.AddGraphicsPass("Overlap.Write", (_, _) => { });
            RenderGraphPass firstRead = graph.AddGraphicsPass("First.Read", (_, _) => { });
            RenderGraphPass laterWrite = graph.AddGraphicsPass("Later.Write", (_, _) => { });
            graph.WritesColor(firstWrite, first);
            graph.WritesColor(overlappingWrite, overlapping);
            graph.ReadsShader(firstRead, first);
            graph.ReadsShader(firstRead, overlapping);
            graph.WritesColor(laterWrite, later);

            graph.Compile();

            Assert.NotEqual(first.PhysicalAllocationId, overlapping.PhysicalAllocationId);
            Assert.Equal(first.PhysicalAllocationId, later.PhysicalAllocationId);
            Assert.True(later.IsAliased);
            Assert.False(overlapping.IsAliased);
        }
    }

    [Fact]
    public void Compile_DoesNotAliasDifferentDescriptionsOrUsageFlags()
    {
        (RenderGraph graph, _, _) = CreateGraph();
        using (graph)
        {
            RenderGraphResource color = graph.DeclareTransientTexture("Color", TextureFormat.R8G8B8A8UNorm, 16, 16);
            RenderGraphResource differentSize = graph.DeclareTransientTexture("DifferentSize", TextureFormat.R8G8B8A8UNorm, 32, 16);
            RenderGraphResource differentUsage = graph.DeclareTransientTexture("DifferentUsage", TextureFormat.R8G8B8A8UNorm, 16, 16);
            RenderGraphPass colorWrite = graph.AddGraphicsPass("Color.Write", (_, _) => { });
            RenderGraphPass sizeWrite = graph.AddGraphicsPass("Size.Write", (_, _) => { });
            RenderGraphPass storageWrite = graph.AddComputePass("Storage.Write", (_, _) => { });
            graph.WritesColor(colorWrite, color);
            graph.WritesColor(sizeWrite, differentSize);
            graph.WritesStorage(storageWrite, differentUsage);

            graph.Compile();

            Assert.NotEqual(color.PhysicalAllocationId, differentSize.PhysicalAllocationId);
            Assert.NotEqual(color.PhysicalAllocationId, differentUsage.PhysicalAllocationId);
        }
    }

    [Fact]
    public void Execute_ReusesAliasedPhysicalTexture()
    {
        (RenderGraph graph, _, NullCommandBuffer commandBuffer) = CreateGraph();
        using (graph)
        {
            RenderGraphResource first = graph.DeclareTransientTexture("First", TextureFormat.R8G8B8A8UNorm, 16, 16);
            RenderGraphResource later = graph.DeclareTransientTexture("Later", TextureFormat.R8G8B8A8UNorm, 16, 16);
            RenderGraphPass firstPass = graph.AddGraphicsPass("First", (_, _) => { });
            RenderGraphPass laterPass = graph.AddGraphicsPass("Later", (_, _) => { });
            graph.WritesColor(firstPass, first);
            graph.WritesColor(laterPass, later);

            graph.Execute(commandBuffer);

            Assert.NotNull(first.Texture);
            Assert.Same(first.Texture, later.Texture);
            Assert.Equal(first.PhysicalAllocationId, later.PhysicalAllocationId);
        }
    }

    [Fact]
    public void Realize_PinsTextureAgainstAliasingAndKeepsCompatibility()
    {
        (RenderGraph graph, _, NullCommandBuffer commandBuffer) = CreateGraph();
        using (graph)
        {
            RenderGraphResource first = graph.DeclareTransientTexture("First", TextureFormat.R8G8B8A8UNorm, 16, 16);
            RenderGraphResource later = graph.DeclareTransientTexture("Later", TextureFormat.R8G8B8A8UNorm, 16, 16);
            graph.Realize(first);
            RenderGraphPass firstPass = graph.AddGraphicsPass("First", (_, _) => { });
            RenderGraphPass laterPass = graph.AddGraphicsPass("Later", (_, _) => { });
            graph.WritesColor(firstPass, first);
            graph.WritesColor(laterPass, later);

            graph.Execute(commandBuffer);

            Assert.NotNull(first.Texture);
            Assert.NotSame(first.Texture, later.Texture);
        }
    }

    [Fact]
    public void Compile_ProducesPreciseGraphicsComputeTransferAndPresentBarriers()
    {
        (RenderGraph graph, NullDevice device, _) = CreateGraph();
        using ITexture imported = CreateTexture(
            device,
            usage: TextureUsage.ColorAttachment | TextureUsage.Sampled | TextureUsage.Storage | TextureUsage.TransferSource | TextureUsage.TransferDestination);
        using (graph)
        {
            RenderGraphResource texture = graph.ImportTexture(
                "Imported",
                imported,
                new RenderGraphResourceState(ImageLayout.PresentSrcKhr, PipelineStages.Bottom, ResourceAccess.None),
                new RenderGraphResourceState(ImageLayout.PresentSrcKhr, PipelineStages.Bottom, ResourceAccess.None));
            RenderGraphPass draw = graph.AddGraphicsPass("Draw", (_, _) => { });
            RenderGraphPass compute = graph.AddComputePass("Compute", (_, _) => { });
            RenderGraphPass copy = graph.AddTransferPass("Copy", (_, _) => { });
            graph.WritesColor(draw, texture);
            graph.ReadsStorage(compute, texture);
            graph.ReadsTransfer(copy, texture);

            RenderGraphCompilation compilation = graph.Compile();

            RenderGraphBarrier drawBarrier = Assert.Single(draw.BarriersBefore);
            Assert.Equal(ImageLayout.PresentSrcKhr, drawBarrier.OldLayout);
            Assert.Equal(ImageLayout.ColorAttachmentOptimal, drawBarrier.NewLayout);
            Assert.Equal(PipelineStages.Bottom, drawBarrier.SourceStages);
            Assert.Equal(ResourceAccess.None, drawBarrier.SourceAccess);
            Assert.Equal(PipelineStages.ColorOutput, drawBarrier.DestinationStages);
            Assert.Equal(ResourceAccess.ColorWrite, drawBarrier.DestinationAccess);

            RenderGraphBarrier computeBarrier = Assert.Single(compute.BarriersBefore);
            Assert.Equal(ImageLayout.ColorAttachmentOptimal, computeBarrier.OldLayout);
            Assert.Equal(ImageLayout.General, computeBarrier.NewLayout);
            Assert.Equal(PipelineStages.ColorOutput, computeBarrier.SourceStages);
            Assert.Equal(ResourceAccess.ColorWrite, computeBarrier.SourceAccess);
            Assert.Equal(PipelineStages.ComputeShader, computeBarrier.DestinationStages);
            Assert.Equal(ResourceAccess.ShaderRead, computeBarrier.DestinationAccess);

            RenderGraphBarrier copyBarrier = Assert.Single(copy.BarriersBefore);
            Assert.Equal(ImageLayout.General, copyBarrier.OldLayout);
            Assert.Equal(ImageLayout.TransferSrcOptimal, copyBarrier.NewLayout);
            Assert.Equal(PipelineStages.ComputeShader, copyBarrier.SourceStages);
            Assert.Equal(ResourceAccess.ShaderRead, copyBarrier.SourceAccess);
            Assert.Equal(PipelineStages.Transfer, copyBarrier.DestinationStages);
            Assert.Equal(ResourceAccess.TransferRead, copyBarrier.DestinationAccess);

            RenderGraphBarrier finalBarrier = Assert.Single(compilation.TailBarriers);
            Assert.True(finalBarrier.IsFinalTransition);
            Assert.Equal(ImageLayout.TransferSrcOptimal, finalBarrier.OldLayout);
            Assert.Equal(ImageLayout.PresentSrcKhr, finalBarrier.NewLayout);
            Assert.Equal(PipelineStages.Transfer, finalBarrier.SourceStages);
            Assert.Equal(ResourceAccess.TransferRead, finalBarrier.SourceAccess);
            Assert.Equal(PipelineStages.Bottom, finalBarrier.DestinationStages);
            Assert.Equal(ResourceAccess.None, finalBarrier.DestinationAccess);
        }
    }

    [Fact]
    public void Execute_AppliesImportedFinalState()
    {
        (RenderGraph graph, NullDevice device, NullCommandBuffer commandBuffer) = CreateGraph();
        using ITexture imported = CreateTexture(device, usage: TextureUsage.TransferDestination);
        using (graph)
        {
            RenderGraphResource texture = graph.ImportTexture(
                "Swapchain",
                imported,
                ImageLayout.Undefined,
                ImageLayout.PresentSrcKhr);
            RenderGraphPass copy = graph.AddTransferPass("Copy", (_, _) => { });
            graph.WritesTransfer(copy, texture);

            graph.Execute(commandBuffer);

            Assert.Equal(ImageLayout.PresentSrcKhr, texture.CurrentLayout);
            Assert.Equal(PipelineStages.Bottom, texture.CurrentState.Stages);
            Assert.Equal(ResourceAccess.None, texture.CurrentState.Access);
        }
    }

    [Fact]
    public void RebindImportedTexture_UsesNewTextureAndStateWithoutRecompile()
    {
        (RenderGraph graph, NullDevice device, NullCommandBuffer commandBuffer) = CreateGraph();
        using ITexture first = CreateTexture(device, usage: TextureUsage.TransferDestination);
        using ITexture second = CreateTexture(device, usage: TextureUsage.TransferDestination);
        using (graph)
        {
            RenderGraphResource texture = graph.ImportTexture("Swapchain", first, ImageLayout.Undefined, ImageLayout.PresentSrcKhr);
            RenderGraphPass copy = graph.AddTransferPass("Copy", (_, _) => { });
            graph.WritesTransfer(copy, texture);
            RenderGraphCompilation compilation = graph.Compile();

            graph.RebindImportedTexture(texture, second, ImageLayout.Undefined, ImageLayout.PresentSrcKhr);
            graph.Execute(commandBuffer);

            Assert.Same(compilation, graph.Compilation);
            Assert.Same(second, texture.Texture);
            RenderGraphBarrier barrier = Assert.Single(copy.BarriersBefore);
            Assert.Equal(ImageLayout.Undefined, barrier.OldLayout);
            Assert.Equal(ImageLayout.TransferDstOptimal, barrier.NewLayout);
            Assert.Equal(ImageLayout.PresentSrcKhr, texture.CurrentLayout);
        }
    }

    [Fact]
    public void RebindImportedTexture_RejectsDescriptionMismatch()
    {
        (RenderGraph graph, NullDevice device, _) = CreateGraph();
        using ITexture first = CreateTexture(device, 16, 16);
        using ITexture wrong = CreateTexture(device, 32, 16);
        using (graph)
        {
            RenderGraphResource texture = graph.ImportTexture("Imported", first);

            ArgumentException exception = Assert.Throws<ArgumentException>(() => graph.RebindImportedTexture(texture, wrong));

            Assert.Contains("exactly match", exception.Message);
        }
    }

    [Fact]
    public void Execute_RunsGraphicsComputeAndTransferPasses()
    {
        (RenderGraph graph, _, NullCommandBuffer commandBuffer) = CreateGraph();
        using (graph)
        {
            List<string> execution = [];
            RenderGraphResource graphicsOutput = graph.DeclareTransientTexture("Graphics", TextureFormat.R8G8B8A8UNorm, 16, 16);
            RenderGraphResource computeOutput = graph.DeclareTransientTexture("Compute", TextureFormat.R8G8B8A8UNorm, 16, 16);
            RenderGraphPass graphics = graph.AddGraphicsPass("Graphics", (_, _) => execution.Add("Graphics"));
            RenderGraphPass compute = graph.AddComputePass("Compute", (cmd, _) =>
            {
                execution.Add("Compute");
                cmd.Dispatch(4, 2, 1);
            });
            RenderGraphPass transfer = graph.AddTransferPass("Transfer", (_, _) => execution.Add("Transfer"));
            graph.WritesColor(graphics, graphicsOutput);
            graph.ReadsStorage(compute, graphicsOutput);
            graph.WritesStorage(compute, computeOutput);
            graph.ReadsTransfer(transfer, computeOutput);

            graph.Execute(commandBuffer);

            Assert.Equal(["Graphics", "Compute", "Transfer"], execution);
            Assert.Equal(1u, commandBuffer.DispatchCalls);
            Assert.True(commandBuffer.HasRecorded);
        }
    }

    [Fact]
    public void Execute_CanDisableAndReenablePassWithoutRecompiling()
    {
        (RenderGraph graph, _, NullCommandBuffer commandBuffer) = CreateGraph();
        using (graph)
        {
            bool visible = false;
            int calls = 0;
            RenderGraphResource output = graph.DeclareTransientTexture("Output", TextureFormat.R8G8B8A8UNorm, 16, 16);
            RenderGraphPass pass = graph.AddComputePass("Optional", (cmd, _) =>
            {
                calls++;
                cmd.Dispatch(1, 1, 1);
            });
            pass.ShouldExecute = () => visible;
            graph.WritesStorage(pass, output);
            graph.Execute(commandBuffer);
            Assert.Equal(0, calls);
            Assert.Equal(0u, commandBuffer.DispatchCalls);
            visible = true;
            graph.Execute(commandBuffer);
            Assert.Equal(1, calls);
            Assert.Equal(1u, commandBuffer.DispatchCalls);
            visible = false;
            graph.Execute(commandBuffer);
            Assert.Equal(1, calls);
        }
    }

    [Fact]
    public void Execute_InsertsLayoutTransitionsBetweenPasses()
    {
        (RenderGraph graph, _, NullCommandBuffer commandBuffer) = CreateGraph();
        using (graph)
        {
            RenderGraphResource color = graph.DeclareTransientTexture("Color", TextureFormat.R8G8B8A8UNorm, 64, 64);
            graph.Realize(color);
            Assert.Equal(ImageLayout.Undefined, color.CurrentLayout);
            RenderGraphPass writePass = graph.AddGraphicsPass("Write", (_, _) => { });
            graph.WritesColor(writePass, color, clear: true, Vector4.One);
            RenderGraphPass readPass = graph.AddGraphicsPass("Read", (_, _) => { });
            graph.ReadsShader(readPass, color);

            graph.Execute(commandBuffer);

            Assert.Equal(ImageLayout.ShaderReadOnlyOptimal, color.CurrentLayout);
            Assert.Equal(PipelineStages.FragmentShader, color.CurrentState.Stages);
            Assert.Equal(ResourceAccess.ShaderRead, color.CurrentState.Access);
        }
    }

    [Fact]
    public void WritesColor_OnDepthResource_Throws()
    {
        (RenderGraph graph, _, _) = CreateGraph();
        using (graph)
        {
            RenderGraphResource depth = graph.DeclareTransientTexture("Depth", TextureFormat.D32Float, 64, 64);
            RenderGraphPass pass = graph.AddGraphicsPass("Broken", (_, _) => { });

            Assert.Throws<InvalidOperationException>(() => graph.WritesColor(pass, depth));
        }
    }

    [Fact]
    public void TransientTextures_AreDisposedWithGraph()
    {
        (RenderGraph graph, _, NullCommandBuffer commandBuffer) = CreateGraph();
        RenderGraphResource resource = graph.DeclareTransientTexture("Transient", TextureFormat.R8G8B8A8UNorm, 32, 32);
        RenderGraphPass pass = graph.AddGraphicsPass("Pass", (_, _) => { });
        graph.WritesColor(pass, resource, clear: true, Vector4.Zero);
        graph.Execute(commandBuffer);
        Assert.NotNull(resource.Texture);
        ITexture texture = resource.Texture!;

        graph.Dispose();

        Assert.Null(resource.Texture);
        Assert.NotEqual(0u, texture.Width);
    }

    [Fact]
    public void ImportedTextures_AreNotDisposedOrCleared()
    {
        (RenderGraph graph, NullDevice device, _) = CreateGraph();
        using ITexture imported = CreateTexture(device);
        RenderGraphResource resource = graph.ImportTexture("Imported", imported);
        Assert.False(resource.Transient);

        graph.Dispose();

        Assert.Same(imported, resource.Texture);
        Assert.NotEqual(0u, imported.Width);
    }
}
