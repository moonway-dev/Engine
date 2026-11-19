namespace Engine.Scripting;

using Engine.Mathematics;
using Engine.Platform;

public static class ScriptInput
{
    private static readonly Dictionary<string, KeyCode[]> Actions = new(StringComparer.OrdinalIgnoreCase);

    internal static IWindow? Window { get; set; }

    public static Vector2 MousePosition => Window?.Mouse.Position ?? Vector2.Zero;

    public static Vector2 MouseDelta => Window?.Mouse.Delta ?? Vector2.Zero;

    public static Vector2 ScrollDelta => Window?.Mouse.ScrollDelta ?? Vector2.Zero;

    public static bool IsMouseInsideWindow => Window?.Mouse.IsInsideWindow ?? false;

    public static KeyModifiers Modifiers => Window?.Keyboard.Modifiers ?? KeyModifiers.None;

    public static IReadOnlyCollection<KeyCode> DownKeys => Window?.Keyboard.DownKeys ?? [];

    public static IReadOnlyCollection<MouseButton> DownMouseButtons => Window?.Mouse.DownButtons ?? [];

    public static bool IsKeyDown(KeyCode key) => Window?.Keyboard.IsDown(key) ?? false;

    public static bool IsKeyPressed(KeyCode key) => Window?.Keyboard.IsPressed(key) ?? false;

    public static bool IsKeyReleased(KeyCode key) => Window?.Keyboard.IsReleased(key) ?? false;

    public static bool IsAnyKeyDown() => Window is not null && Window.Keyboard.DownKeys.Count > 0;

    public static bool IsMouseDown(MouseButton button) => Window?.Mouse.IsDown(button) ?? false;

    public static bool IsMousePressed(MouseButton button) => Window?.Mouse.IsPressed(button) ?? false;

    public static bool IsMouseReleased(MouseButton button) => Window?.Mouse.IsReleased(button) ?? false;

    public static void DefineAction(string name, params KeyCode[] keys)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Actions[name] = keys.Length == 0 ? throw new ArgumentException("Action needs at least one key.", nameof(keys)) : keys;
    }

    public static void RemoveAction(string name) => Actions.Remove(name);

    public static void ClearActions() => Actions.Clear();

    public static IReadOnlyCollection<string> ActionNames => Actions.Keys;

    public static bool IsActionDown(string name) =>
        Actions.TryGetValue(name, out KeyCode[]? keys) && keys.Any(IsKeyDown);

    public static bool IsActionPressed(string name) =>
        Actions.TryGetValue(name, out KeyCode[]? keys) && keys.Any(IsKeyPressed);

    public static bool IsActionReleased(string name) =>
        Actions.TryGetValue(name, out KeyCode[]? keys) && keys.Any(IsKeyReleased);

    public static CursorMode CursorMode
    {
        get => Window?.CursorMode ?? Platform.CursorMode.Normal;
        set
        {
            if (Window is not null)
                Window.CursorMode = value;
        }
    }

    public static void SetCursorLocked(bool locked) =>
        CursorMode = locked ? Platform.CursorMode.Disabled : Platform.CursorMode.Normal;

    public static bool IsCursorLocked => Window?.CursorMode == Platform.CursorMode.Disabled;
}
