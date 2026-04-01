using System;
using System.Collections.Generic;
using MTEngine.World;

namespace MTEditor;

public class TileAction
{
    public int X { get; set; }
    public int Y { get; set; }
    public Tile OldTile { get; set; } = Tile.Empty;
    public Tile NewTile { get; set; } = Tile.Empty;
}

public class EditorHistory
{
    private readonly Stack<List<TileAction>> _undo = new();
    private readonly Stack<List<TileAction>> _redo = new();
    private List<TileAction>? _currentBatch;

    // ячейки изменённые в текущем батче — чтобы не дублировать
    private readonly HashSet<(int, int)> _currentBatchCells = new();

    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;

    public bool IsBatchOpen => _currentBatch != null;

    public void BeginBatch()
    {
        if (_currentBatch != null) return; // уже открыт
        _currentBatch = new List<TileAction>();
        _currentBatchCells.Clear();
    }

    public void Record(int x, int y, Tile oldTile, Tile newTile)
    {
        if (_currentBatch == null) return;

        // каждую ячейку пишем только один раз за батч
        // чтобы oldTile был реально старым, а не промежуточным
        if (_currentBatchCells.Contains((x, y))) return;

        _currentBatch.Add(new TileAction
        {
            X = x,
            Y = y,
            OldTile = oldTile.Clone(), // глубокая копия!
            NewTile = newTile.Clone()
        });
        _currentBatchCells.Add((x, y));
    }

    public void CommitBatch()
    {
        if (_currentBatch == null) return;

        if (_currentBatch.Count > 0)
        {
            _undo.Push(_currentBatch);
            _redo.Clear();
            Console.WriteLine($"[History] Committed {_currentBatch.Count} tile changes. Undo stack: {_undo.Count}");
        }

        _currentBatch = null;
        _currentBatchCells.Clear();
    }

    public void Undo(TileMap tileMap)
    {
        if (_undo.Count == 0)
        {
            Console.WriteLine("[History] Nothing to undo.");
            return;
        }

        var batch = _undo.Pop();
        foreach (var action in batch)
            tileMap.SetTile(action.X, action.Y, action.OldTile.Clone());

        _redo.Push(batch);
        Console.WriteLine($"[History] Undid {batch.Count} tiles. Undo:{_undo.Count} Redo:{_redo.Count}");
    }

    public void Redo(TileMap tileMap)
    {
        if (_redo.Count == 0)
        {
            Console.WriteLine("[History] Nothing to redo.");
            return;
        }

        var batch = _redo.Pop();
        foreach (var action in batch)
            tileMap.SetTile(action.X, action.Y, action.NewTile.Clone());

        _undo.Push(batch);
        Console.WriteLine($"[History] Redid {batch.Count} tiles. Undo:{_undo.Count} Redo:{_redo.Count}");
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _currentBatch = null;
        _currentBatchCells.Clear();
    }
}