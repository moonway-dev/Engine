namespace Engine.Platform;

public readonly struct SurfaceSource
{
    public required IReadOnlyList<string> RequiredInstanceExtensions { get; init; }

    public required Func<nint, ulong> CreateSurface { get; init; }
}
