namespace MTEditor;

public sealed class EditorActionTracker
{
    private long _nextOrder;
    private long _redoGeneration;

    public long CurrentRedoGeneration => _redoGeneration;

    public long CommitNewOperation()
    {
        _redoGeneration++;
        _nextOrder++;
        return _nextOrder;
    }

    public void Reset()
    {
        _nextOrder = 0;
        _redoGeneration = 0;
    }
}
