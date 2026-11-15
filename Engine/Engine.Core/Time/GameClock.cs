namespace Engine.Core;

public sealed class GameClock
{
    public const float DefaultFixedDeltaTime = 1f / 60f;
    public const int DefaultMaxFixedStepsPerFrame = 5;
    public const float MaxFrameDeltaSeconds = 0.25f;

    private double _fixedAccumulator;
    private double _fpsTimer;
    private int _fpsFrames;
    private int _fixedStepsTaken;

    public float FixedDeltaTime { get; set; } = DefaultFixedDeltaTime;
    public int MaxFixedStepsPerFrame { get; set; } = DefaultMaxFixedStepsPerFrame;
    public float TimeScale { get; set; } = 1f;
    public bool IsPaused { get; set; }

    public float DeltaTime { get; private set; }
    public float UnscaledDeltaTime { get; private set; }
    public float TotalTime { get; private set; }
    public float UnscaledTotalTime { get; private set; }
    public ulong FrameIndex { get; private set; }
    public float AverageFps { get; private set; }
    public int FixedStepsPending => Math.Min((int)(_fixedAccumulator / FixedDeltaTime), int.MaxValue);

    public void Tick(double realDeltaSeconds)
    {
        realDeltaSeconds = Math.Clamp(realDeltaSeconds, 0.0, MaxFrameDeltaSeconds);
        UnscaledDeltaTime = (float)realDeltaSeconds;
        float scaledDelta = IsPaused ? 0f : (float)realDeltaSeconds * TimeScale;
        DeltaTime = scaledDelta;
        UnscaledTotalTime += UnscaledDeltaTime;
        TotalTime += scaledDelta;
        FrameIndex++;
        _fixedStepsTaken = 0;
        if (!IsPaused)
            _fixedAccumulator += scaledDelta;
        _fpsFrames++;
        _fpsTimer += realDeltaSeconds;
        if (_fpsTimer >= 0.5)
        {
            AverageFps = (float)(_fpsFrames / _fpsTimer);
            _fpsFrames = 0;
            _fpsTimer = 0;
        }
    }

    public bool ConsumeFixedStep()
    {
        if (IsPaused || _fixedStepsTaken >= MaxFixedStepsPerFrame || _fixedAccumulator < FixedDeltaTime)
            return false;
        _fixedAccumulator -= FixedDeltaTime;
        _fixedStepsTaken++;
        return true;
    }

    public void Reset()
    {
        DeltaTime = 0f;
        UnscaledDeltaTime = 0f;
        TotalTime = 0f;
        UnscaledTotalTime = 0f;
        FrameIndex = 0;
        AverageFps = 0f;
        _fixedAccumulator = 0;
        _fixedStepsTaken = 0;
        _fpsTimer = 0;
        _fpsFrames = 0;
        IsPaused = false;
        TimeScale = 1f;
    }
}
