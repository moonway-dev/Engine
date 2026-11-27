using Engine.Mathematics;
using Engine.Rendering;
using Engine.RHI;
using Xunit;

namespace Engine.Tests.Rendering;

public sealed class RayTracingQualityControllerTests
{
    [Theory]
    [InlineData(0, 1600, 900, 400, 225)]
    [InlineData(1, 1600, 900, 800, 450)]
    [InlineData(2, 1600, 900, 1200, 675)]
    [InlineData(3, 1600, 900, 1600, 900)]
    [InlineData(0, 801, 451, 201, 113)]
    [InlineData(0, 1, 1, 1, 1)]
    [InlineData(1, 800, 450, 400, 225)]
    public void Profile_LogicalExtentScalesWithinAllocatedTexture(int stage, int width, int height, int expectedWidth, int expectedHeight)
    {
        Size2D extent = RayTracingQualityProfile.FromStage(stage).LogicalExtent(new Size2D(width, height));
        Assert.Equal(new Size2D(expectedWidth, expectedHeight), extent);
    }

    [Fact]
    public void Profiles_IncreaseRayWorkMonotonically()
    {
        long previousWork = 0;
        for (int stage = 0; stage <= 3; stage++)
        {
            RayTracingQualityProfile profile = RayTracingQualityProfile.FromStage(stage);
            Size2D extent = profile.LogicalExtent(new Size2D(1600, 900));
            long work = (long)extent.Width * extent.Height * profile.Samples;
            Assert.True(work > previousWork);
            previousWork = work;
        }
    }

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
            new GpuPassTiming("RtGameGiPreFilter", 2),
            new GpuPassTiming("RtGameGiTemporal", 1),
            new GpuPassTiming("RtGameGiSpatial.0", 3),
            new GpuPassTiming("RtSceneTrace", 30),
            new GpuPassTiming("RtSceneShadowStabilize", 4),
            new GpuPassTiming("ForwardPass", 100),
        ]), true);

        Assert.Equal(4.1d, controller.GetLastMilliseconds(RayTracingQualityCamera.Game, RayTracingQualityEffect.Reflections), 6);
        Assert.Equal(56d, controller.GetLastMilliseconds(RayTracingQualityCamera.Game, RayTracingQualityEffect.Gi));
        Assert.Equal(34d, controller.GetLastMilliseconds(RayTracingQualityCamera.Scene, RayTracingQualityEffect.Shadows));
        Assert.Equal(0d, controller.GetLastMilliseconds(RayTracingQualityCamera.Scene, RayTracingQualityEffect.Gi));
    }

    [Fact]
    public void Update_ReducesAfterTenOverBudgetFramesAndStopsAtStageZero()
    {
        RayTracingQualityController controller = new();
        RayTracingQualityCamera camera = RayTracingQualityCamera.Game;
        RayTracingQualityEffect effect = RayTracingQualityEffect.Reflections;

        for (int frame = 1; frame <= 10; frame++)
            controller.Update(new GpuFrameTiming(frame, [new GpuPassTiming("RtGameReflections", 10)]), true);
        Assert.Equal(2, controller.GetStage(camera, effect, RayTracingQualityPreset.Ultra, true, false, true));

        for (int frame = 11; frame <= 20; frame++)
            controller.Update(new GpuFrameTiming(frame, [new GpuPassTiming("RtGameReflections", 10)]), true);
        Assert.Equal(1, controller.GetStage(camera, effect, RayTracingQualityPreset.Ultra, true, false, true));

        for (int frame = 21; frame <= 30; frame++)
            controller.Update(new GpuFrameTiming(frame, [new GpuPassTiming("RtGameReflections", 10)]), true);
        Assert.Equal(0, controller.GetStage(camera, effect, RayTracingQualityPreset.Ultra, true, false, true));

        for (int frame = 31; frame <= 40; frame++)
            controller.Update(new GpuFrameTiming(frame, [new GpuPassTiming("RtGameReflections", 10)]), true);
        Assert.Equal(0, controller.GetStage(camera, effect, RayTracingQualityPreset.Ultra, true, false, true));
    }

    [Fact]
    public void Update_RaisesQualityOnlyAfterThirtyLowBudgetFrames()
    {
        RayTracingQualityController controller = new();
        RayTracingQualityCamera camera = RayTracingQualityCamera.Game;
        RayTracingQualityEffect effect = RayTracingQualityEffect.Reflections;

        for (int frame = 1; frame <= 10; frame++)
            controller.Update(new GpuFrameTiming(frame, [new GpuPassTiming("RtGameReflections", 10)]), true);
        Assert.Equal(2, controller.GetStage(camera, effect, RayTracingQualityPreset.Ultra, true, false, true));

        for (int frame = 11; frame <= 39; frame++)
            controller.Update(new GpuFrameTiming(frame, [new GpuPassTiming("RtGameReflections", 0.1)]), true);
        Assert.Equal(2, controller.GetStage(camera, effect, RayTracingQualityPreset.Ultra, true, false, true));
        controller.Update(new GpuFrameTiming(40, [new GpuPassTiming("RtGameReflections", 0.1)]), true);
        Assert.Equal(3, controller.GetStage(camera, effect, RayTracingQualityPreset.Ultra, true, false, true));
    }

    [Fact]
    public void GetStage_RespectsPresetCapsAndPrioritizesGameView()
    {
        RayTracingQualityController controller = new();

        Assert.Equal(0, controller.GetStage(RayTracingQualityCamera.Scene, RayTracingQualityEffect.Gi, RayTracingQualityPreset.Low, false, true, true));
        Assert.Equal(3, controller.GetStage(RayTracingQualityCamera.Game, RayTracingQualityEffect.Gi, RayTracingQualityPreset.Ultra, false, true, true));
        Assert.Equal(2, controller.GetStage(RayTracingQualityCamera.Scene, RayTracingQualityEffect.Gi, RayTracingQualityPreset.Ultra, true, true, true));
        Assert.Equal(2, controller.GetStage(RayTracingQualityCamera.Game, RayTracingQualityEffect.Gi, RayTracingQualityPreset.Ultra, true, true, true));
        Assert.Equal(2, controller.GetStage(RayTracingQualityCamera.Scene, RayTracingQualityEffect.Gi, RayTracingQualityPreset.Ultra, true, false, false));
    }

    [Fact]
    public void GetStage_UsesFallbackWhenGpuTimestampsAreUnavailable()
    {
        RayTracingQualityController controller = new();
        controller.Update(null, false);

        Assert.Equal(2, controller.GetStage(RayTracingQualityCamera.Scene, RayTracingQualityEffect.Shadows, RayTracingQualityPreset.Ultra, true, true, true));
        Assert.Equal(2, controller.GetStage(RayTracingQualityCamera.Game, RayTracingQualityEffect.Shadows, RayTracingQualityPreset.Ultra, true, true, true));
        Assert.Equal(2, controller.GetStage(RayTracingQualityCamera.Scene, RayTracingQualityEffect.Shadows, RayTracingQualityPreset.Ultra, true, true, false));
        Assert.Equal(1, controller.GetStage(RayTracingQualityCamera.Game, RayTracingQualityEffect.Shadows, RayTracingQualityPreset.Medium, true, false, false));
    }
}
