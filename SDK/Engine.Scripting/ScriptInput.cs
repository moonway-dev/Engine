namespace Engine.Scripting;

using Engine.Mathematics;
using Engine.Platform;

public sealed record InputAxis(
    KeyCode Negative,
    KeyCode Positive,
    KeyCode AltNegative = KeyCode.Unknown,
    KeyCode AltPositive = KeyCode.Unknown,
    float Sensitivity = 1f,
    float DeadZone = 0f)
{
    public float Evaluate(Func<KeyCode, bool> isDown)
    {
        float raw = (isDown(Positive) || isDown(AltPositive) ? 1f : 0f) - (isDown(Negative) || isDown(AltNegative) ? 1f : 0f);
        float magnitude = Math.Abs(raw);
        if (magnitude <= DeadZone)
            return 0f;
        float scaled = raw - MathF.Sign(raw) * DeadZone;
        return Math.Clamp(scaled / (1f - DeadZone), -1f, 1f) * Sensitivity;
    }
}

public static class ScriptInput
{
    private const float MouseAxisScale = 0.1f;
    private static readonly Dictionary<string, KeyCode[]> Actions = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, InputAxis> Axes = new(StringComparer.OrdinalIgnoreCase);

    internal static IWindow? Window { get; set; }

    static ScriptInput()
    {
        Axes["Horizontal"] = new InputAxis(KeyCode.A, KeyCode.D, KeyCode.Left, KeyCode.Right);
        Axes["Vertical"] = new InputAxis(KeyCode.S, KeyCode.W, KeyCode.Down, KeyCode.Up);
    }

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

    public static void DefineAxis(string name, InputAxis axis)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Axes[name] = axis;
    }

    public static void DefineAxis(string name, KeyCode negative, KeyCode positive, float sensitivity = 1f, float deadZone = 0f) =>
        DefineAxis(name, new InputAxis(negative, positive, Sensitivity: sensitivity, DeadZone: deadZone));

    public static void RemoveAxis(string name) => Axes.Remove(name);

    public static void ClearAxes() => Axes.Clear();

    public static IReadOnlyCollection<string> AxisNames => Axes.Keys;

    public static float GetAxis(string name)
    {
        if (string.Equals(name, "Mouse X", StringComparison.OrdinalIgnoreCase))
            return (Window?.Mouse.Delta.X ?? 0f) * MouseAxisScale;
        if (string.Equals(name, "Mouse Y", StringComparison.OrdinalIgnoreCase))
            return (Window?.Mouse.Delta.Y ?? 0f) * MouseAxisScale;
        if (string.Equals(name, "Mouse ScrollWheel", StringComparison.OrdinalIgnoreCase))
            return (Window?.Mouse.ScrollDelta.Y ?? 0f) * MouseAxisScale;
        return Axes.TryGetValue(name, out InputAxis? axis) ? axis.Evaluate(IsKeyDown) : 0f;
    }

    public static bool TryGetAxis(string name, out float value)
    {
        value = GetAxis(name);
        return Axes.ContainsKey(name)
            || string.Equals(name, "Mouse X", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Mouse Y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Mouse ScrollWheel", StringComparison.OrdinalIgnoreCase);
    }

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

    public static void SetCursorVisible(bool visible) =>
        CursorMode = IsCursorLocked
            ? Platform.CursorMode.Disabled
            : visible ? Platform.CursorMode.Normal : Platform.CursorMode.Hidden;

    public static bool IsCursorLocked => Window?.CursorMode == Platform.CursorMode.Disabled;

    public static bool IsCursorVisible => Window?.CursorMode is not (Platform.CursorMode.Hidden or Platform.CursorMode.Disabled);
}
