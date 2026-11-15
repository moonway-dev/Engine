using Engine.Mathematics;
namespace Engine.Platform;

public enum FullscreenMode
{
    Windowed,
    Borderless,
    Exclusive,
}

public sealed class WindowDescription
{
    public string Title { get; set; } = "Engine";
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public int MinWidth { get; set; } = 320;
    public int MinHeight { get; set; } = 240;
    public Vector2? Position { get; set; }
    public bool Resizable { get; set; } = true;
    public bool Visible { get; set; } = true;
    public bool Decorated { get; set; } = true;
    public bool Maximized { get; set; }
    public FullscreenMode Fullscreen { get; set; } = FullscreenMode.Windowed;
    public int MonitorIndex { get; set; }
}
