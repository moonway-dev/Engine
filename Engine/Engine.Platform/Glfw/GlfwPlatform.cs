using GlfwMonitor = Silk.NET.GLFW.Monitor;
using Engine.Core;
using Engine.Mathematics;
using Silk.NET.GLFW;

namespace Engine.Platform;

internal unsafe sealed class GlfwPlatform : IPlatform
{
    private readonly ILogger _log;
    private readonly List<GlfwWindow> _windows = [];
    private readonly List<MonitorInfo> _monitors = [];
    private Glfw? _glfw;
    private GlfwCallbacks.ErrorCallback? _errorCallback;
    private GlfwCallbacks.MonitorCallback? _monitorCallback;
    private bool _initialized;

    public string Name => "GLFW";

    public double TimeSeconds => _glfw?.GetTime() ?? 0;

    public IReadOnlyList<MonitorInfo> Monitors => _monitors;

    public GlfwPlatform(ILogger log)
    {
        _log = log;
    }

    public void Initialize()
    {
        _glfw = Glfw.GetApi();
        _errorCallback = (code, description) => _log.Error($"GLFW error {code}: {description}");
        _glfw.SetErrorCallback(_errorCallback);
        if (!_glfw.Init())
            throw new PlatformException("Failed to initialize GLFW.");
        _initialized = true;
        _monitorCallback = (monitor, _) => RefreshMonitors();
        _glfw.SetMonitorCallback(_monitorCallback);
        RefreshMonitors();
        _log.Information($"GLFW {_glfw.GetVersionString()} initialized, {_monitors.Count} monitor(s) detected");
    }

    public IWindow CreateWindow(WindowDescription description)
    {
        EnsureInitialized();
        GlfwWindow window = new(_glfw!, description);
        _windows.Add(window);
        _log.Debug($"Window '{description.Title}' created ({description.Width}x{description.Height})");
        return window;
    }

    public void PollEvents() => _glfw!.PollEvents();

    internal void UpdateWindowInputs()
    {
        foreach (GlfwWindow window in _windows)
            window.UpdateInputStates();
    }

    public bool IsVulkanSupported() => _glfw!.VulkanSupported();

    internal GlfwMonitor* GetNativeMonitor(int index)
    {
        GlfwMonitor** monitors = _glfw!.GetMonitors(out int count);
        if (index < 0 || index >= count)
            return null;
        return monitors[index];
    }

    private void RefreshMonitors()
    {
        _monitors.Clear();
        GlfwMonitor* primary = _glfw!.GetPrimaryMonitor();
        GlfwMonitor** monitors = _glfw.GetMonitors(out int count);
        for (int i = 0; i < count; i++)
        {
            GlfwMonitor* monitor = monitors[i];
            _glfw.GetMonitorPos(monitor, out int x, out int y);
            VideoMode* mode = _glfw.GetVideoMode(monitor);
            _monitors.Add(new MonitorInfo(
                (int)i,
                _glfw.GetMonitorName(monitor),
                new Vector2(x, y),
                new Size2D(mode->Width, mode->Height),
                mode->RefreshRate,
                monitor == primary));
        }
    }

    public void Dispose()
    {
        if (!_initialized)
            return;
        foreach (GlfwWindow window in _windows)
            window.Dispose();
        _windows.Clear();
        _monitorCallback = null;
        _errorCallback = null;
        _glfw!.Terminate();
        _glfw = null;
        _initialized = false;
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
            throw new PlatformException("Platform is not initialized.");
    }
}
