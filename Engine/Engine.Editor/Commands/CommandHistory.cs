namespace Engine.Editor.Commands;

public sealed class CommandHistory
{
    private readonly List<Entry> _undo = [];
    private readonly List<Entry> _redo = [];
    private long _nextStateId = 1;
    private long _currentStateId;
    private long _savedStateId;
    private long _mergeGeneration;

    public event EventHandler? Changed;

    public int UndoCount => _undo.Count;

    public int RedoCount => _redo.Count;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string? UndoName => CanUndo ? _undo[^1].Command.Name : null;

    public string? RedoName => CanRedo ? _redo[^1].Command.Name : null;

    public long CurrentStateId => _currentStateId;

    public long SavedStateId => _savedStateId;

    public bool IsAtSavePoint => _currentStateId == _savedStateId;

    public void Execute(IEditorCommand command, bool allowMerge = true)
    {
        ArgumentNullException.ThrowIfNull(command);
        long beforeStateId = _currentStateId;
        command.Execute();
        long afterStateId = _nextStateId++;
        if (allowMerge && _redo.Count == 0 && _undo.Count > 0
            && _undo[^1].MergeGeneration == _mergeGeneration
            && _undo[^1].AfterStateId != _savedStateId
            && _undo[^1].Command is IMergeableEditorCommand mergeable
            && mergeable.TryMerge(command))
        {
            Entry previous = _undo[^1];
            _undo[^1] = previous with { AfterStateId = afterStateId };
        }
        else
        {
            _undo.Add(new Entry(command, beforeStateId, afterStateId, _mergeGeneration));
        }
        _redo.Clear();
        _currentStateId = afterStateId;
        if (!allowMerge)
            _mergeGeneration++;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Undo()
    {
        if (!CanUndo)
            return false;
        Entry entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        try
        {
            entry.Command.Undo();
        }
        catch
        {
            _undo.Add(entry);
            throw;
        }
        _redo.Add(entry);
        _currentStateId = entry.BeforeStateId;
        _mergeGeneration++;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Redo()
    {
        if (!CanRedo)
            return false;
        Entry entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        try
        {
            entry.Command.Execute();
        }
        catch
        {
            _redo.Add(entry);
            throw;
        }
        _undo.Add(entry);
        _currentStateId = entry.AfterStateId;
        _mergeGeneration++;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void EndMergeGroup() => _mergeGeneration++;

    public void MarkSavePoint()
    {
        if (_savedStateId == _currentStateId)
            return;
        _savedStateId = _currentStateId;
        _mergeGeneration++;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Reset(bool markSaved = true)
    {
        _undo.Clear();
        _redo.Clear();
        _currentStateId = _nextStateId++;
        _savedStateId = markSaved ? _currentStateId : 0;
        _mergeGeneration++;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed record Entry(IEditorCommand Command, long BeforeStateId, long AfterStateId, long MergeGeneration);
}
