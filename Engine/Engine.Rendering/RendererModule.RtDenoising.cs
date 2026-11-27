using Engine.Mathematics;
using Engine.RHI;

namespace Engine.Rendering;

public sealed partial class RendererModule
{
    private RenderGraphResource AddRtDenoiseChain(bool gameView, int effect, RenderGraphResource source, RenderGraphPass trace, RenderGraphPass before, out RenderGraphPass? temporalPass)
    {
        string prefix = gameView ? "RtGame" : "RtScene";
        string name = effect == 0 ? "Shadow" : effect == 1 ? "Reflection" : "Gi";
        if (!_rtDenoisers.TryGetValue((gameView, effect), out RtDenoiserHistory? history))
        {
            TemporalHistory? color = effect == 1 ? (gameView ? _gameReflectionHistory : _sceneReflectionHistory)
                : effect == 2 ? (gameView ? _gameGiHistory : _sceneGiHistory) : null;
            history = new RtDenoiserHistory(color);
            _rtDenoisers.Add((gameView, effect), history);
        }
        RenderGraphResource depth = (gameView ? _gameRtDepth : _rtDepth)!;
        RenderGraphResource normal = (gameView ? _gameRtNormalRoughness : _rtNormalRoughness)!;
        RenderGraphResource motion = (gameView ? _gameRtMotion : _rtMotion)!;
        RenderGraphResource validity = effect == 0 ? (gameView ? _gameRtVisibilityValidity : _rtVisibilityValidity)!
            : effect == 1 ? (gameView ? _gameRtReflectionValidity : _rtReflectionValidity)!
            : (gameView ? _gameRtGiValidity : _rtGiValidity)!;
        history.Recreate(_device!, depth.Description.Extent);
        RenderGraphResource colorRead = effect == 1 ? (gameView ? _gameReflectionHistoryRead : _sceneReflectionHistoryRead)!
            : effect == 2 ? (gameView ? _gameGiHistoryRead : _sceneGiHistoryRead)!
            : _graph!.ImportTexture($"{prefix}{name}HistoryRead", history.Color.ReadTexture, ImageLayout.Undefined);
        RenderGraphResource colorWrite = effect == 1 ? (gameView ? _gameReflectionHistoryWrite : _sceneReflectionHistoryWrite)!
            : effect == 2 ? (gameView ? _gameGiHistoryWrite : _sceneGiHistoryWrite)!
            : _graph!.ImportTexture($"{prefix}{name}HistoryWrite", history.Color.WriteTexture, ImageLayout.Undefined);
        RenderGraphResource momentsRead = _graph!.ImportTexture($"{prefix}{name}MomentsRead", history.Moments.ReadTexture, ImageLayout.Undefined);
        RenderGraphResource momentsWrite = _graph.ImportTexture($"{prefix}{name}MomentsWrite", history.Moments.WriteTexture, ImageLayout.Undefined);
        RenderGraphResource geometryRead = _graph.ImportTexture($"{prefix}{name}GeometryRead", history.Geometry.ReadTexture, ImageLayout.Undefined);
        RenderGraphResource geometryWrite = _graph.ImportTexture($"{prefix}{name}GeometryWrite", history.Geometry.WriteTexture, ImageLayout.Undefined);
        _rtDenoiserStates.Add((history, colorRead, colorWrite, momentsRead, momentsWrite, geometryRead, geometryWrite, gameView, effect));
        Size2D SourceExtent(RenderGraphResource input) => ReferenceEquals(input, source)
            ? RayTracingLogicalExtent(gameView, (RayTracingQualityEffect)effect)
            : input.Description.Extent;
        uint mode = effect == 0 ? 4u : effect == 1 ? 1u : 2u;
        RenderGraphResource[] scratch = Enumerable.Range(0, effect == 0 ? 1 : effect == 1 ? 2 : 3).Select(index => _graph.DeclareTransientTexture($"{prefix}{name}Spatial{index}", new TextureDescription
        {
            Extent = depth.Description.Extent,
            Format = TextureFormat.R16G16B16A16Float,
            Usage = TextureUsage.Sampled | TextureUsage.Storage,
        })).ToArray();
        RenderGraphPass AddTemporal(string name, RenderGraphResource input, RenderGraphResource output)
        {
            RenderGraphPass pass = _graph.AddComputePassBefore(before, $"{prefix}{name}", (commandBuffer, graph) =>
            {
                IDescriptorSet set = _rtTemporalProgram!.CreateDescriptorSet(0);
                set.WriteTexture(0, graph.GetTexture(input));
                set.WriteTexture(1, graph.GetTexture(motion));
                set.WriteTexture(2, graph.GetTexture(depth));
                set.WriteTexture(3, graph.GetTexture(normal));
                set.WriteTexture(4, graph.GetTexture(colorRead));
                set.WriteTexture(5, graph.GetTexture(momentsRead));
                set.WriteTexture(6, graph.GetTexture(geometryRead));
                set.WriteTexture(7, graph.GetTexture(validity));
                set.WriteStorageImage(8, graph.GetTexture(output));
                set.WriteStorageImage(9, graph.GetTexture(momentsWrite));
                set.WriteStorageImage(10, graph.GetTexture(geometryWrite));
                _frameSets[_activeFrameSlot].Add(set);
                commandBuffer.BindComputePipeline(_rtTemporalPipeline!);
                commandBuffer.BindDescriptorSet(set, 0);
                Size2D extent = depth.Description.Extent;
                Size2D sourceExtent = SourceExtent(input);
                RtDenoiseConstants constants = new()
                {
                    ExtentWidth = (uint)extent.Width,
                    ExtentHeight = (uint)extent.Height,
                    SourceWidth = (uint)sourceExtent.Width,
                    SourceHeight = (uint)sourceExtent.Height,
                    HistoryValid = history.IsValid ? 1u : 0u,
                    Effect = (uint)effect,
                };
                commandBuffer.PushConstants(StructuredBytes(constants), ShaderStages.Compute);
                commandBuffer.Dispatch((uint)(extent.Width + 7) / 8, (uint)(extent.Height + 7) / 8);
                history.Recorded = true;
            });
            foreach (RenderGraphResource resource in new[] { input, motion, depth, normal, colorRead, momentsRead, geometryRead, validity })
                _graph.ReadsSampled(pass, resource);
            _graph.WritesStorage(pass, output);
            _graph.WritesStorage(pass, momentsWrite);
            _graph.WritesStorage(pass, geometryWrite);
            pass.ShouldExecute = trace.ShouldExecute;
            return pass;
        }

        void AddFilter(string name, RenderGraphResource input, RenderGraphResource output, uint stepWidth, RenderGraphResource? moments = null)
        {
            RenderGraphPass filter = _graph.AddComputePassBefore(before, $"{prefix}{name}", (commandBuffer, graph) =>
                ExecuteRtFilterPass(commandBuffer, graph, input, output, depth, normal, motion, validity,
                    mode, stepWidth, validityEnabled: ReferenceEquals(input, source), logicalSourceExtent: SourceExtent(input),
                    moments: moments, padding2: effect == 0 ? FrameIndex(gameView) : 0u));
            foreach (RenderGraphResource resource in new[] { input, depth, normal, motion, validity })
                _graph.ReadsSampled(filter, resource);
            if (moments is not null)
                _graph.ReadsSampled(filter, moments);
            _graph.WritesStorage(filter, output);
            filter.ShouldExecute = trace.ShouldExecute;
        }

        if (effect == 0)
        {
            AddFilter("ShadowBlur", source, scratch[0], 1u);
            temporalPass = AddTemporal("ShadowStabilize", scratch[0], colorWrite);
            return colorWrite;
        }
        RenderGraphResource temporalInput = source;
        if (effect != 1)
        {
            AddFilter("GiPreFilter", source, scratch[0], 1u);
            temporalInput = scratch[0];
        }
        temporalPass = AddTemporal($"{name}Temporal", temporalInput, colorWrite);
        RenderGraphResource input = colorWrite;
        int iterationOffset = effect == 1 ? 0 : 1;
        int iterations = effect == 1 ? 2 : 3;
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            RenderGraphResource output = scratch[iterationOffset + (iteration & 1)];
            AddFilter($"{name}Spatial.{iteration}", input, output, 1u << iteration, effect == 2 ? momentsWrite : null);
            input = output;
        }
        return input;
    }

    private uint FrameIndex(bool gameView) => gameView ? (uint)_gameJitterIndex : (uint)_sceneJitterIndex;

    private void UpdateRtDenoiserDescriptors()
    {
        foreach (var state in _rtDenoiserStates)
        {
            state.History.Prepare(RayTracingLogicalExtent(state.GameView, (RayTracingQualityEffect)state.Effect));
            RebindDenoiserHistory(state.History.Color, state.ColorRead, state.ColorWrite);
            RebindDenoiserHistory(state.History.Moments, state.MomentsRead, state.MomentsWrite);
            RebindDenoiserHistory(state.History.Geometry, state.GeometryRead, state.GeometryWrite);
        }
    }

    private void RebindDenoiserHistory(TemporalHistory history, RenderGraphResource read, RenderGraphResource write)
    {
        _graph!.RebindImportedTexture(read, history.ReadTexture, history.ReadInitialized ? ImageLayout.ShaderReadOnlyOptimal : ImageLayout.Undefined);
        _graph.RebindImportedTexture(write, history.WriteTexture, history.WriteInitialized ? ImageLayout.ShaderReadOnlyOptimal : ImageLayout.Undefined,
            ImageLayout.ShaderReadOnlyOptimal);
    }
}
