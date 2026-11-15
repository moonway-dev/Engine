using Engine.Mathematics;
namespace Engine.Platform;

public readonly record struct MonitorInfo(
    int Index,
    string Name,
    Vector2 Position,
    Size2D ClientSize,
    float RefreshRate,
    bool IsPrimary);

public interface IPlatform : IDisposable
{
    string Name { get; }

    double TimeSeconds { get; }

    IReadOnlyList<MonitorInfo> Monitors { get; }

    IWindow CreateWindow(WindowDescription description);

    void PollEvents();

    bool IsVulkanSupported();
}
