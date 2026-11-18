using Engine.Mathematics;
namespace Engine.Platform;

public interface IWindow : IDisposable
{
    string Title { get; set; }

    Size2D ClientSize { get; }

    Size2D FramebufferSize { get; }

    Vector2 Position { get; set; }

    bool IsVisible { get; }

    bool IsFocused { get; }

    bool IsMinimized { get; }

    bool ShouldClose { get; }

    CursorMode CursorMode { get; set; }

    void SetCursorPosition(Vector2 position);

    void ResetMouseDelta();

    KeyboardState Keyboard { get; }

    MouseState Mouse { get; }

    event Action? CloseRequested;

    event Action<Size2D>? Resized;

    event Action<bool>? FocusChanged;

    event Action<KeyCode, KeyModifiers>? KeyPressed;

    event Action<KeyCode, KeyModifiers>? KeyReleased;

    event Action<string>? TextInput;

    event Action<Vector2>? MouseMoved;

    event Action<MouseButton, KeyModifiers>? MouseButtonPressed;

    event Action<MouseButton, KeyModifiers>? MouseButtonReleased;

    event Action<Vector2>? MouseScrolled;

    void UpdateInputStates();

    NativeHandle GetNativeHandle();

    SurfaceSource GetSurfaceSource();

    void Focus();

    void Close();
}
