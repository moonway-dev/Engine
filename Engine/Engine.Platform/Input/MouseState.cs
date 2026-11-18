using Engine.Mathematics;
namespace Engine.Platform;

public sealed class MouseState
{
    private readonly HashSet<MouseButton> _current = [];
    private readonly HashSet<MouseButton> _previous = [];

    public Vector2 Position { get; private set; }
    public Vector2 Delta { get; private set; }
    public Vector2 ScrollDelta { get; private set; }
    public bool IsInsideWindow { get; private set; }

    public bool IsDown(MouseButton button) => _current.Contains(button);

    public bool IsPressed(MouseButton button) => _current.Contains(button) && !_previous.Contains(button);

    public bool IsReleased(MouseButton button) => !_current.Contains(button) && _previous.Contains(button);

    public IReadOnlyCollection<MouseButton> DownButtons => _current;

    internal void SetPosition(Vector2 position)
    {
        Delta += position - Position;
        Position = position;
    }

    internal void ResetDelta() => Delta = Vector2.Zero;

    internal void SetInsideWindow(bool inside) => IsInsideWindow = inside;

    internal void AddScroll(Vector2 delta) => ScrollDelta += delta;

    internal void SetDown(MouseButton button) => _current.Add(button);

    internal void SetUp(MouseButton button) => _current.Remove(button);

    internal void EndFrame()
    {
        Delta = Vector2.Zero;
        ScrollDelta = Vector2.Zero;
        _previous.Clear();
        foreach (MouseButton button in _current)
            _previous.Add(button);
    }

    internal void Reset()
    {
        _current.Clear();
        _previous.Clear();
        Position = Vector2.Zero;
        Delta = Vector2.Zero;
        ScrollDelta = Vector2.Zero;
        IsInsideWindow = false;
    }
}
