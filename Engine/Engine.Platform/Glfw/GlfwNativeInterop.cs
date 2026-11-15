using System.Runtime.InteropServices;

namespace Engine.Platform;

internal static unsafe class GlfwNativeInterop
{
    public enum GlfwPlatformId
    {
        Win32 = 0x00060001,
        Cocoa = 0x00060002,
        Wayland = 0x00060003,
        X11 = 0x00060004,
        Null = 0x00060005,
    }

    [DllImport("glfw3", EntryPoint = "glfwGetWin32Window")]
    private static extern nint GetWin32WindowImport(nint window);

    [DllImport("glfw3", EntryPoint = "glfwGetX11Display")]
    private static extern nint GetX11DisplayImport();

    [DllImport("glfw3", EntryPoint = "glfwGetX11Window")]
    private static extern nint GetX11WindowImport(nint window);

    [DllImport("glfw3", EntryPoint = "glfwGetWaylandDisplay")]
    private static extern nint GetWaylandDisplayImport();

    [DllImport("glfw3", EntryPoint = "glfwGetWaylandWindow")]
    private static extern nint GetWaylandWindowImport(nint window);

    [DllImport("glfw3", EntryPoint = "glfwGetCocoaWindow")]
    private static extern nint GetCocoaWindowImport(nint window);

    [DllImport("glfw3", EntryPoint = "glfwGetPlatform")]
    private static extern int GetPlatformImport();

    public static nint GetWin32Window(nint window) => SafeCall(() => GetWin32WindowImport(window));

    public static nint GetX11Display() => SafeCall(GetX11DisplayImport);

    public static nint GetX11Window(nint window) => SafeCall(() => GetX11WindowImport(window));

    public static nint GetWaylandDisplay() => SafeCall(GetWaylandDisplayImport);

    public static nint GetWaylandWindow(nint window) => SafeCall(() => GetWaylandWindowImport(window));

    public static nint GetCocoaWindow(nint window) => SafeCall(() => GetCocoaWindowImport(window));

    public static GlfwPlatformId GetPlatform() => SafeCall(() => (GlfwPlatformId)GetPlatformImport());

    private static T SafeCall<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (EntryPointNotFoundException)
        {
            return default!;
        }
    }
}
