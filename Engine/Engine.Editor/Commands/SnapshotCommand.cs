namespace Engine.Editor.Commands;

public sealed class SnapshotCommand : IEditorCommand
{
    private readonly IEditorSnapshotSource _source;
    private readonly Action _mutation;
    private IEditorSnapshot? _before;
    private IEditorSnapshot? _after;

    public string Name { get; }

    public SnapshotCommand(string name, IEditorSnapshotSource source, Action mutation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _mutation = mutation ?? throw new ArgumentNullException(nameof(mutation));
    }

    public void Execute()
    {
        if (_after is not null)
        {
            _after.Restore();
            return;
        }
        _before = _source.CaptureSnapshot();
        try
        {
            _mutation();
            _after = _source.CaptureSnapshot();
        }
        catch
        {
            _before.Restore();
            _before = null;
            throw;
        }
    }

    public void Undo()
    {
        if (_before is null)
            throw new InvalidOperationException("The command has not been executed.");
        _before.Restore();
    }
}
