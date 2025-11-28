using System;
using System.Collections.Generic;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Engine.Core;

public enum KeyCode
{
    None = 0,
    A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    Alpha0, Alpha1, Alpha2, Alpha3, Alpha4, Alpha5, Alpha6, Alpha7, Alpha8, Alpha9,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    Escape,
    Space,
    Tab,
    Enter,
    Backspace,
    LeftShift,
    RightShift,
    LeftControl,
    RightControl,
    LeftAlt,
    RightAlt,
    LeftSuper,
    RightSuper,
    CapsLock,
    UpArrow,
    DownArrow,
    LeftArrow,
    RightArrow,
    PageUp,
    PageDown,
    Home,
    End,
    Insert,
    Delete
}

public static class Input
{
    private const int MouseButtonCount = 3;

    private static readonly HashSet<KeyCode> _heldKeys = new();
    private static readonly HashSet<KeyCode> _keysDown = new();
    private static readonly HashSet<KeyCode> _keysUp = new();
    private static readonly HashSet<KeyCode> _keysDownBuffer = new();
    private static readonly HashSet<KeyCode> _keysUpBuffer = new();

    private static readonly bool[] _mouseButtons = new bool[MouseButtonCount];
    private static readonly bool[] _mouseButtonsDown = new bool[MouseButtonCount];
    private static readonly bool[] _mouseButtonsUp = new bool[MouseButtonCount];
    private static readonly bool[] _mouseButtonsDownBuffer = new bool[MouseButtonCount];
    private static readonly bool[] _mouseButtonsUpBuffer = new bool[MouseButtonCount];

    private static readonly Dictionary<Keys, KeyCode> _keyLookup = BuildKeyLookup();

    private static NativeWindow? _window;
    private static bool _initialized;
    private static bool _suppressNextMouseDelta;

    private static Vector2 _mousePosition;
    private static Vector2 _mouseDelta;
    private static Vector2 _pendingMouseDelta;
    private static float _scrollDelta;
    private static float _pendingScrollDelta;

    public static bool IsInitialized => _initialized;

    public static Vector2 MousePosition
    {
        get
        {
            EnsureInitialized();
            return _mousePosition;
        }
    }

    public static Vector2 MouseDelta
    {
        get
        {
            EnsureInitialized();
            return _mouseDelta;
        }
    }

    public static float ScrollDelta
    {
        get
        {
            EnsureInitialized();
            return _scrollDelta;
        }
    }

    public static void Initialize(NativeWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (_initialized)
        {
            if (!ReferenceEquals(_window, window))
                throw new InvalidOperationException("Input has already been initialized with a different window.");
            return;
        }

        _window = window;
        _window.KeyDown += OnKeyDown;
        _window.KeyUp += OnKeyUp;
        _window.MouseDown += OnMouseDown;
        _window.MouseUp += OnMouseUp;
        _window.MouseMove += OnMouseMove;
        _window.MouseWheel += OnMouseWheel;
        _window.FocusedChanged += OnFocusChanged;

        var startPos = _window.MousePosition;
        _mousePosition = new Vector2((float)startPos.X, (float)startPos.Y);

        _initialized = true;
    }

    public static void Update()
    {
        EnsureInitialized();

        _keysDown.Clear();
        foreach (KeyCode key in _keysDownBuffer)
            _keysDown.Add(key);
        _keysDownBuffer.Clear();

        _keysUp.Clear();
        foreach (KeyCode key in _keysUpBuffer)
            _keysUp.Add(key);
        _keysUpBuffer.Clear();

        for (int i = 0; i < MouseButtonCount; i++)
        {
            _mouseButtonsDown[i] = _mouseButtonsDownBuffer[i];
            _mouseButtonsUp[i] = _mouseButtonsUpBuffer[i];
            _mouseButtonsDownBuffer[i] = false;
            _mouseButtonsUpBuffer[i] = false;
        }

        _mouseDelta = _pendingMouseDelta;
        _pendingMouseDelta = Vector2.Zero;

        _scrollDelta = _pendingScrollDelta;
        _pendingScrollDelta = 0f;
    }

    public static bool GetKey(KeyCode key)
    {
        EnsureInitialized();
        return _heldKeys.Contains(key);
    }

    public static bool GetKeyDown(KeyCode key)
    {
        EnsureInitialized();
        return _keysDown.Contains(key);
    }

    public static bool GetKeyUp(KeyCode key)
    {
        EnsureInitialized();
        return _keysUp.Contains(key);
    }

    public static bool GetMouseButton(int button)
    {
        EnsureInitialized();
        return IsValidMouseButton(button) && _mouseButtons[button];
    }

    public static bool GetMouseButtonDown(int button)
    {
        EnsureInitialized();
        return IsValidMouseButton(button) && _mouseButtonsDown[button];
    }

    public static bool GetMouseButtonUp(int button)
    {
        EnsureInitialized();
        return IsValidMouseButton(button) && _mouseButtonsUp[button];
    }

    public static void OverrideMousePosition(Vector2 absolutePosition, bool suppressDelta = true)
    {
        EnsureInitialized();
        _mousePosition = absolutePosition;
        _pendingMouseDelta = Vector2.Zero;
        if (suppressDelta)
            _suppressNextMouseDelta = true;
    }

    private static void OnKeyDown(KeyboardKeyEventArgs e)
    {
        if (!_initialized || !_keyLookup.TryGetValue(e.Key, out KeyCode keyCode))
            return;

        if (_heldKeys.Add(keyCode))
            _keysDownBuffer.Add(keyCode);
    }

    private static void OnKeyUp(KeyboardKeyEventArgs e)
    {
        if (!_initialized || !_keyLookup.TryGetValue(e.Key, out KeyCode keyCode))
            return;

        if (_heldKeys.Remove(keyCode))
            _keysUpBuffer.Add(keyCode);
    }

    private static void OnMouseDown(MouseButtonEventArgs e)
    {
        if (!_initialized)
            return;

        int buttonIndex = (int)e.Button;
        if (!IsValidMouseButton(buttonIndex))
            return;

        if (!_mouseButtons[buttonIndex])
            _mouseButtonsDownBuffer[buttonIndex] = true;

        _mouseButtons[buttonIndex] = true;
    }

    private static void OnMouseUp(MouseButtonEventArgs e)
    {
        if (!_initialized)
            return;

        int buttonIndex = (int)e.Button;
        if (!IsValidMouseButton(buttonIndex))
            return;

        if (_mouseButtons[buttonIndex])
            _mouseButtonsUpBuffer[buttonIndex] = true;

        _mouseButtons[buttonIndex] = false;
    }

    private static void OnMouseMove(MouseMoveEventArgs e)
    {
        if (!_initialized)
            return;

        Vector2 current = new((float)e.X, (float)e.Y);

        if (_suppressNextMouseDelta)
        {
            _suppressNextMouseDelta = false;
        }
        else
        {
            Vector2 rawDelta = new((float)e.DeltaX, (float)e.DeltaY);
            if (rawDelta.X != 0f || rawDelta.Y != 0f)
            {
                _pendingMouseDelta += new Vector2(rawDelta.X, -rawDelta.Y);
            }
        }

        _mousePosition = current;
    }

    private static void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (!_initialized)
            return;

        _pendingScrollDelta += (float)e.OffsetY;
    }

    private static void OnFocusChanged(FocusedChangedEventArgs args)
    {
        if (!args.IsFocused)
            ResetState();
    }

    private static void ResetState()
    {
        _heldKeys.Clear();
        _keysDown.Clear();
        _keysUp.Clear();
        _keysDownBuffer.Clear();
        _keysUpBuffer.Clear();

        Array.Clear(_mouseButtons, 0, _mouseButtons.Length);
        Array.Clear(_mouseButtonsDown, 0, _mouseButtonsDown.Length);
        Array.Clear(_mouseButtonsUp, 0, _mouseButtonsUp.Length);
        Array.Clear(_mouseButtonsDownBuffer, 0, _mouseButtonsDownBuffer.Length);
        Array.Clear(_mouseButtonsUpBuffer, 0, _mouseButtonsUpBuffer.Length);

        _mouseDelta = Vector2.Zero;
        _pendingMouseDelta = Vector2.Zero;
        _scrollDelta = 0f;
        _pendingScrollDelta = 0f;
        _suppressNextMouseDelta = true;
    }

    private static bool IsValidMouseButton(int button) =>
        button >= 0 && button < MouseButtonCount;

    private static void EnsureInitialized()
    {
        if (!_initialized)
            throw new InvalidOperationException("Input.Initialize must be called before accessing input state.");
    }

    private static Dictionary<Keys, KeyCode> BuildKeyLookup()
    {
        return new Dictionary<Keys, KeyCode>
        {
            [Keys.A] = KeyCode.A,
            [Keys.B] = KeyCode.B,
            [Keys.C] = KeyCode.C,
            [Keys.D] = KeyCode.D,
            [Keys.E] = KeyCode.E,
            [Keys.F] = KeyCode.F,
            [Keys.G] = KeyCode.G,
            [Keys.H] = KeyCode.H,
            [Keys.I] = KeyCode.I,
            [Keys.J] = KeyCode.J,
            [Keys.K] = KeyCode.K,
            [Keys.L] = KeyCode.L,
            [Keys.M] = KeyCode.M,
            [Keys.N] = KeyCode.N,
            [Keys.O] = KeyCode.O,
            [Keys.P] = KeyCode.P,
            [Keys.Q] = KeyCode.Q,
            [Keys.R] = KeyCode.R,
            [Keys.S] = KeyCode.S,
            [Keys.T] = KeyCode.T,
            [Keys.U] = KeyCode.U,
            [Keys.V] = KeyCode.V,
            [Keys.W] = KeyCode.W,
            [Keys.X] = KeyCode.X,
            [Keys.Y] = KeyCode.Y,
            [Keys.Z] = KeyCode.Z,
            [Keys.D0] = KeyCode.Alpha0,
            [Keys.D1] = KeyCode.Alpha1,
            [Keys.D2] = KeyCode.Alpha2,
            [Keys.D3] = KeyCode.Alpha3,
            [Keys.D4] = KeyCode.Alpha4,
            [Keys.D5] = KeyCode.Alpha5,
            [Keys.D6] = KeyCode.Alpha6,
            [Keys.D7] = KeyCode.Alpha7,
            [Keys.D8] = KeyCode.Alpha8,
            [Keys.D9] = KeyCode.Alpha9,
            [Keys.F1] = KeyCode.F1,
            [Keys.F2] = KeyCode.F2,
            [Keys.F3] = KeyCode.F3,
            [Keys.F4] = KeyCode.F4,
            [Keys.F5] = KeyCode.F5,
            [Keys.F6] = KeyCode.F6,
            [Keys.F7] = KeyCode.F7,
            [Keys.F8] = KeyCode.F8,
            [Keys.F9] = KeyCode.F9,
            [Keys.F10] = KeyCode.F10,
            [Keys.F11] = KeyCode.F11,
            [Keys.F12] = KeyCode.F12,
            [Keys.Escape] = KeyCode.Escape,
            [Keys.Space] = KeyCode.Space,
            [Keys.Tab] = KeyCode.Tab,
            [Keys.Enter] = KeyCode.Enter,
            [Keys.Backspace] = KeyCode.Backspace,
            [Keys.LeftShift] = KeyCode.LeftShift,
            [Keys.RightShift] = KeyCode.RightShift,
            [Keys.LeftControl] = KeyCode.LeftControl,
            [Keys.RightControl] = KeyCode.RightControl,
            [Keys.LeftAlt] = KeyCode.LeftAlt,
            [Keys.RightAlt] = KeyCode.RightAlt,
            [Keys.LeftSuper] = KeyCode.LeftSuper,
            [Keys.RightSuper] = KeyCode.RightSuper,
            [Keys.CapsLock] = KeyCode.CapsLock,
            [Keys.Up] = KeyCode.UpArrow,
            [Keys.Down] = KeyCode.DownArrow,
            [Keys.Left] = KeyCode.LeftArrow,
            [Keys.Right] = KeyCode.RightArrow,
            [Keys.PageUp] = KeyCode.PageUp,
            [Keys.PageDown] = KeyCode.PageDown,
            [Keys.Home] = KeyCode.Home,
            [Keys.End] = KeyCode.End,
            [Keys.Insert] = KeyCode.Insert,
            [Keys.Delete] = KeyCode.Delete
        };
    }
}
