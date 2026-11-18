namespace Engine.Core;

public readonly record struct FrameTime(
    float DeltaTime,
    float UnscaledDeltaTime,
    float TotalTime,
    float FixedDeltaTime,
    ulong FrameIndex,
    float FixedInterpolationAlpha = 0f,
    int FixedStepsThisFrame = 0,
    int FixedStepsPending = 0,
    float DroppedFixedTimeSeconds = 0f);
