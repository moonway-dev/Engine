using Engine.Platform;

namespace Engine.Editor.Input;

public readonly record struct EditorHotkey(KeyCode Key, KeyModifiers Modifiers = KeyModifiers.None)
{
    public bool Matches(KeyCode key, KeyModifiers modifiers) =>
        Key == key && Normalize(modifiers) == Normalize(Modifiers);

    private static KeyModifiers Normalize(KeyModifiers modifiers) =>
        modifiers & (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Super);
}

public sealed class EditorHotkeyBinding
{
    public string Id { get; }

    public string DisplayName { get; }

    public EditorHotkey Hotkey { get; set; }

    public Func<bool>? CanExecute { get; set; }

    public Action Execute { get; set; }

    public EditorHotkeyBinding(string id, string displayName, EditorHotkey hotkey, Action execute)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        Id = id;
        DisplayName = displayName;
        Hotkey = hotkey;
        Execute = execute ?? throw new ArgumentNullException(nameof(execute));
    }
}

public sealed class EditorHotkeyService
{
    private readonly Dictionary<string, EditorHotkeyBinding> _bindings = new(StringComparer.Ordinal);

    public IReadOnlyCollection<EditorHotkeyBinding> Bindings => _bindings.Values;

    public void Register(EditorHotkeyBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (!_bindings.TryAdd(binding.Id, binding))
            throw new InvalidOperationException($"A hotkey binding with ID '{binding.Id}' is already registered.");
    }

    public bool Remove(string id) => _bindings.Remove(id);

    public bool TryHandle(KeyCode key, KeyModifiers modifiers, bool textInputActive = false)
    {
        foreach (EditorHotkeyBinding binding in _bindings.Values)
        {
            if (!binding.Hotkey.Matches(key, modifiers))
                continue;
            if (textInputActive)
                continue;
            if (binding.CanExecute?.Invoke() == false)
                continue;
            binding.Execute();
            return true;
        }
        return false;
    }

    public bool Process(KeyboardState keyboard, bool textInputActive = false)
    {
        ArgumentNullException.ThrowIfNull(keyboard);
        KeyModifiers modifiers = GetModifiers(keyboard);
        foreach (KeyCode key in keyboard.DownKeys)
        {
            if (keyboard.IsPressed(key) && TryHandle(key, modifiers, textInputActive))
                return true;
        }
        return false;
    }

    public static KeyModifiers GetModifiers(KeyboardState keyboard)
    {
        KeyModifiers modifiers = KeyModifiers.None;
        if (keyboard.IsDown(KeyCode.LeftControl) || keyboard.IsDown(KeyCode.RightControl))
            modifiers |= KeyModifiers.Control;
        if (keyboard.IsDown(KeyCode.LeftShift) || keyboard.IsDown(KeyCode.RightShift))
            modifiers |= KeyModifiers.Shift;
        if (keyboard.IsDown(KeyCode.LeftAlt) || keyboard.IsDown(KeyCode.RightAlt))
            modifiers |= KeyModifiers.Alt;
        if (keyboard.IsDown(KeyCode.LeftSuper) || keyboard.IsDown(KeyCode.RightSuper))
            modifiers |= KeyModifiers.Super;
        return modifiers;
    }
}
