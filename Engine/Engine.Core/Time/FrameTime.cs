namespace Engine.Core;

public readonly record struct FrameTime(
    float DeltaTime,
    float UnscaledDeltaTime,
    float TotalTime,
    float FixedDeltaTime,
    ulong FrameIndex);
