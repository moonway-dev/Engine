namespace Engine.Editor.Commands;

public sealed class PropertyCommand<T> : IMergeableEditorCommand
{
    private readonly Func<T> _read;
    private readonly Action<T> _write;
    private readonly object _mergeKey;
    private readonly T _before;
    private T _after;

    public string Name { get; }

    public PropertyCommand(
        string name,
        object mergeKey,
        Func<T> read,
        Action<T> write,
        T before,
        T after)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        _mergeKey = mergeKey ?? throw new ArgumentNullException(nameof(mergeKey));
        _read = read ?? throw new ArgumentNullException(nameof(read));
        _write = write ?? throw new ArgumentNullException(nameof(write));
        _before = before;
        _after = after;
    }

    public PropertyCommand(
        string name,
        object mergeKey,
        Func<T> read,
        Action<T> write,
        T after)
        : this(name, mergeKey, read, write, read(), after)
    {
    }

    public void Execute() => _write(_after);

    public void Undo() => _write(_before);

    public bool TryMerge(IEditorCommand subsequent)
    {
        if (subsequent is not PropertyCommand<T> property
            || !Equals(_mergeKey, property._mergeKey))
            return false;
        _after = property._after;
        return true;
    }

    public T ReadCurrent() => _read();
}
