using System;
using System.Collections.Generic;
using MTEngine.World;

namespace MTEditor;

public class TileAction
{
    public int Layer { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public Tile OldTile { get; set; } = Tile.Empty;
    public Tile NewTile { get; set; } = Tile.Empty;
}

public class EditorHistory
{
    private sealed class TileBatchAction
    {
        public required List<TileAction> Changes { get; init; }
        public required long Order { get; init; }
    }

    private readonly EditorActionTracker _tracker;
    private readonly Stack<TileBatchAction> _undo = new();
    private readonly Stack<TileBatchAction> _redo = new();
    private List<TileAction>? _currentBatch;
    private long _redoGeneration = -1;

    // ячейки изменённые в текущем батче — чтобы не дублировать
    private readonly HashSet<(int, int, int)> _currentBatchCells = new();

    public int UndoCount => _undo.Count;
    public int RedoCount
    {
        get
        {
            InvalidateRedoIfNeeded();
            return _redo.Count;
        }
    }

    public long UndoOrder => _undo.Count > 0 ? _undo.Peek().Order : long.MinValue;

    public long RedoOrder
    {
        get
        {
            InvalidateRedoIfNeeded();
            return _redo.Count > 0 ? _redo.Peek().Order : long.MinValue;
        }
    }

    public bool IsBatchOpen => _currentBatch != null;

    public EditorHistory(EditorActionTracker tracker)
    {
        _tracker = tracker;
    }

    public void BeginBatch()
    {
        if (_currentBatch != null) return; // уже открыт
        _currentBatch = new List<TileAction>();
        _currentBatchCells.Clear();
    }

    public void Record(int layer, int x, int y, Tile oldTile, Tile newTile)
    {
        if (_currentBatch == null) return;

        // каждую ячейку пишем только один раз за батч
        // чтобы oldTile был реально старым, а не промежуточным
        if (_currentBatchCells.Contains((layer, x, y))) return;

        _currentBatch.Add(new TileAction
        {
            Layer = layer,
            X = x,
            Y = y,
            OldTile = oldTile.Clone(), // глубокая копия!
            NewTile = newTile.Clone()
        });
        _currentBatchCells.Add((layer, x, y));
    }

    public void CommitBatch()
    {
        if (_currentBatch == null) return;

        if (_currentBatch.Count > 0)
        {
            _undo.Push(new TileBatchAction
            {
                Changes = _currentBatch,
                Order = _tracker.CommitNewOperation()
            });
            _redo.Clear();
            _redoGeneration = -1;
            Console.WriteLine($"[History] Committed {_currentBatch.Count} tile changes. Undo stack: {_undo.Count}");
        }

        _currentBatch = null;
        _currentBatchCells.Clear();
    }

    public bool Undo(TileMap tileMap)
    {
        if (_undo.Count == 0)
        {
            Console.WriteLine("[History] Nothing to undo.");
            return false;
        }

        var batch = _undo.Pop();
        foreach (var action in batch.Changes)
            tileMap.SetTile(action.X, action.Y, action.OldTile.Clone(), action.Layer);

        _redo.Push(batch);
        _redoGeneration = _tracker.CurrentRedoGeneration;
        Console.WriteLine($"[History] Undid {batch.Changes.Count} tiles. Undo:{_undo.Count} Redo:{_redo.Count}");
        return true;
    }

    public bool Redo(TileMap tileMap)
    {
        InvalidateRedoIfNeeded();
        if (_redo.Count == 0)
        {
            Console.WriteLine("[History] Nothing to redo.");
            return false;
        }

        var batch = _redo.Pop();
        foreach (var action in batch.Changes)
            tileMap.SetTile(action.X, action.Y, action.NewTile.Clone(), action.Layer);

        _undo.Push(batch);
        Console.WriteLine($"[History] Redid {batch.Changes.Count} tiles. Undo:{_undo.Count} Redo:{_redo.Count}");
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _currentBatch = null;
        _currentBatchCells.Clear();
        _redoGeneration = -1;
    }

    private void InvalidateRedoIfNeeded()
    {
        if (_redo.Count == 0)
            return;

        if (_redoGeneration == _tracker.CurrentRedoGeneration)
            return;

        _redo.Clear();
        _redoGeneration = -1;
    }
}
