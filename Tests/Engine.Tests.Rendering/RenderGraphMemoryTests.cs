using Engine.Backends.Null;
using Engine.Core;
using Engine.Mathematics;
using Engine.RHI;
using Engine.Rendering;
using Xunit;

namespace Engine.Tests.Rendering;

public class RenderGraphMemoryTests
{
    private static RenderGraph CreateGraph(IDevice? device = null) =>
        new(device ?? new NullDevice(), new LoggerFactory(new MemoryLogSink()).CreateLogger("RenderGraph"));

    [Fact]
    public void Compile_NontextureHazardsShareTextureDependenciesAndStableSorter()
    {
        using RenderGraph graph = CreateGraph();
        RenderGraphBufferResource buffer = graph.ImportBuffer("Buffer", new TrackedBuffer(new BufferDescription { Size = 256, Usage = BufferUsage.Storage }));
        RenderGraphAccelerationStructureResource accelerationStructure = graph.ImportAccelerationStructure("AS", new TrackedAccelerationStructure());
        RenderGraphResource texture = graph.DeclareTransientTexture("Output", TextureFormat.R8G8B8A8UNorm, 8, 8);
        RenderGraphPass read = graph.AddGraphicsPass("Read", (_, _) => { });
        RenderGraphPass write = graph.AddComputePass("Write", (_, _) => { });
        RenderGraphPass build = graph.AddAccelerationStructureBuildPass("Build", (_, _) => { });
        RenderGraphPass trace = graph.AddRayTracingPass("Trace", (_, _) => { });
        RenderGraphPass overwrite = graph.AddComputePass("Overwrite", (_, _) => { });
        RenderGraphPass sample = graph.AddGraphicsPass("Sample", (_, _) => { });
        graph.ReadsStorage(read, buffer);
        graph.WritesStorage(write, buffer);
        graph.ReadsStorage(build, buffer);
        graph.WritesAccelerationStructure(build, accelerationStructure);
        graph.ReadsAccelerationStructure(trace, accelerationStructure);
        graph.WritesStorage(trace, texture);
        graph.WritesStorage(overwrite, buffer);
        graph.ReadsShader(sample, texture);
        RenderGraphCompilation compilation = graph.Compile();
        Assert.Equal(["Read", "Write", "Build", "Trace", "Overwrite", "Sample"], compilation.OrderedPasses.Select(pass => pass.Name));
        Assert.Contains(read, write.Dependencies);
        Assert.Contains(write, build.Dependencies);
        Assert.Contains(write, overwrite.Dependencies);
        Assert.Contains(build, overwrite.Dependencies);
        Assert.Contains(build, trace.Dependencies);
        Assert.Contains(trace, sample.Dependencies);
        Assert.Equal(PipelineStages.RayTracingShader, Assert.Single(trace.CompiledAccesses).State.Stages);
        RenderGraphMemoryBarrier barrier = Assert.Single(trace.MemoryBarriersBefore);
        Assert.Equal(new RenderGraphMemoryState(PipelineStages.AccelerationStructureBuild, ResourceAccess.AccelerationStructureWrite), barrier.SourceState);
        Assert.Equal(new RenderGraphMemoryState(PipelineStages.RayTracingShader, ResourceAccess.AccelerationStructureRead), barrier.DestinationState);
        graph.DependsOn(write, build);
        Assert.Contains("cycle", Assert.Throws<RenderGraphValidationException>(() => graph.Compile()).Message);
    }

    [Fact]
    public void Compile_ValidatesNontextureDeclarations()
    {
        using RenderGraph graph = CreateGraph();
        RenderGraphBufferResource buffer = graph.DeclareTransientBuffer("Input", new BufferDescription { Size = 256, Usage = BufferUsage.None });
        RenderGraphPass pass = graph.AddComputePass("Read", (_, _) => { });
        graph.ReadsStorage(pass, buffer);
        Assert.Contains("before", Assert.Throws<RenderGraphValidationException>(() => graph.Compile()).Message);
        graph.ClearPasses();
        pass = graph.AddRayTracingPass("WrongKind", (_, _) => { });
        graph.UsesAccelerationStructureScratch(pass, buffer);
        Assert.Contains("build pass", Assert.Throws<RenderGraphValidationException>(() => graph.Compile()).Message);
        graph.ClearPasses();
        pass = graph.AddComputePass("Conflict", (_, _) => { });
        graph.WritesStorage(pass, buffer);
        graph.ReadsStorage(pass, buffer);
        Assert.Contains("conflicting", Assert.Throws<RenderGraphValidationException>(() => graph.Compile()).Message);
        graph.ClearPasses();
        RenderGraphBufferResource imported = graph.ImportBuffer("MissingUsage", new TrackedBuffer(new BufferDescription { Size = 256, Usage = BufferUsage.TransferDestination }));
        pass = graph.AddComputePass("MissingUsage", (_, _) => { });
        graph.ReadsStorage(pass, imported);
        Assert.Contains("Storage", Assert.Throws<RenderGraphValidationException>(() => graph.Compile()).Message);
    }

    [Fact]
    public void Execute_TransientBuffersHaveDistinctAllocationsAndGraphOwnership()
    {
        TrackingDevice device = new();
        using RecordingCommandBuffer commands = new();
        RenderGraph graph = CreateGraph(device);
        RenderGraphBufferResource first = graph.DeclareTransientBuffer("First", new BufferDescription { Size = 256, Usage = BufferUsage.None, Memory = MemoryUsage.GpuOnly });
        RenderGraphBufferResource second = graph.DeclareTransientBuffer("Second", first.Description);
        RenderGraphBufferResource unused = graph.DeclareTransientBuffer("Unused", first.Description);
        RenderGraphPass buildFirst = graph.AddAccelerationStructureBuildPass("First.Build", (_, g) => Assert.Same(first.Buffer, g.GetBuffer(first)));
        RenderGraphPass buildSecond = graph.AddAccelerationStructureBuildPass("Second.Build", (_, _) => { });
        graph.UsesAccelerationStructureScratch(buildFirst, first);
        graph.UsesAccelerationStructureScratch(buildSecond, second);
        graph.Execute(commands);
        Assert.Equal(0, first.FirstUse);
        Assert.Equal(0, first.LastUse);
        Assert.Equal(1, second.FirstUse);
        Assert.Equal(1, second.LastUse);
        Assert.Equal(BufferUsage.Storage | BufferUsage.DeviceAddress, first.CompiledDescription.Usage);
        Assert.NotSame(first.Buffer, second.Buffer);
        Assert.NotEqual(first.PhysicalAllocationId, second.PhysicalAllocationId);
        Assert.False(first.IsAliased);
        Assert.Null(unused.Buffer);
        Assert.Equal(2, device.Buffers.Count);
        IBuffer original = first.Buffer!;
        graph.Execute(commands);
        Assert.Same(original, first.Buffer);
        Assert.Equal(2, device.Buffers.Count);
        graph.ClearPasses();
        RenderGraphPass copy = graph.AddTransferPass("Copy", (_, _) => { });
        graph.WritesTransfer(copy, first);
        graph.Execute(commands);
        Assert.NotSame(original, first.Buffer);
        Assert.Equal(0, device.Buffers[0].DisposeCalls);
        graph.Dispose();
        Assert.All(device.Buffers, buffer => Assert.Equal(1, buffer.DisposeCalls));
        Assert.Null(first.Buffer);
        Assert.Null(second.Buffer);
    }

    [Fact]
    public void Execute_BuildToTraceAndScratchReuseRecordTypedBarriers()
    {
        using RenderGraph graph = CreateGraph();
        using RecordingCommandBuffer commands = new();
        TrackedAccelerationStructure bottom = new();
        RenderGraphAccelerationStructureResource blas = graph.ImportAccelerationStructure("BLAS", bottom);
        RenderGraphAccelerationStructureResource tlas = graph.ImportAccelerationStructure("TLAS", new TrackedAccelerationStructure(AccelerationStructureType.TopLevel));
        RenderGraphBufferResource scratch = graph.DeclareTransientBuffer("Scratch", new BufferDescription { Size = 512, Usage = BufferUsage.None });
        RenderGraphBufferResource input = graph.ImportBuffer("Input", new TrackedBuffer(new BufferDescription { Size = 256, Usage = BufferUsage.AccelerationStructureBuildInput | BufferUsage.DeviceAddress }), new RenderGraphMemoryState(PipelineStages.Host, ResourceAccess.HostWrite));
        RenderGraphBufferResource sbt = graph.ImportBuffer("SBT", new TrackedBuffer(new BufferDescription { Size = 256, Usage = BufferUsage.ShaderBindingTable | BufferUsage.DeviceAddress }), new RenderGraphMemoryState(PipelineStages.Host, ResourceAccess.HostWrite));
        List<string> executed = [];
        RenderGraphPass buildBottom = graph.AddAccelerationStructureBuildPass("BLAS.Build", (_, g) =>
        {
            Assert.Same(bottom, g.GetAccelerationStructure(blas));
            executed.Add("BLAS.Build");
        });
        RenderGraphPass buildTop = graph.AddAccelerationStructureBuildPass("TLAS.Build", (_, _) => executed.Add("TLAS.Build"));
        RenderGraphPass trace = graph.AddRayTracingPass("Trace", (_, _) => executed.Add("Trace"));
        graph.ReadsAccelerationStructureBuildInput(buildBottom, input);
        graph.UsesAccelerationStructureScratch(buildBottom, scratch);
        graph.WritesAccelerationStructure(buildBottom, blas);
        graph.ReadsAccelerationStructure(buildTop, blas);
        graph.UsesAccelerationStructureScratch(buildTop, scratch);
        graph.WritesAccelerationStructure(buildTop, tlas);
        graph.ReadsAccelerationStructure(trace, tlas);
        graph.ReadsShaderBindingTable(trace, sbt);
        graph.Execute(commands);
        Assert.Equal(["BLAS.Build", "TLAS.Build", "Trace"], executed);
        Assert.Contains(commands.BufferBarriers, barrier => ReferenceEquals(barrier.Buffer, scratch.Buffer)
            && barrier.Source == new RenderGraphMemoryState(PipelineStages.AccelerationStructureBuild, ResourceAccess.AccelerationStructureRead | ResourceAccess.AccelerationStructureWrite)
            && barrier.Destination == barrier.Source);
        Assert.Contains(commands.MemoryBarriers, barrier => barrier.Source == new RenderGraphMemoryState(PipelineStages.AccelerationStructureBuild, ResourceAccess.AccelerationStructureWrite)
            && barrier.Destination == new RenderGraphMemoryState(PipelineStages.RayTracingShader, ResourceAccess.AccelerationStructureRead));
        Assert.Contains(commands.BufferBarriers, barrier => ReferenceEquals(barrier.Buffer, sbt.Buffer)
            && barrier.Destination == new RenderGraphMemoryState(PipelineStages.RayTracingShader, ResourceAccess.ShaderRead));
    }

    [Fact]
    public void Compile_PassesInsertedBeforeForwardPreserveTransientWritersAndRtBarriers()
    {
        using RenderGraph graph = CreateGraph();
        using RecordingCommandBuffer commands = new();
        RenderGraphResource depth = graph.DeclareTransientTexture("Depth", new TextureDescription
        {
            Extent = new Size2D(8, 8),
            Format = TextureFormat.D32Float,
        });
        RenderGraphResource normal = graph.DeclareTransientTexture("Normal", TextureFormat.R16G16B16A16Float, 8, 8);
        RenderGraphResource visibility = graph.DeclareTransientTexture("Visibility", TextureFormat.R16G16B16A16Float, 8, 8);
        RenderGraphAccelerationStructureResource tlas = graph.ImportAccelerationStructure("TLAS", new TrackedAccelerationStructure(AccelerationStructureType.TopLevel));
        RenderGraphBufferResource instances = graph.ImportBuffer("Instances", new TrackedBuffer(new BufferDescription
        {
            Size = 256,
            Usage = BufferUsage.AccelerationStructureBuildInput | BufferUsage.DeviceAddress,
        }));
        RenderGraphBufferResource sbt = graph.ImportBuffer("SBT", new TrackedBuffer(new BufferDescription
        {
            Size = 256,
            Usage = BufferUsage.ShaderBindingTable | BufferUsage.DeviceAddress,
        }));
        RenderGraphBufferResource scratch = graph.DeclareTransientBuffer("Scratch", new BufferDescription { Size = 256, Usage = BufferUsage.None });
        List<string> executed = [];

        RenderGraphPass forward = graph.AddGraphicsPass("Forward", (_, _) => executed.Add("Forward"));
        graph.ReadsShader(forward, visibility);
        graph.ReadsShader(forward, depth);

        RenderGraphPass trace = graph.AddRayTracingPassBefore(forward, "Trace", (_, _) => executed.Add("Trace"));
        graph.ReadsAccelerationStructure(trace, tlas);
        graph.ReadsShaderBindingTable(trace, sbt);
        graph.ReadsShader(trace, depth);
        graph.ReadsShader(trace, normal);
        graph.WritesStorage(trace, visibility);

        RenderGraphPass build = graph.AddAccelerationStructureBuildPassBefore(trace, "Build", (_, _) => executed.Add("Build"));
        graph.ReadsAccelerationStructureBuildInput(build, instances);
        graph.UsesAccelerationStructureScratch(build, scratch);
        graph.WritesAccelerationStructure(build, tlas);

        RenderGraphPass prepass = graph.AddGraphicsPassBefore(build, "Prepass", (_, _) => executed.Add("Prepass"));
        graph.WritesColor(prepass, normal);
        graph.WritesDepth(prepass, depth);

        RenderGraphCompilation compilation = graph.Compile();
        Assert.Equal(["Prepass", "Build", "Trace", "Forward"], compilation.OrderedPasses.Select(pass => pass.Name));
        Assert.Equal(Enumerable.Range(0, 4), graph.Passes.Select(pass => pass.DeclarationIndex));
        Assert.Contains(build, trace.Dependencies);
        Assert.Contains(trace, forward.Dependencies);
        Assert.Contains(prepass, trace.Dependencies);
        Assert.Contains(trace.MemoryBarriersBefore, barrier => ReferenceEquals(barrier.Resource, tlas)
            && barrier.SourceState.Access == ResourceAccess.AccelerationStructureWrite
            && barrier.DestinationState.Access == ResourceAccess.AccelerationStructureRead);

        graph.Execute(commands);
        Assert.Equal(["Prepass", "Build", "Trace", "Forward"], executed);
    }

    [Fact]
    public void Execute_SkippedBuildDoesNotChangeAccelerationStructureOrScratchState()
    {
        using RenderGraph graph = CreateGraph();
        using RecordingCommandBuffer commands = new();
        RenderGraphMemoryState initial = new(PipelineStages.RayTracingShader, ResourceAccess.AccelerationStructureRead);
        RenderGraphAccelerationStructureResource accelerationStructure = graph.ImportAccelerationStructure("AS", new TrackedAccelerationStructure(), initial);
        RenderGraphBufferResource scratch = graph.DeclareTransientBuffer("Scratch", new BufferDescription { Size = 256, Usage = BufferUsage.None });
        bool enabled = false;
        int calls = 0;
        RenderGraphPass build = graph.AddAccelerationStructureBuildPass("Build", (_, _) => calls++);
        build.ShouldExecute = () => enabled;
        graph.WritesAccelerationStructure(build, accelerationStructure);
        graph.UsesAccelerationStructureScratch(build, scratch);
        RenderGraphPass trace = graph.AddRayTracingPass("Trace", (_, _) => { });
        graph.ReadsAccelerationStructure(trace, accelerationStructure);
        RenderGraphCompilation compilation = graph.Compile();
        graph.Execute(commands);
        Assert.Empty(commands.MemoryBarriers);
        Assert.Empty(commands.BufferBarriers);
        Assert.Equal(RenderGraphMemoryState.Undefined, scratch.CurrentState);
        Assert.Equal(-1, scratch.LastUsedPass);
        Assert.Equal(0, calls);
        enabled = true;
        graph.Execute(commands);
        Assert.Equal(1, calls);
        Assert.Contains(commands.MemoryBarriers, barrier => barrier.Source.Access == ResourceAccess.AccelerationStructureWrite && barrier.Destination == initial);
        Assert.Same(compilation, graph.Compilation);
    }

    [Fact]
    public void Execute_BufferWarBarrierIncludesAllPriorReaderStages()
    {
        using RenderGraph graph = CreateGraph();
        using RecordingCommandBuffer commands = new();
        RenderGraphBufferResource buffer = graph.ImportBuffer("Buffer", new TrackedBuffer(new BufferDescription { Size = 256, Usage = BufferUsage.Storage }));
        RenderGraphPass vertexRead = graph.AddGraphicsPass("Vertex", (_, _) => { });
        RenderGraphPass traceRead = graph.AddRayTracingPass("Trace", (_, _) => { });
        RenderGraphPass write = graph.AddComputePass("Write", (_, _) => { });
        graph.ReadsStorage(vertexRead, buffer, PipelineStages.VertexShader);
        graph.ReadsStorage(traceRead, buffer);
        graph.WritesStorage(write, buffer);
        graph.Execute(commands);
        var barrier = commands.BufferBarriers.Last();
        Assert.True((barrier.Source.Stages & PipelineStages.VertexShader) != 0);
        Assert.True((barrier.Source.Stages & PipelineStages.RayTracingShader) != 0);
        Assert.Equal(new RenderGraphMemoryState(PipelineStages.ComputeShader, ResourceAccess.ShaderWrite), barrier.Destination);
    }

    [Fact]
    public void RebindImports_PreserveCompilationStatesAndCallerOwnership()
    {
        RenderGraph graph = CreateGraph();
        using RecordingCommandBuffer commands = new();
        BufferDescription description = new() { Size = 256, Usage = BufferUsage.TransferDestination | BufferUsage.Storage };
        TrackedBuffer firstBuffer = new(description);
        TrackedBuffer secondBuffer = new(description);
        TrackedAccelerationStructure firstAs = new();
        TrackedAccelerationStructure secondAs = new();
        RenderGraphMemoryState initialBuffer = new(PipelineStages.Host, ResourceAccess.HostWrite);
        RenderGraphMemoryState finalBuffer = new(PipelineStages.ComputeShader, ResourceAccess.ShaderRead);
        RenderGraphMemoryState initialAs = new(PipelineStages.AccelerationStructureBuild, ResourceAccess.AccelerationStructureWrite);
        RenderGraphMemoryState finalAs = new(PipelineStages.RayTracingShader, ResourceAccess.AccelerationStructureRead);
        RenderGraphBufferResource buffer = graph.ImportBuffer("Buffer", firstBuffer, initialBuffer, finalBuffer);
        RenderGraphAccelerationStructureResource accelerationStructure = graph.ImportAccelerationStructure("AS", firstAs, initialAs, finalAs);
        RenderGraphPass copy = graph.AddTransferPass("Copy", (_, _) => { });
        graph.WritesTransfer(copy, buffer);
        RenderGraphPass trace = graph.AddRayTracingPass("Trace", (_, _) => { });
        graph.ReadsAccelerationStructure(trace, accelerationStructure);
        RenderGraphCompilation compilation = graph.Compile();
        graph.RebindImportedBuffer(buffer, secondBuffer);
        graph.RebindImportedAccelerationStructure(accelerationStructure, secondAs);
        Assert.Same(compilation, graph.Compilation);
        graph.Execute(commands);
        Assert.Same(secondBuffer, graph.GetBuffer(buffer));
        Assert.Same(secondAs, graph.GetAccelerationStructure(accelerationStructure));
        Assert.Equal(finalBuffer, buffer.CurrentState);
        Assert.Equal(finalAs, accelerationStructure.CurrentState);
        Assert.All(commands.BufferBarriers, barrier => Assert.Same(secondBuffer, barrier.Buffer));
        Assert.Equal(initialBuffer, commands.BufferBarriers[0].Source);
        Assert.Equal(finalBuffer, commands.BufferBarriers.Last().Destination);
        Assert.Throws<ArgumentException>(() => graph.RebindImportedBuffer(buffer, new TrackedBuffer(description with { Size = 128 })));
        Assert.Throws<ArgumentException>(() => graph.RebindImportedAccelerationStructure(accelerationStructure, new TrackedAccelerationStructure(AccelerationStructureType.TopLevel)));
        Assert.Throws<InvalidOperationException>(() => graph.RefreshImportedBufferShape(graph.DeclareTransientBuffer("Transient", description), secondBuffer, initialBuffer));
        graph.SetImportedStates(buffer, finalBuffer);
        Assert.False(graph.IsCompiled);
        graph.Dispose();
        Assert.Equal(0, firstBuffer.DisposeCalls);
        Assert.Equal(0, secondBuffer.DisposeCalls);
        Assert.Equal(0, firstAs.DisposeCalls);
        Assert.Equal(0, secondAs.DisposeCalls);
        Assert.Same(secondBuffer, buffer.Buffer);
        Assert.Same(secondAs, accelerationStructure.AccelerationStructure);
    }

    [Fact]
    public void RefreshImportedShapes_RecompilesAndRetainsTransientTextures()
    {
        RenderGraph graph = CreateGraph();
        using RecordingCommandBuffer commands = new();
        TrackedBuffer firstBuffer = new(new BufferDescription { Size = 256, Usage = BufferUsage.Storage });
        TrackedBuffer resizedBuffer = new(new BufferDescription { Size = 512, Usage = BufferUsage.Storage | BufferUsage.DeviceAddress });
        TrackedAccelerationStructure firstAs = new(size: 256);
        TrackedAccelerationStructure resizedAs = new(AccelerationStructureType.TopLevel, size: 512);
        RenderGraphBufferResource buffer = graph.ImportBuffer("Buffer", firstBuffer);
        RenderGraphAccelerationStructureResource accelerationStructure = graph.ImportAccelerationStructure("AS", firstAs);
        RenderGraphResource output = graph.DeclareTransientTexture("Output", TextureFormat.R8G8B8A8UNorm, 8, 8);
        RenderGraphPass trace = graph.AddRayTracingPass("Trace", (_, _) => { });
        graph.ReadsStorage(trace, buffer);
        graph.ReadsAccelerationStructure(trace, accelerationStructure);
        graph.WritesStorage(trace, output);

        graph.Execute(commands);
        ITexture pooledTexture = graph.GetTexture(output);
        RenderGraphCompilation firstCompilation = graph.Compilation!;

        graph.RefreshImportedBufferShape(buffer, resizedBuffer, RenderGraphMemoryState.Undefined);
        graph.RefreshImportedAccelerationStructureShape(accelerationStructure, resizedAs, RenderGraphMemoryState.Undefined);

        Assert.False(graph.IsCompiled);
        graph.Execute(commands);

        Assert.NotSame(firstCompilation, graph.Compilation);
        Assert.Equal(new BufferDescription { Size = 512, Usage = BufferUsage.Storage | BufferUsage.DeviceAddress }, buffer.Description);
        Assert.Same(resizedBuffer, graph.GetBuffer(buffer));
        Assert.Equal(AccelerationStructureType.TopLevel, accelerationStructure.Type);
        Assert.Equal(512ul, accelerationStructure.Size);
        Assert.Same(resizedAs, graph.GetAccelerationStructure(accelerationStructure));
        Assert.Same(pooledTexture, graph.GetTexture(output));
        graph.Dispose();
        Assert.Equal(0, firstBuffer.DisposeCalls);
        Assert.Equal(0, resizedBuffer.DisposeCalls);
        Assert.Equal(0, firstAs.DisposeCalls);
        Assert.Equal(0, resizedAs.DisposeCalls);
    }

    [Fact]
    public void Recompile_ReusesPoolLeasesAndPreservesPinnedTexturesAfterLazyBuildInsertion()
    {
        TrackingDevice device = new();
        using RenderGraph graph = CreateGraph(device);
        using RecordingCommandBuffer commands = new();
        TextureDescription description = new()
        {
            Extent = new Size2D(8, 8),
            Format = TextureFormat.R8G8B8A8UNorm,
            Usage = TextureUsage.Storage | TextureUsage.Sampled,
        };
        RenderGraphResource scene = graph.DeclareTransientTexture("Scene", description);
        RenderGraphResource game = graph.DeclareTransientTexture("Game", description);
        RenderGraphPass sceneWrite = graph.AddComputePass("Scene.Write", (_, _) => { });
        RenderGraphPass gameWrite = graph.AddComputePass("Game.Write", (_, _) => { });
        graph.WritesStorage(sceneWrite, scene);
        graph.WritesStorage(gameWrite, game);
        graph.Compile();
        graph.Realize(scene);
        ITexture originalScene = graph.GetTexture(scene);
        graph.Realize(game);
        ITexture originalGame = graph.GetTexture(game);
        Assert.Same(originalScene, graph.GetTexture(scene));
        Assert.NotSame(originalScene, originalGame);
        Assert.Equal(2, device.Textures.Count);

        for (int slot = 0; slot < 4; slot++)
        {
            RenderGraphAccelerationStructureResource topLevel = graph.ImportAccelerationStructure($"TLAS.{slot}", new TrackedAccelerationStructure());
            RenderGraphPass build = graph.AddAccelerationStructureBuildPassBefore(sceneWrite, $"Build.{slot}", (_, _) => { });
            graph.WritesAccelerationStructure(build, topLevel);
            graph.ReadsAccelerationStructure(sceneWrite, topLevel);
            graph.Execute(commands);
            Assert.Same(originalScene, graph.GetTexture(scene));
            Assert.Same(originalGame, graph.GetTexture(game));
            Assert.Equal(2, device.Textures.Count);
        }
    }

    [Fact]
    public void Recompile_ReleasedTextureCanBeReusedByReplacementResource()
    {
        TrackingDevice device = new();
        using RenderGraph graph = CreateGraph(device);
        using RecordingCommandBuffer commands = new();
        RenderGraphResource first = graph.DeclareTransientTexture("First", TextureFormat.R8G8B8A8UNorm, 8, 8);
        RenderGraphPass write = graph.AddComputePass("Write", (_, _) => { });
        graph.WritesStorage(write, first);
        graph.Execute(commands);
        ITexture original = graph.GetTexture(first);
        graph.ClearPasses();
        RenderGraphResource replacement = graph.DeclareTransientTexture("Replacement", first.Description);
        write = graph.AddComputePass("Replacement.Write", (_, _) => { });
        graph.WritesStorage(write, replacement);
        graph.Execute(commands);
        Assert.Null(first.Texture);
        Assert.Same(original, graph.GetTexture(replacement));
        Assert.Single(device.Textures);
    }

    [Fact]
    public void Realize_ReplacesIncompatibleTextureWithoutReusingItsLease()
    {
        TrackingDevice device = new();
        using RenderGraph graph = CreateGraph(device);
        RenderGraphResource output = graph.DeclareTransientTexture("Output", TextureFormat.R8G8B8A8UNorm, 8, 8);
        graph.Realize(output);
        ITexture original = graph.GetTexture(output);
        RenderGraphPass write = graph.AddComputePass("Write", (_, _) => { });
        graph.WritesStorage(write, output);
        graph.Realize(output);
        Assert.NotSame(original, graph.GetTexture(output));
        Assert.Equal(TextureUsage.Storage, output.CompiledDescription.Usage);
        Assert.Equal(2, device.Textures.Count);
        Assert.Equal(0, device.Textures[0].DisposeCalls);
    }

    [Fact]
    public void Execute_SkippedTextureWriterDoesNotTransitionInitializedStorageReaders()
    {
        using RenderGraph graph = CreateGraph();
        using RecordingCommandBuffer commands = new();
        RenderGraphResourceState initial = new(ImageLayout.General, PipelineStages.ComputeShader, ResourceAccess.ShaderRead);
        RenderGraphResource texture = graph.ImportTexture("Storage", new NullTexture(new TextureDescription
        {
            Extent = new Size2D(8, 8),
            Format = TextureFormat.R8G8B8A8UNorm,
        }), initial, initial);
        RenderGraphPass firstRead = graph.AddComputePass("First.Read", (_, _) => { });
        RenderGraphPass skipped = graph.AddGraphicsPass("Skipped.Write", (_, _) => Assert.Fail("Skipped pass executed."));
        RenderGraphPass secondRead = graph.AddComputePass("Second.Read", (_, _) => { });
        graph.ReadsStorage(firstRead, texture);
        graph.WritesColor(skipped, texture);
        graph.ReadsStorage(secondRead, texture);
        skipped.ShouldExecute = () => false;
        graph.Execute(commands);
        Assert.Empty(commands.ImageBarriers);
        Assert.Equal(initial, texture.CurrentState);
        Assert.Equal(secondRead.ExecutionIndex, texture.LastUsedPass);
    }

    [Fact]
    public void Execute_TextureWarBarrierIncludesAllInitializedStorageReaders()
    {
        using RenderGraph graph = CreateGraph();
        using RecordingCommandBuffer commands = new();
        RenderGraphResource texture = graph.ImportTexture("Storage", new NullTexture(new TextureDescription
        {
            Extent = new Size2D(8, 8),
            Format = TextureFormat.R8G8B8A8UNorm,
        }), new RenderGraphResourceState(ImageLayout.General, PipelineStages.VertexShader, ResourceAccess.ShaderRead));
        RenderGraphPass vertexRead = graph.AddGraphicsPass("Vertex.Read", (_, _) => { });
        RenderGraphPass traceRead = graph.AddRayTracingPass("Trace.Read", (_, _) => { });
        RenderGraphPass write = graph.AddComputePass("Write", (_, _) => { });
        graph.ReadsStorage(vertexRead, texture, PipelineStages.VertexShader);
        graph.ReadsStorage(traceRead, texture);
        graph.WritesStorage(write, texture);
        graph.Compile();
        Assert.Equal(PipelineStages.VertexShader | PipelineStages.RayTracingShader, Assert.Single(write.BarriersBefore).SourceStages);
        graph.Execute(commands);
        Assert.Equal(PipelineStages.VertexShader | PipelineStages.RayTracingShader, commands.ImageBarriers.Last().Source.Stages);
    }

    private sealed class TrackedTexture(in TextureDescription description) : ITexture
    {
        public TextureFormat Format { get; } = description.Format;
        public uint Width { get; } = (uint)description.Extent.Width;
        public uint Height { get; } = (uint)description.Extent.Height;
        public uint MipLevels { get; } = description.MipLevels;
        public uint ArrayLayers { get; } = description.ArrayLayers;
        public TextureDimension Dimension { get; } = description.Dimension;
        public int DisposeCalls { get; private set; }
        public void Dispose() => DisposeCalls++;
    }

    private sealed class TrackedBuffer(in BufferDescription description) : IBuffer
    {
        public ulong Size { get; } = description.Size;
        public BufferUsage Usage { get; } = description.Usage;
        public MemoryUsage Memory { get; } = description.Memory;
        public int DisposeCalls { get; private set; }
        public IntPtr Map() => IntPtr.Zero;
        public void Unmap() { }
        public void Dispose() => DisposeCalls++;
    }

    private sealed class TrackedAccelerationStructure(
        AccelerationStructureType type = AccelerationStructureType.BottomLevel,
        ulong size = 256) : IAccelerationStructure
    {
        public AccelerationStructureType Type { get; } = type;
        public ulong Size { get; } = size;
        public ulong DeviceAddress => 4096;
        public bool IsBuilt => true;
        public int DisposeCalls { get; private set; }
        public void Dispose() => DisposeCalls++;
    }

    private sealed class TrackingDevice : IDevice
    {
        private readonly NullDevice _inner = new();
        public List<TrackedBuffer> Buffers { get; } = [];
        public List<TrackedTexture> Textures { get; } = [];
        public string Name => _inner.Name;
        public FeatureInfo Features => _inner.Features;
        public IMemoryAllocator MemoryAllocator => _inner.MemoryAllocator;
        public IBuffer CreateBuffer(in BufferDescription description)
        {
            TrackedBuffer buffer = new(description);
            Buffers.Add(buffer);
            return buffer;
        }
        public ITexture CreateTexture(in TextureDescription description)
        {
            TrackedTexture texture = new(description);
            Textures.Add(texture);
            return texture;
        }
        public ITextureView CreateTextureView(ITexture texture, TextureViewType type) => _inner.CreateTextureView(texture, type);
        public ITextureView CreateTextureView(ITexture texture, in TextureViewDescription description) => _inner.CreateTextureView(texture, description);
        public ISampler CreateSampler(in SamplerDescription description) => _inner.CreateSampler(description);
        public IShaderProgram CreateShaderProgram(in ShaderProgramDescription description) => _inner.CreateShaderProgram(description);
        public IGraphicsPipeline CreateGraphicsPipeline(in GraphicsPipelineDescription description) => _inner.CreateGraphicsPipeline(description);
        public IComputePipeline CreateComputePipeline(in ComputePipelineDescription description) => _inner.CreateComputePipeline(description);
        public ICommandBuffer CreateCommandBuffer() => new RecordingCommandBuffer();
        public void UploadBufferData(IBuffer buffer, ReadOnlySpan<byte> data, ulong bufferOffset = 0) => _inner.UploadBufferData(buffer, data, bufferOffset);
        public void UploadTextureData(ITexture texture, ReadOnlySpan<byte> data) => _inner.UploadTextureData(texture, data);
        public void UploadTextureSubresources(ITexture texture, ReadOnlySpan<TextureSubresourceData> subresources) => _inner.UploadTextureSubresources(texture, subresources);
        public void FlushUploads() => _inner.FlushUploads();
        public void Submit(ICommandBuffer commandBuffer) => _inner.Submit(commandBuffer);
        public void WaitIdle() => _inner.WaitIdle();
        public void Dispose() => _inner.Dispose();
    }

    private sealed class RecordingCommandBuffer : ICommandBuffer
    {
        private readonly NullCommandBuffer _inner = new();
        public List<(IBuffer Buffer, RenderGraphMemoryState Source, RenderGraphMemoryState Destination)> BufferBarriers { get; } = [];
        public List<(RenderGraphMemoryState Source, RenderGraphMemoryState Destination)> MemoryBarriers { get; } = [];
        public List<(ITexture Texture, RenderGraphResourceState Source, RenderGraphResourceState Destination)> ImageBarriers { get; } = [];
        public RecordingCommandBuffer() => _inner.Begin();
        public void MemoryBarrier(PipelineStages sourceStages, ResourceAccess sourceAccess, PipelineStages destinationStages, ResourceAccess destinationAccess) =>
            MemoryBarriers.Add((new(sourceStages, sourceAccess), new(destinationStages, destinationAccess)));
        public void BufferBarrier(IBuffer buffer, ulong size, PipelineStages sourceStages, ResourceAccess sourceAccess, PipelineStages destinationStages, ResourceAccess destinationAccess, ulong offset = 0)
        {
            Assert.Equal(buffer.Size, size);
            Assert.Equal(0ul, offset);
            BufferBarriers.Add((buffer, new(sourceStages, sourceAccess), new(destinationStages, destinationAccess)));
        }
        public void BufferBarrier(IBuffer buffer, ulong size, ulong offset = 0) => _inner.BufferBarrier(buffer, size, offset);
        public void Begin() => _inner.Begin();
        public void End() => _inner.End();
        public void BeginRendering(in RenderPassInfo info) => _inner.BeginRendering(info);
        public void EndRendering() => _inner.EndRendering();
        public void BeginTimestamp(string name) => _inner.BeginTimestamp(name);
        public void EndTimestamp() => _inner.EndTimestamp();
        public void SetViewport(in Viewport viewport) => _inner.SetViewport(viewport);
        public void SetScissor(in Rect scissor) => _inner.SetScissor(scissor);
        public void BindGraphicsPipeline(IGraphicsPipeline pipeline) => _inner.BindGraphicsPipeline(pipeline);
        public void BindComputePipeline(IComputePipeline pipeline) => _inner.BindComputePipeline(pipeline);
        public void BindDescriptorSet(IDescriptorSet set, uint setIndex, ReadOnlySpan<uint> dynamicOffsets = default) => _inner.BindDescriptorSet(set, setIndex, dynamicOffsets);
        public void BindVertexBuffer(uint slot, IBuffer buffer, ulong offset = 0) => _inner.BindVertexBuffer(slot, buffer, offset);
        public void BindIndexBuffer(IBuffer buffer, IndexType indexType) => _inner.BindIndexBuffer(buffer, indexType);
        public void PushConstants(ReadOnlySpan<byte> data, ShaderStages stages, uint offset = 0) => _inner.PushConstants(data, stages, offset);
        public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0) => _inner.Draw(vertexCount, instanceCount, firstVertex, firstInstance);
        public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int vertexOffset = 0, uint firstInstance = 0) => _inner.DrawIndexed(indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
        public void Dispatch(uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1) => _inner.Dispatch(groupCountX, groupCountY, groupCountZ);
        public void CopyBufferToBuffer(IBuffer source, IBuffer destination, ulong size, ulong sourceOffset = 0, ulong destinationOffset = 0) => _inner.CopyBufferToBuffer(source, destination, size, sourceOffset, destinationOffset);
        public void CopyBufferToImage(IBuffer source, ITexture destination, ulong sourceOffset = 0) => _inner.CopyBufferToImage(source, destination, sourceOffset);
        public void ImageBarrier(ITexture texture, ImageLayout oldLayout, ImageLayout newLayout) => _inner.ImageBarrier(texture, oldLayout, newLayout);
        public void ImageBarrier(ITexture texture, ImageLayout oldLayout, ImageLayout newLayout, PipelineStages sourceStages, ResourceAccess sourceAccess, PipelineStages destinationStages, ResourceAccess destinationAccess, TextureSubresourceRange subresources = default) =>
            ImageBarriers.Add((texture, new(oldLayout, sourceStages, sourceAccess), new(newLayout, destinationStages, destinationAccess)));
        public void CopyImage(ITexture source, ImageLayout sourceLayout, ITexture destination, ImageLayout destinationLayout) => _inner.CopyImage(source, sourceLayout, destination, destinationLayout);
        public void Dispose() => _inner.Dispose();
    }
}
