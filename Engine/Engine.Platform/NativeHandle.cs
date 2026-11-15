namespace Engine.Platform;

public readonly struct NativeHandle
{
    public nint GlfwWindow { get; init; }
    public nint Win32Hwnd { get; init; }
    public nint Win32HInstance { get; init; }
    public nint X11Display { get; init; }
    public nint X11Window { get; init; }
    public nint WaylandDisplay { get; init; }
    public nint WaylandSurface { get; init; }
    public nint CocoaWindow { get; init; }

    public static NativeHandle Empty => default;

    public bool IsEmpty => GlfwWindow == 0;
}
