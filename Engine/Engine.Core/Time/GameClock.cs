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
    private int _fixedStepsPending;
    private double _droppedFixedTimeSeconds;
    private double _droppedFixedTimeSecondsThisFrame;

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
    public int FixedStepsPending => _fixedStepsPending;
    public int FixedStepsTakenThisFrame => _fixedStepsTaken;
    public float FixedInterpolationAlpha => FixedDeltaTime > 0f ? Math.Clamp((float)(_fixedAccumulator / FixedDeltaTime), 0f, 1f) : 0f;
    public float DroppedFixedTimeSeconds => (float)_droppedFixedTimeSeconds;
    public float DroppedFixedTimeSecondsThisFrame => (float)_droppedFixedTimeSecondsThisFrame;

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
        _droppedFixedTimeSecondsThisFrame = 0;
        if (!IsPaused)
            _fixedAccumulator += scaledDelta;
        if (FixedDeltaTime <= 0f || !float.IsFinite(FixedDeltaTime))
        {
            _fixedStepsPending = 0;
            _fpsFrames++;
            _fpsTimer += realDeltaSeconds;
            if (_fpsTimer >= 0.5)
            {
                AverageFps = (float)(_fpsFrames / _fpsTimer);
                _fpsFrames = 0;
                _fpsTimer = 0;
            }
            return;
        }
        int maxSteps = Math.Max(0, MaxFixedStepsPerFrame);
        double availableSteps = Math.Floor(_fixedAccumulator / FixedDeltaTime);
        if (availableSteps > maxSteps)
        {
            double droppedTime = (availableSteps - maxSteps) * FixedDeltaTime;
            _fixedAccumulator -= droppedTime;
            _droppedFixedTimeSeconds += droppedTime;
            _droppedFixedTimeSecondsThisFrame = droppedTime;
            availableSteps = maxSteps;
        }
        _fixedStepsPending = (int)Math.Clamp(availableSteps, 0, int.MaxValue);
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
        if (IsPaused || FixedDeltaTime <= 0f || _fixedStepsTaken >= MaxFixedStepsPerFrame || _fixedAccumulator < FixedDeltaTime)
            return false;
        _fixedAccumulator -= FixedDeltaTime;
        _fixedStepsTaken++;
        _fixedStepsPending = Math.Max(0, _fixedStepsPending - 1);
        if (_fixedStepsTaken >= Math.Max(0, MaxFixedStepsPerFrame) && FixedDeltaTime > 0f && _fixedAccumulator >= FixedDeltaTime)
        {
            double droppedSteps = Math.Floor(_fixedAccumulator / FixedDeltaTime);
            double droppedTime = droppedSteps * FixedDeltaTime;
            _fixedAccumulator -= droppedTime;
            _droppedFixedTimeSeconds += droppedTime;
            _droppedFixedTimeSecondsThisFrame += droppedTime;
            _fixedStepsPending = 0;
        }
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
        _fixedStepsPending = 0;
        _droppedFixedTimeSeconds = 0;
        _droppedFixedTimeSecondsThisFrame = 0;
        _fpsTimer = 0;
        _fpsFrames = 0;
        IsPaused = false;
        TimeScale = 1f;
    }
}
