using Engine.Mathematics;
namespace Engine.Platform;

public sealed class KeyboardState
{
    private readonly HashSet<KeyCode> _current = [];
    private readonly HashSet<KeyCode> _previous = [];

    public KeyModifiers Modifiers { get; internal set; } = KeyModifiers.None;

    public bool IsDown(KeyCode key) => _current.Contains(key);

    public bool IsPressed(KeyCode key) => _current.Contains(key) && !_previous.Contains(key);

    public bool IsReleased(KeyCode key) => !_current.Contains(key) && _previous.Contains(key);

    public IReadOnlyCollection<KeyCode> DownKeys => _current;

    internal void SetDown(KeyCode key) => _current.Add(key);

    internal void SetUp(KeyCode key) => _current.Remove(key);

    internal void EndFrame()
    {
        _previous.Clear();
        foreach (KeyCode key in _current)
            _previous.Add(key);
    }

    internal void Reset()
    {
        _current.Clear();
        _previous.Clear();
        Modifiers = KeyModifiers.None;
    }
}
