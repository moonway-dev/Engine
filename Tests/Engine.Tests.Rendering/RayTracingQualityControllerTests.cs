using Engine.Rendering;
using Engine.RHI;
using Xunit;

namespace Engine.Tests.Rendering;

public sealed class RayTracingQualityControllerTests
{
    [Fact]
    public void Update_IgnoresUnsupportedIncompleteAndStaleTimings()
    {
        RayTracingQualityController controller = new();

        controller.Update(new GpuFrameTiming(1, [new GpuPassTiming("RtGameReflections", 20)], Incomplete: true), true);
        Assert.False(controller.HasGpuTiming);

        controller.Update(new GpuFrameTiming(1, [new GpuPassTiming("RtGameReflections", 20)]), true);
        Assert.True(controller.HasGpuTiming);
        Assert.Equal(20d, controller.GetLastMilliseconds(RayTracingQualityCamera.Game, RayTracingQualityEffect.Reflections));

        controller.Update(new GpuFrameTiming(1, [new GpuPassTiming("RtGameReflections", 1)]), true);
        Assert.Equal(20d, controller.GetLastMilliseconds(RayTracingQualityCamera.Game, RayTracingQualityEffect.Reflections));

        controller.Update(new GpuFrameTiming(2, [new GpuPassTiming("RtGameReflections", 1)]), false);
        Assert.False(controller.HasGpuTiming);
    }

    [Fact]
    public void Update_AggregatesTraceAndFilterPassesForEffect()
    {
        RayTracingQualityController controller = new();

        controller.Update(new GpuFrameTiming(1,
        [
            new GpuPassTiming("RtGameReflections", 2.1),
            new GpuPassTiming("RtGameFilter", 1.6),
            new GpuPassTiming("RtGameComposite", 0.4),
            new GpuPassTiming("RtGameGi", 50),
            new GpuPassTiming("RtSceneTrace", 30),
            new GpuPassTiming("ForwardPass", 100),
        ]), true);

        Assert.Equal(4.1d, controller.GetLastMilliseconds(RayTracingQualityCamera.Game, RayTracingQualityEffect.Reflections), 6);
        Assert.Equal(50d, controller.GetLastMilliseconds(RayTracingQualityCamera.Game, RayTracingQualityEffect.Gi));
        Assert.Equal(30d, controller.GetLastMilliseconds(RayTracingQualityCamera.Scene, RayTracingQualityEffect.Shadows));
        Assert.Equal(0d, controller.GetLastMilliseconds(RayTracingQualityCamera.Scene, RayTracingQualityEffect.Gi));
    }

    [Fact]
    public void Update_ReducesAfterThreeOverBudgetFramesAndStopsAtStageZero()
    {
        RayTracingQualityController controller = new();
        RayTracingQualityCamera camera = RayTracingQualityCamera.Game;
        RayTracingQualityEffect effect = RayTracingQualityEffect.Reflections;

        for (int frame = 1; frame <= 3; frame++)
            controller.Update(new GpuFrameTiming(frame, [new GpuPassTiming("RtGameReflections", 10)]), true);
        Assert.Equal(2, controller.GetStage(camera, effect, RayTracingQualityPreset.Ultra, true, false, true));

        for (int frame = 4; frame <= 9; frame++)
            controller.Update(new GpuFrameTiming(frame, [new GpuPassTiming("RtGameReflections", 10)]), true);
        Assert.Equal(0, controller.GetStage(camera, effect, RayTracingQualityPreset.Ultra, true, false, true));

        for (int frame = 10; frame <= 18; frame++)
            controller.Update(new GpuFrameTiming(frame, [new GpuPassTiming("RtGameReflections", 10)]), true);
        Assert.Equal(0, controller.GetStage(camera, effect, RayTracingQualityPreset.Ultra, true, false, true));
    }

    [Fact]
    public void Update_RaisesQualityOnlyAfterThirtyLowBudgetFrames()
    {
        RayTracingQualityController controller = new();
        RayTracingQualityCamera camera = RayTracingQualityCamera.Game;
        RayTracingQualityEffect effect = RayTracingQualityEffect.Reflections;
        controller.Update(new GpuFrameTiming(1, [new GpuPassTiming("RtGameReflections", 10)]), true);
        controller.Update(new GpuFrameTiming(2, [new GpuPassTiming("RtGameReflections", 10)]), true);
        controller.Update(new GpuFrameTiming(3, [new GpuPassTiming("RtGameReflections", 10)]), true);
        Assert.Equal(2, controller.GetStage(camera, effect, RayTracingQualityPreset.Ultra, true, false, true));

        for (int frame = 4; frame <= 32; frame++)
            controller.Update(new GpuFrameTiming(frame, [new GpuPassTiming("RtGameReflections", 0.1)]), true);
        Assert.Equal(2, controller.GetStage(camera, effect, RayTracingQualityPreset.Ultra, true, false, true));
        controller.Update(new GpuFrameTiming(33, [new GpuPassTiming("RtGameReflections", 0.1)]), true);
        Assert.Equal(3, controller.GetStage(camera, effect, RayTracingQualityPreset.Ultra, true, false, true));
    }

    [Fact]
    public void GetStage_RespectsPresetCapsAndPrioritizesGameView()
    {
        RayTracingQualityController controller = new();

        Assert.Equal(0, controller.GetStage(RayTracingQualityCamera.Scene, RayTracingQualityEffect.Gi, RayTracingQualityPreset.Low, false, true, true));
        Assert.Equal(3, controller.GetStage(RayTracingQualityCamera.Game, RayTracingQualityEffect.Gi, RayTracingQualityPreset.Ultra, false, true, true));
        Assert.Equal(0, controller.GetStage(RayTracingQualityCamera.Scene, RayTracingQualityEffect.Gi, RayTracingQualityPreset.Ultra, true, true, true));
        Assert.Equal(2, controller.GetStage(RayTracingQualityCamera.Game, RayTracingQualityEffect.Gi, RayTracingQualityPreset.Ultra, true, true, true));
        Assert.Equal(2, controller.GetStage(RayTracingQualityCamera.Scene, RayTracingQualityEffect.Gi, RayTracingQualityPreset.Ultra, true, false, false));
    }

    [Fact]
    public void GetStage_UsesFallbackWhenGpuTimestampsAreUnavailable()
    {
        RayTracingQualityController controller = new();
        controller.Update(null, false);

        Assert.Equal(0, controller.GetStage(RayTracingQualityCamera.Scene, RayTracingQualityEffect.Shadows, RayTracingQualityPreset.Ultra, true, true, true));
        Assert.Equal(2, controller.GetStage(RayTracingQualityCamera.Game, RayTracingQualityEffect.Shadows, RayTracingQualityPreset.Ultra, true, true, true));
        Assert.Equal(2, controller.GetStage(RayTracingQualityCamera.Scene, RayTracingQualityEffect.Shadows, RayTracingQualityPreset.Ultra, true, true, false));
        Assert.Equal(1, controller.GetStage(RayTracingQualityCamera.Game, RayTracingQualityEffect.Shadows, RayTracingQualityPreset.Medium, true, false, false));
    }
}
