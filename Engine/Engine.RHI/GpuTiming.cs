namespace Engine.RHI;

public sealed record GpuFrameTiming(long FrameNumber, IReadOnlyList<GpuPassTiming> Passes, bool Incomplete = false);

public readonly record struct GpuPassTiming(string Name, double Milliseconds);
