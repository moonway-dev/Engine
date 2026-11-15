namespace Engine.Editor.Commands;

public interface IEditorCommand
{
    string Name { get; }

    void Execute();

    void Undo();
}

public interface IMergeableEditorCommand : IEditorCommand
{
    bool TryMerge(IEditorCommand subsequent);
}

public interface IEditorSnapshot
{
    void Restore();
}

public interface IEditorSnapshotSource
{
    IEditorSnapshot CaptureSnapshot();
}
