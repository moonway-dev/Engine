using Engine.Editor.Commands;
using Engine.Editor.Documents;

namespace Engine.Editor.Inspector;

public sealed class InspectorPropertyBinding<T>
{
    private readonly SceneDocument _document;
    private readonly Func<T> _read;
    private readonly Action<T> _write;
    private readonly object _mergeKey;
    private readonly IEqualityComparer<T> _comparer;

    public string Name { get; }

    public T Value => _read();

    public InspectorPropertyBinding(
        SceneDocument document,
        string name,
        object mergeKey,
        Func<T> read,
        Action<T> write,
        IEqualityComparer<T>? comparer = null)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        _mergeKey = mergeKey ?? throw new ArgumentNullException(nameof(mergeKey));
        _read = read ?? throw new ArgumentNullException(nameof(read));
        _write = write ?? throw new ArgumentNullException(nameof(write));
        _comparer = comparer ?? EqualityComparer<T>.Default;
    }

    public bool Set(T value, bool allowMerge = true)
    {
        T before = _read();
        if (_comparer.Equals(before, value))
            return false;
        _document.Execute(new PropertyCommand<T>(
            $"Set {Name}",
            _mergeKey,
            _read,
            _write,
            before,
            value), allowMerge);
        return true;
    }
}

public static class InspectorBindingFactory
{
    public static InspectorPropertyBinding<T> ForObject<T>(
        SceneDocument document,
        Guid objectId,
        string propertyName,
        Func<Engine.Scene.GameObject, T> read,
        Action<Engine.Scene.GameObject, T> write)
    {
        return new InspectorPropertyBinding<T>(
            document,
            propertyName,
            (objectId, propertyName),
            () => read(document.Scene.FindById(objectId)
                ?? throw new InvalidOperationException("The inspected GameObject no longer exists.")),
            value => write(document.Scene.FindById(objectId)
                ?? throw new InvalidOperationException("The inspected GameObject no longer exists."), value));
    }

    public static InspectorPropertyBinding<T> ForComponent<T>(
        SceneDocument document,
        Guid componentId,
        string propertyName,
        Func<Engine.Scene.Component, T> read,
        Action<Engine.Scene.Component, T> write)
    {
        return new InspectorPropertyBinding<T>(
            document,
            propertyName,
            (componentId, propertyName),
            () => read(document.Scene.FindComponentById(componentId)
                ?? throw new InvalidOperationException("The inspected component no longer exists.")),
            value => write(document.Scene.FindComponentById(componentId)
                ?? throw new InvalidOperationException("The inspected component no longer exists."), value));
    }
}
