using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MTEngine.Core;
using MTEngine.World;

namespace MTEditor.Tools;

public class TilePainterTool
{
    private MapData _map;
    private TileMap _tileMap;
    private readonly PrototypeManager _prototypes;
    private readonly AssetManager _assets;
    private EditorHistory _history;

    private bool _wasPaintingLeft = false;
    private bool _wasPaintingRight = false;
    private Point? _leftAnchor;
    private Point? _rightAnchor;

    public TilePainterTool(MapData map, TileMap tileMap, PrototypeManager prototypes,
                            AssetManager assets, EditorHistory history)
    {
        _map = map;
        _tileMap = tileMap;
        _prototypes = prototypes;
        _assets = assets;
        _history = history;
    }

    public void SetMap(MapData map, TileMap tileMap)
    {
        _map = map;
        _tileMap = tileMap;
    }

    public void SetHistory(EditorHistory history)
    {
        _history = history;
    }

    public void Update(MouseState mouse, MouseState prev, Vector2 worldPos, string? selectedTileId)
        => Update(mouse, prev, worldPos, selectedTileId, 0);

    public void Update(MouseState mouse, MouseState prev, Vector2 worldPos, string? selectedTileId, int activeLayer)
        => Update(mouse, prev, worldPos, selectedTileId, activeLayer, BrushShape.Point);

    public void Update(MouseState mouse, MouseState prev, Vector2 worldPos, string? selectedTileId, int activeLayer, BrushShape brushShape)
    {
        var tileX = (int)Math.Floor(worldPos.X / _map.TileSize);
        var tileY = (int)Math.Floor(worldPos.Y / _map.TileSize);

        bool inBounds = tileX >= 0 && tileX < _map.Width && tileY >= 0 && tileY < _map.Height;

        bool isLeft = mouse.LeftButton == ButtonState.Pressed;
        bool isRight = mouse.RightButton == ButtonState.Pressed;

        if (isLeft && !_wasPaintingLeft)
        {
            _history.BeginBatch();
            _leftAnchor = new Point(tileX, tileY);
        }

        if (isRight && !_wasPaintingRight)
        {
            _history.BeginBatch();
            _rightAnchor = new Point(tileX, tileY);
        }

        if (inBounds)
        {
            if (brushShape == BrushShape.Point)
            {
                if (isLeft && selectedTileId != null)
                    PaintTile(tileX, tileY, selectedTileId, activeLayer);

                if (isRight)
                    EraseTile(tileX, tileY, activeLayer);
            }
        }

        if (!isLeft && _wasPaintingLeft)
        {
            if (brushShape != BrushShape.Point && _leftAnchor.HasValue && selectedTileId != null)
                ApplyShape(_leftAnchor.Value, new Point(tileX, tileY), brushShape, activeLayer, point => PaintTile(point.X, point.Y, selectedTileId, activeLayer));
            _history.CommitBatch();
            _leftAnchor = null;
        }

        if (!isRight && _wasPaintingRight)
        {
            if (brushShape != BrushShape.Point && _rightAnchor.HasValue)
                ApplyShape(_rightAnchor.Value, new Point(tileX, tileY), brushShape, activeLayer, point => EraseTile(point.X, point.Y, activeLayer));
            _history.CommitBatch();
            _rightAnchor = null;
        }

        _wasPaintingLeft = isLeft;
        _wasPaintingRight = isRight;
    }

    public IReadOnlyList<Point> GetPreviewPoints(Vector2 worldPos, BrushShape brushShape)
    {
        var tileX = (int)MathF.Floor(worldPos.X / _map.TileSize);
        var tileY = (int)MathF.Floor(worldPos.Y / _map.TileSize);
        var current = new Point(tileX, tileY);

        if (brushShape != BrushShape.Point)
        {
            var mouse = Mouse.GetState();
            var anchor = mouse.LeftButton == ButtonState.Pressed ? _leftAnchor
                : mouse.RightButton == ButtonState.Pressed ? _rightAnchor
                : null;

            if (anchor.HasValue)
            {
                return EnumerateShapePoints(anchor.Value, current, brushShape)
                    .Where(point => IsInBounds(point.X, point.Y))
                    .Distinct()
                    .ToArray();
            }
        }

        return IsInBounds(tileX, tileY) ? new[] { current } : Array.Empty<Point>();
    }

    private void PaintTile(int tileX, int tileY, string selectedTileId, int activeLayer)
    {
        if (!IsInBounds(tileX, tileY))
            return;

        var proto = _prototypes.GetTile(selectedTileId);
        if (proto == null)
            return;

        var oldTile = _tileMap.GetTile(tileX, tileY, activeLayer);
        var newTile = new Tile
        {
            ProtoId = selectedTileId,
            Solid = proto.Solid,
            Transparent = proto.Transparent,
            Type = proto.Solid ? TileType.Wall : TileType.Floor
        };

        if (oldTile.ProtoId == newTile.ProtoId)
            return;

        _history.Record(activeLayer, tileX, tileY, oldTile, newTile);
        _tileMap.SetTile(tileX, tileY, newTile, activeLayer);
    }

    private void EraseTile(int tileX, int tileY, int activeLayer)
    {
        if (!IsInBounds(tileX, tileY))
            return;

        var oldTile = _tileMap.GetTile(tileX, tileY, activeLayer);
        if (oldTile.Type == TileType.Empty)
            return;

        _history.Record(activeLayer, tileX, tileY, oldTile, Tile.Empty);
        _tileMap.SetTile(tileX, tileY, Tile.Empty, activeLayer);
    }

    private void ApplyShape(Point start, Point end, BrushShape brushShape, int activeLayer, Action<Point> apply)
    {
        foreach (var point in EnumerateShapePoints(start, end, brushShape))
            apply(point);
    }

    private IEnumerable<Point> EnumerateShapePoints(Point start, Point end, BrushShape brushShape)
    {
        return brushShape switch
        {
            BrushShape.Line => EnumerateLine(start, end),
            BrushShape.FilledRectangle => EnumerateFilledRectangle(start, end),
            BrushShape.HollowRectangle => EnumerateHollowRectangle(start, end),
            _ => new[] { end }
        };
    }

    private static IEnumerable<Point> EnumerateLine(Point start, Point end)
    {
        var x0 = start.X;
        var y0 = start.Y;
        var x1 = end.X;
        var y1 = end.Y;
        var dx = Math.Abs(x1 - x0);
        var sx = x0 < x1 ? 1 : -1;
        var dy = -Math.Abs(y1 - y0);
        var sy = y0 < y1 ? 1 : -1;
        var err = dx + dy;

        while (true)
        {
            yield return new Point(x0, y0);
            if (x0 == x1 && y0 == y1)
                break;

            var e2 = 2 * err;
            if (e2 >= dy)
            {
                err += dy;
                x0 += sx;
            }
            if (e2 <= dx)
            {
                err += dx;
                y0 += sy;
            }
        }
    }

    private static IEnumerable<Point> EnumerateFilledRectangle(Point start, Point end)
    {
        var minX = Math.Min(start.X, end.X);
        var maxX = Math.Max(start.X, end.X);
        var minY = Math.Min(start.Y, end.Y);
        var maxY = Math.Max(start.Y, end.Y);

        for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
                yield return new Point(x, y);
    }

    private static IEnumerable<Point> EnumerateHollowRectangle(Point start, Point end)
    {
        var minX = Math.Min(start.X, end.X);
        var maxX = Math.Max(start.X, end.X);
        var minY = Math.Min(start.Y, end.Y);
        var maxY = Math.Max(start.Y, end.Y);

        for (var x = minX; x <= maxX; x++)
        {
            yield return new Point(x, minY);
            if (maxY != minY)
                yield return new Point(x, maxY);
        }

        for (var y = minY + 1; y < maxY; y++)
        {
            yield return new Point(minX, y);
            if (maxX != minX)
                yield return new Point(maxX, y);
        }
    }

    private bool IsInBounds(int tileX, int tileY)
        => tileX >= 0 && tileX < _map.Width && tileY >= 0 && tileY < _map.Height;
}
