using GlfwMonitor = Silk.NET.GLFW.Monitor;
using System.Runtime.InteropServices;
using Engine.Mathematics;
using Silk.NET.Core.Native;
using Silk.NET.GLFW;

namespace Engine.Platform;

internal unsafe sealed class GlfwWindow : IWindow
{
    private readonly Glfw _glfw;
    private readonly WindowHandle* _handle;
    private readonly KeyboardState _keyboard = new();
    private readonly MouseState _mouse = new();
    private Size2D _clientSize;
    private Size2D _framebufferSize;
    private bool _closeRequested;
    private bool _disposed;
    private string _title;
    private GlfwCallbacks.WindowCloseCallback? _closeCallback;
    private GlfwCallbacks.WindowSizeCallback? _sizeCallback;
    private GlfwCallbacks.FramebufferSizeCallback? _framebufferSizeCallback;
    private GlfwCallbacks.WindowFocusCallback? _focusCallback;
    private GlfwCallbacks.KeyCallback? _keyCallback;
    private readonly int _monitorIndex;
    private FullscreenMode _fullscreenMode;
    private Vector2 _savedWindowedPosition;
    private Size2D _savedWindowedSize;
    private GlfwCallbacks.CharCallback? _charCallback;
    private GlfwCallbacks.MouseButtonCallback? _mouseButtonCallback;
    private GlfwCallbacks.CursorPosCallback? _cursorPosCallback;
    private GlfwCallbacks.ScrollCallback? _scrollCallback;
    private GlfwCallbacks.CursorEnterCallback? _cursorEnterCallback;

    public string Title
    {
        get => _title;
        set
        {
            _title = value;
            _glfw.SetWindowTitle(_handle, value);
        }
    }

    public Size2D ClientSize => _clientSize;

    public Size2D FramebufferSize => _framebufferSize;

    public Vector2 Position
    {
        get
        {
            _glfw.GetWindowPos(_handle, out int x, out int y);
            return new Vector2(x, y);
        }
        set => _glfw.SetWindowPos(_handle, (int)value.X, (int)value.Y);
    }

    public bool IsVisible => _glfw.GetWindowAttrib(_handle, WindowAttributeGetter.Visible);

    public bool IsFocused => _glfw.GetWindowAttrib(_handle, WindowAttributeGetter.Focused);

    public bool IsMinimized => _glfw.GetWindowAttrib(_handle, WindowAttributeGetter.Iconified);

    public bool ShouldClose => _closeRequested || _glfw.WindowShouldClose(_handle);

    private CursorMode _cursorMode = CursorMode.Normal;
    private bool _rawMouseMotionEnabled;
    private readonly bool _rawMouseMotionSupported;

    public FullscreenMode FullscreenMode => _fullscreenMode;

    public unsafe void SetFullscreen(FullscreenMode mode)
    {
        if (mode == _fullscreenMode)
            return;
        if (mode == FullscreenMode.Windowed)
        {
            _glfw.SetWindowMonitor(
                _handle, null, (int)_savedWindowedPosition.X, (int)_savedWindowedPosition.Y,
                Math.Max(_savedWindowedSize.Width, 320), Math.Max(_savedWindowedSize.Height, 240), 0);
            _fullscreenMode = FullscreenMode.Windowed;
            RefreshSizes();
            return;
        }
        _glfw.GetWindowPos(_handle, out int x, out int y);
        _glfw.GetWindowSize(_handle, out int width, out int height);
        _savedWindowedPosition = new Vector2(x, y);
        _savedWindowedSize = new Size2D(width, height);
        GlfwMonitor* monitor = GetMonitorByIndex(_monitorIndex);
        if (monitor is null)
            monitor = _glfw.GetPrimaryMonitor();
        if (monitor is null)
            return;
        VideoMode* videoMode = _glfw.GetVideoMode(monitor);
        _glfw.SetWindowMonitor(_handle, monitor, 0, 0, videoMode->Width, videoMode->Height, videoMode->RefreshRate);
        _fullscreenMode = mode;
        RefreshSizes();
    }

    public CursorMode CursorMode
    {
        get => _cursorMode;
        set
        {
            if (_cursorMode == value)
                return;

            if (value == CursorMode.Disabled)
            {
                _glfw.SetInputMode(_handle, CursorStateAttribute.Cursor, CursorModeValue.CursorDisabled);
                if (_rawMouseMotionSupported)
                {
                    _glfw.SetInputMode(_handle, CursorStateAttribute.RawMouseMotion, true);
                    _rawMouseMotionEnabled = true;
                }
            }
            else
            {
                if (_rawMouseMotionEnabled)
                {
                    _glfw.SetInputMode(_handle, CursorStateAttribute.RawMouseMotion, false);
                    _rawMouseMotionEnabled = false;
                }
                _glfw.SetInputMode(_handle, CursorStateAttribute.Cursor, value switch
                {
                    CursorMode.Normal => CursorModeValue.CursorNormal,
                    CursorMode.Hidden => CursorModeValue.CursorHidden,
                    _ => CursorModeValue.CursorNormal,
                });
            }

            _cursorMode = value;
        }
    }

    public void SetCursorPosition(Vector2 position)
    {
        _glfw.SetCursorPos(_handle, position.X, position.Y);
    }

    public void ResetMouseDelta() => _mouse.ResetDelta();

    public KeyboardState Keyboard => _keyboard;

    public MouseState Mouse => _mouse;

    public event Action? CloseRequested;
    public event Action<Size2D>? Resized;
    public event Action<bool>? FocusChanged;
    public event Action<KeyCode, KeyModifiers>? KeyPressed;
    public event Action<KeyCode, KeyModifiers>? KeyReleased;
    public event Action<string>? TextInput;
    public event Action<Vector2>? MouseMoved;
    public event Action<MouseButton, KeyModifiers>? MouseButtonPressed;
    public event Action<MouseButton, KeyModifiers>? MouseButtonReleased;
    public event Action<Vector2>? MouseScrolled;

    public GlfwWindow(Glfw glfw, WindowDescription description)
    {
        _glfw = glfw;
        _rawMouseMotionSupported = _glfw.RawMouseMotionSupported();
        _title = description.Title;

        _glfw.WindowHint(WindowHintClientApi.ClientApi, ClientApi.NoApi);
        _glfw.WindowHint(WindowHintBool.Resizable, description.Resizable);
        _glfw.WindowHint(WindowHintBool.Visible, false);
        _glfw.WindowHint(WindowHintBool.Decorated, description.Decorated);

        _monitorIndex = description.MonitorIndex;
        _fullscreenMode = description.Fullscreen;
        GlfwMonitor* monitor = null;
        if (description.Fullscreen != FullscreenMode.Windowed)
        {
            monitor = GetMonitorByIndex(description.MonitorIndex);
            if (monitor is not null)
            {
                VideoMode* mode = _glfw.GetVideoMode(monitor);
                _glfw.WindowHint(WindowHintInt.RedBits, mode->RedBits);
                _glfw.WindowHint(WindowHintInt.GreenBits, mode->GreenBits);
                _glfw.WindowHint(WindowHintInt.BlueBits, mode->BlueBits);
                _glfw.WindowHint(WindowHintInt.RefreshRate, mode->RefreshRate);
                description.Width = mode->Width;
                description.Height = mode->Height;
                if (description.Fullscreen == FullscreenMode.Borderless)
                    _glfw.WindowHint(WindowHintBool.Decorated, false);
            }
        }

        _handle = _glfw.CreateWindow(description.Width, description.Height, description.Title, monitor, null);
        if (_handle is null)
            throw new PlatformException($"Failed to create GLFW window '{description.Title}'.");

        _glfw.SetWindowSizeLimits(_handle, description.MinWidth, description.MinHeight, -1, -1);
        RefreshSizes();

        Vector2? position = description.Position;
        if (position is null && monitor is null)
        {
            GlfwMonitor* primary = _glfw.GetPrimaryMonitor();
            if (primary is not null)
            {
                VideoMode* mode = _glfw.GetVideoMode(primary);
                _glfw.GetMonitorPos(primary, out int monitorX, out int monitorY);
                position = new Vector2(
                    monitorX + (mode->Width - description.Width) / 2,
                    monitorY + (mode->Height - description.Height) / 2);
            }
        }
        if (position is not null)
            Position = position.Value;

        AttachCallbacks();

        if (description.Maximized)
            _glfw.MaximizeWindow(_handle);
        if (description.Visible)
            _glfw.ShowWindow(_handle);
        _glfw.FocusWindow(_handle);
        _glfw.GetCursorPos(_handle, out double cursorX, out double cursorY);
        _mouse.SetPosition(new Vector2((float)cursorX, (float)cursorY));
    }

    private GlfwMonitor* GetMonitorByIndex(int index)
    {
        GlfwMonitor** monitors = _glfw.GetMonitors(out int count);
        if (index < 0 || index >= count)
            return null;
        return monitors[index];
    }

    private void RefreshSizes()
    {
        _glfw.GetWindowSize(_handle, out int width, out int height);
        _clientSize = new Size2D(width, height);
        _glfw.GetFramebufferSize(_handle, out width, out height);
        _framebufferSize = new Size2D(width, height);
    }

    private void AttachCallbacks()
    {
        _closeCallback = window =>
        {
            _closeRequested = true;
            CloseRequested?.Invoke();
        };
        _sizeCallback = (window, width, height) => _clientSize = new Size2D(width, height);
        _framebufferSizeCallback = (window, width, height) =>
        {
            _framebufferSize = new Size2D(width, height);
            Resized?.Invoke(_framebufferSize);
        };
        _focusCallback = (window, focused) =>
        {
            if (!focused)
            {
                CursorMode = CursorMode.Normal;
                _keyboard.Reset();
                _mouse.Reset();
            }
            FocusChanged?.Invoke(focused);
        };
        _keyCallback = (window, key, scancode, action, mods) =>
        {
            KeyCode keyCode = (KeyCode)(int)key;
            KeyModifiers modifiers = (KeyModifiers)mods;
            switch (action)
            {
                case InputAction.Press:
                    _keyboard.SetDown(keyCode);
                    _keyboard.Modifiers |= modifiers;
                    KeyPressed?.Invoke(keyCode, modifiers);
                    break;
                case InputAction.Release:
                    _keyboard.SetUp(keyCode);
                    _keyboard.Modifiers &= ~modifiers;
                    KeyReleased?.Invoke(keyCode, modifiers);
                    break;
            }
        };
        _charCallback = (window, codepoint) => TextInput?.Invoke(char.ConvertFromUtf32((int)codepoint));
        _mouseButtonCallback = (window, button, action, mods) =>
        {
            MouseButton mouseButton = (MouseButton)button;
            KeyModifiers modifiers = (KeyModifiers)mods;
            switch (action)
            {
                case InputAction.Press:
                    _mouse.SetDown(mouseButton);
                    MouseButtonPressed?.Invoke(mouseButton, modifiers);
                    break;
                case InputAction.Release:
                    _mouse.SetUp(mouseButton);
                    MouseButtonReleased?.Invoke(mouseButton, modifiers);
                    break;
            }
        };
        _cursorPosCallback = (window, x, y) =>
        {
            Vector2 position = new((float)x, (float)y);
            _mouse.SetPosition(position);
            MouseMoved?.Invoke(position);
        };
        _scrollCallback = (window, offsetX, offsetY) =>
        {
            Vector2 delta = new((float)offsetX, (float)offsetY);
            _mouse.AddScroll(delta);
            MouseScrolled?.Invoke(delta);
        };
        _cursorEnterCallback = (window, entered) => _mouse.SetInsideWindow(entered);

        _glfw.SetWindowCloseCallback(_handle, _closeCallback);
        _glfw.SetWindowSizeCallback(_handle, _sizeCallback);
        _glfw.SetFramebufferSizeCallback(_handle, _framebufferSizeCallback);
        _glfw.SetWindowFocusCallback(_handle, _focusCallback);
        _glfw.SetKeyCallback(_handle, _keyCallback);
        _glfw.SetCharCallback(_handle, _charCallback);
        _glfw.SetMouseButtonCallback(_handle, _mouseButtonCallback);
        _glfw.SetCursorPosCallback(_handle, _cursorPosCallback);
        _glfw.SetScrollCallback(_handle, _scrollCallback);
        _glfw.SetCursorEnterCallback(_handle, _cursorEnterCallback);
    }

    public void UpdateInputStates()
    {
        _keyboard.EndFrame();
        _mouse.EndFrame();
    }

    public NativeHandle GetNativeHandle()
    {
        NativeHandle handle = new() { GlfwWindow = (nint)_handle };
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            handle = handle with { Win32Hwnd = GlfwNativeInterop.GetWin32Window((nint)_handle) };
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            handle = GlfwNativeInterop.GetPlatform() switch
            {
                GlfwNativeInterop.GlfwPlatformId.X11 => handle with
                {
                    X11Display = GlfwNativeInterop.GetX11Display(),
                    X11Window = GlfwNativeInterop.GetX11Window((nint)_handle),
                },
                GlfwNativeInterop.GlfwPlatformId.Wayland => handle with
                {
                    WaylandDisplay = GlfwNativeInterop.GetWaylandDisplay(),
                    WaylandSurface = GlfwNativeInterop.GetWaylandWindow((nint)_handle),
                },
                _ => handle,
            };
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            handle = handle with { CocoaWindow = GlfwNativeInterop.GetCocoaWindow((nint)_handle) };
        return handle;
    }

    public SurfaceSource GetSurfaceSource() => new()
    {
        RequiredInstanceExtensions = GetRequiredInstanceExtensions(),
        CreateSurface = CreateVulkanSurface,
    };

    private string[] GetRequiredInstanceExtensions()
    {
        byte** extensions = _glfw.GetRequiredInstanceExtensions(out uint count);
        string[] names = new string[count];
        for (uint i = 0; i < count; i++)
            names[i] = Marshal.PtrToStringUTF8((nint)extensions[i])!;
        return names;
    }

    private ulong CreateVulkanSurface(nint vkInstance)
    {
        if (!_glfw.VulkanSupported())
            throw new PlatformException("GLFW reports no Vulkan support on this machine.");
        VkNonDispatchableHandle* surface = stackalloc VkNonDispatchableHandle[1];
        int result = _glfw.CreateWindowSurface(new VkHandle(vkInstance), _handle, null, surface);
        if (result != 0)
            throw new PlatformException($"glfwCreateWindowSurface failed with VkResult {result}.");
        return surface[0].Handle;
    }

    public void Focus() => _glfw.FocusWindow(_handle);

    public void Close() => _glfw.SetWindowShouldClose(_handle, true);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_cursorMode != CursorMode.Normal)
            CursorMode = CursorMode.Normal;
        CloseRequested = null;
        Resized = null;
        FocusChanged = null;
        KeyPressed = null;
        KeyReleased = null;
        TextInput = null;
        MouseMoved = null;
        MouseButtonPressed = null;
        MouseButtonReleased = null;
        MouseScrolled = null;
        _closeCallback = null;
        _sizeCallback = null;
        _framebufferSizeCallback = null;
        _focusCallback = null;
        _keyCallback = null;
        _charCallback = null;
        _mouseButtonCallback = null;
        _cursorPosCallback = null;
        _scrollCallback = null;
        _cursorEnterCallback = null;
        _glfw.DestroyWindow(_handle);
    }
}
