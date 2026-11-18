using Engine.Core;
using Xunit;

namespace Engine.Tests.Core;

public class GameClockTests
{
    [Fact]
    public void Tick_AdvancesTimeAndFrameIndex()
    {
        GameClock clock = new();
        clock.Tick(1.0 / 60.0);
        Assert.Equal(1UL, clock.FrameIndex);
        Assert.Equal(1f / 60f, clock.DeltaTime, 5);
        clock.Tick(1.0 / 60.0);
        Assert.Equal(2UL, clock.FrameIndex);
        Assert.Equal(2f / 60f, clock.TotalTime, 4);
    }

    [Fact]
    public void Tick_AccumulatesFixedSteps()
    {
        GameClock clock = new();
        clock.Tick(1.0 / 30.0);
        Assert.Equal(2, clock.FixedStepsPending);
        Assert.True(clock.ConsumeFixedStep());
        Assert.True(clock.ConsumeFixedStep());
        Assert.False(clock.ConsumeFixedStep());
    }

    [Fact]
    public void MaxFixedSteps_LimitsSpiral()
    {
        GameClock clock = new() { MaxFixedStepsPerFrame = 2 };
        clock.Tick(0.2);
        Assert.True(clock.ConsumeFixedStep());
        Assert.True(clock.ConsumeFixedStep());
        Assert.False(clock.ConsumeFixedStep());
    }

    [Fact]
    public void MaxFixedSteps_DropsExcessWholeStepsAndKeepsInterpolationRemainder()
    {
        GameClock clock = new() { MaxFixedStepsPerFrame = 2 };
        clock.Tick(0.2);
        Assert.True(clock.ConsumeFixedStep());
        Assert.True(clock.ConsumeFixedStep());
        Assert.False(clock.ConsumeFixedStep());

        Assert.Equal(0, clock.FixedStepsPending);
        Assert.InRange(clock.FixedInterpolationAlpha, 0f, 1f);
        Assert.True(clock.DroppedFixedTimeSeconds > 0f);
    }

    [Fact]
    public void TimeScale_ScalesDelta()
    {
        GameClock clock = new() { TimeScale = 0.5f };
        clock.Tick(0.1);
        Assert.Equal(0.05f, clock.DeltaTime, 5);
    }

    [Fact]
    public void Pause_StopsAccumulation()
    {
        GameClock clock = new() { IsPaused = true };
        clock.Tick(1.0 / 60.0);
        Assert.Equal(0f, clock.DeltaTime);
        Assert.Equal(0, clock.FixedStepsPending);
        Assert.Equal(1UL, clock.FrameIndex);
    }

    [Fact]
    public void LargeDelta_IsClamped()
    {
        GameClock clock = new();
        clock.Tick(5.0);
        Assert.Equal(GameClock.MaxFrameDeltaSeconds, clock.DeltaTime, 3);
    }

    [Fact]
    public void AverageFps_TracksFrameRate()
    {
        GameClock clock = new();
        for (int i = 0; i < 60; i++)
            clock.Tick(1.0 / 60.0);
        Assert.InRange(clock.AverageFps, 59f, 61f);
    }
}
