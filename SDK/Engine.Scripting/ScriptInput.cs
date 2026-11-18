namespace Engine.Scripting;

using Engine.Mathematics;
using Engine.Platform;

public static class ScriptInput
{
    internal static IWindow? Window { get; set; }

    public static bool IsKeyDown(KeyCode key) => Window?.Keyboard.IsDown(key) ?? false;

    public static bool IsKeyPressed(KeyCode key) => Window?.Keyboard.IsPressed(key) ?? false;

    public static bool IsKeyReleased(KeyCode key) => Window?.Keyboard.IsReleased(key) ?? false;

    public static bool IsMouseDown(MouseButton button) => Window?.Mouse.IsDown(button) ?? false;

    public static bool IsMousePressed(MouseButton button) => Window?.Mouse.IsPressed(button) ?? false;

    public static bool IsMouseReleased(MouseButton button) => Window?.Mouse.IsReleased(button) ?? false;

    public static Vector2 MousePosition => Window?.Mouse.Position ?? Vector2.Zero;

    public static Vector2 MouseDelta => Window?.Mouse.Delta ?? Vector2.Zero;
}
