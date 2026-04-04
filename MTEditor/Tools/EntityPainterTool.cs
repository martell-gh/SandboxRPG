using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MTEngine.Core;
using MTEngine.Rendering;
using MTEngine.World;

namespace MTEditor.Tools;

public class EntityPainterTool
{
    private sealed class EntitySnapshotAction
    {
        public required List<MapEntityData> Before { get; init; }
        public required List<MapEntityData> After { get; init; }
    }

    private sealed class SpriteMaskData
    {
        public Rectangle OpaqueBounds { get; init; }
        public Point[] EdgePixels { get; init; } = Array.Empty<Point>();
    }

    private MapData _map;
    private readonly Dictionary<string, SpriteMaskData> _spriteMaskCache = new();

    private Vector2? _leftBrushAnchor;
    private Vector2? _rightBrushAnchor;
    private bool _wasPaintingLeft;
    private bool _wasPaintingRight;

    private readonly List<MapEntityData> _selectedEntities = new();
    private readonly Dictionary<MapEntityData, Vector2> _dragStartPositions = new();
    private readonly Stack<EntitySnapshotAction> _undo = new();
    private readonly Stack<EntitySnapshotAction> _redo = new();
    private MapEntityData? _hoveredEntity;
    private bool _isDraggingSelection;
    private bool _isBoxSelecting;
    private Vector2 _dragAnchorWorld;
    private Point _selectionStartScreen;
    private Point _selectionCurrentScreen;
    private List<MapEntityData>? _pendingBrushSnapshot;
    private bool _brushChanged;
    private List<MapEntityData>? _pendingDragSnapshot;
    private bool _dragChanged;

    public EntityPainterTool(MapData map)
    {
        _map = map;
    }

    public void SetMap(MapData map)
    {
        _map = map;
        _selectedEntities.Clear();
        _dragStartPositions.Clear();
        _hoveredEntity = null;
        _isDraggingSelection = false;
        _isBoxSelecting = false;
        _pendingBrushSnapshot = null;
        _pendingDragSnapshot = null;
        _brushChanged = false;
        _dragChanged = false;
        _undo.Clear();
        _redo.Clear();
    }

    public void UpdateBrush(MouseState mouse, MouseState prev, Vector2 worldPos, string selectedEntityId, BrushShape brushShape)
    {
        var snapToGrid = IsSnapModifierDown();
        var current = NormalizeBrushPosition(worldPos, snapToGrid);
        var inBounds = IsInBounds(worldPos);

        var isLeft = mouse.LeftButton == ButtonState.Pressed;
        var isRight = mouse.RightButton == ButtonState.Pressed;

        if (isLeft && !_wasPaintingLeft)
        {
            _leftBrushAnchor = current;
            _pendingBrushSnapshot = CloneEntities(_map.Entities);
            _brushChanged = false;
        }

        if (isRight && !_wasPaintingRight)
        {
            _rightBrushAnchor = current;
            _pendingBrushSnapshot = CloneEntities(_map.Entities);
            _brushChanged = false;
        }

        if (brushShape == BrushShape.Point && inBounds)
        {
            if (isLeft)
                _brushChanged |= PlaceEntity(current, selectedEntityId);

            if (isRight)
                _brushChanged |= RemoveEntity(current);
        }

        if (!isLeft && _wasPaintingLeft)
        {
            if (brushShape != BrushShape.Point && _leftBrushAnchor.HasValue)
                foreach (var point in EnumerateShapePoints(_leftBrushAnchor.Value, current, brushShape))
                    _brushChanged |= PlaceEntity(point, selectedEntityId);

            CommitPendingBrushSnapshot();
            _leftBrushAnchor = null;
        }

        if (!isRight && _wasPaintingRight)
        {
            if (brushShape != BrushShape.Point && _rightBrushAnchor.HasValue)
                foreach (var point in EnumerateShapePoints(_rightBrushAnchor.Value, current, brushShape))
                    _brushChanged |= RemoveEntity(point);

            CommitPendingBrushSnapshot();
            _rightBrushAnchor = null;
        }

        _wasPaintingLeft = isLeft;
        _wasPaintingRight = isRight;
    }

    public void UpdateSelection(
        MouseState mouse,
        MouseState prev,
        Vector2 worldPos,
        Point mouseScreen,
        Camera camera,
        PrototypeManager prototypes,
        AssetManager assets)
    {
        _hoveredEntity = FindEntityAtScreen(mouseScreen, camera, prototypes, assets);
        var snapToGrid = IsSnapModifierDown();
        var snappedWorldPos = snapToGrid ? SnapToTileCenter(worldPos) : worldPos;
        var leftPressed = mouse.LeftButton == ButtonState.Pressed;
        var leftClicked = leftPressed && prev.LeftButton == ButtonState.Released;
        var leftReleased = mouse.LeftButton == ButtonState.Released && prev.LeftButton == ButtonState.Pressed;

        if (leftClicked)
        {
            if (_hoveredEntity != null)
            {
                if (!_selectedEntities.Contains(_hoveredEntity))
                {
                    _selectedEntities.Clear();
                    _selectedEntities.Add(_hoveredEntity);
                }

                _isDraggingSelection = true;
                _dragAnchorWorld = snappedWorldPos;
                _dragStartPositions.Clear();
                _pendingDragSnapshot = CloneEntities(_map.Entities);
                _dragChanged = false;
                foreach (var entity in _selectedEntities)
                    _dragStartPositions[entity] = new Vector2(entity.X, entity.Y);
            }
            else
            {
                _selectedEntities.Clear();
                _isBoxSelecting = true;
                _selectionStartScreen = mouseScreen;
                _selectionCurrentScreen = mouseScreen;
            }
        }

        if (leftPressed && _isDraggingSelection)
        {
            var delta = snappedWorldPos - _dragAnchorWorld;
            foreach (var pair in _dragStartPositions)
            {
                var next = new Vector2(pair.Value.X + delta.X, pair.Value.Y + delta.Y);
                if (snapToGrid)
                    next = SnapToTileCenter(next);

                next.X = Math.Clamp(next.X, 0f, _map.Width * _map.TileSize);
                next.Y = Math.Clamp(next.Y, 0f, _map.Height * _map.TileSize);

                if (!MathF.Abs(pair.Key.X - next.X).Equals(0f) || !MathF.Abs(pair.Key.Y - next.Y).Equals(0f))
                    _dragChanged = true;

                pair.Key.X = next.X;
                pair.Key.Y = next.Y;
                pair.Key.WorldSpace = true;
            }
        }

        if (leftPressed && _isBoxSelecting)
            _selectionCurrentScreen = mouseScreen;

        if (leftReleased)
        {
            if (_isBoxSelecting)
            {
                var rect = BuildSelectionRect(_selectionStartScreen, _selectionCurrentScreen);
                ApplySelectionRect(rect, camera, prototypes, assets);
                _isBoxSelecting = false;
            }

            _isDraggingSelection = false;
            _dragStartPositions.Clear();
            CommitPendingDragSnapshot();
        }
    }

    public IReadOnlyList<Vector2> GetPreviewPositions(Vector2 worldPos, BrushShape brushShape)
    {
        var snapToGrid = IsSnapModifierDown();
        var current = NormalizeBrushPosition(worldPos, snapToGrid);

        if (brushShape != BrushShape.Point)
        {
            var mouse = Mouse.GetState();
            var anchor = mouse.LeftButton == ButtonState.Pressed ? _leftBrushAnchor
                : mouse.RightButton == ButtonState.Pressed ? _rightBrushAnchor
                : null;

            if (anchor.HasValue)
                return EnumerateShapePoints(anchor.Value, current, brushShape)
                    .Where(IsInBounds)
                    .Select(p => NormalizeBrushPosition(p, snapToGrid))
                    .Distinct()
                    .ToArray();
        }

        return IsInBounds(current) ? new[] { current } : Array.Empty<Vector2>();
    }

    public bool DeleteSelection()
    {
        if (_selectedEntities.Count == 0)
            return false;

        var before = CloneEntities(_map.Entities);
        foreach (var entity in _selectedEntities.ToArray())
            _map.Entities.Remove(entity);

        _selectedEntities.Clear();
        _hoveredEntity = null;
        CommitSnapshot(before);
        return true;
    }

    public bool TryUndo()
    {
        if (_undo.Count == 0)
            return false;

        var action = _undo.Pop();
        _redo.Push(new EntitySnapshotAction
        {
            Before = CloneEntities(_map.Entities),
            After = CloneEntities(action.Before)
        });
        RestoreEntities(action.Before);
        return true;
    }

    public bool TryRedo()
    {
        if (_redo.Count == 0)
            return false;

        var action = _redo.Pop();
        _undo.Push(new EntitySnapshotAction
        {
            Before = CloneEntities(_map.Entities),
            After = CloneEntities(action.Before)
        });
        RestoreEntities(action.Before);
        return true;
    }

    public void DrawSelectionOverlay(SpriteBatch spriteBatch, Camera camera, PrototypeManager prototypes, AssetManager assets)
    {
        foreach (var entity in _selectedEntities)
            DrawEntityOutline(spriteBatch, entity, camera, prototypes, assets, new Color(110, 220, 120));

        if (_hoveredEntity != null && !_selectedEntities.Contains(_hoveredEntity))
            DrawEntityOutline(spriteBatch, _hoveredEntity, camera, prototypes, assets, new Color(255, 220, 80));

        if (_isBoxSelecting)
        {
            var rect = BuildSelectionRect(_selectionStartScreen, _selectionCurrentScreen);
            var pixel = assets.GetColorTexture("#ffffff");
            spriteBatch.Draw(pixel, rect, new Color(90, 150, 110, 40));
            spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), new Color(120, 210, 130));
            spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), new Color(120, 210, 130));
            spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Y, 1, rect.Height), new Color(120, 210, 130));
            spriteBatch.Draw(pixel, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), new Color(120, 210, 130));
        }
    }

    private void ApplySelectionRect(Rectangle screenRect, Camera camera, PrototypeManager prototypes, AssetManager assets)
    {
        _selectedEntities.Clear();
        foreach (var entity in _map.Entities)
        {
            if (TryGetEntityScreenRect(entity, camera, prototypes, assets, out var rect) && rect.Intersects(screenRect))
                _selectedEntities.Add(entity);
        }
    }

    private MapEntityData? FindEntityAtScreen(Point screenPoint, Camera camera, PrototypeManager prototypes, AssetManager assets)
    {
        for (var i = _map.Entities.Count - 1; i >= 0; i--)
        {
            var entity = _map.Entities[i];
            if (TryGetEntityScreenRect(entity, camera, prototypes, assets, out var rect) && rect.Contains(screenPoint))
                return entity;
        }

        return null;
    }

    private bool TryGetEntityScreenRect(MapEntityData entity, Camera camera, PrototypeManager prototypes, AssetManager assets, out Rectangle rect)
    {
        var proto = prototypes.GetEntity(entity.ProtoId);
        var src = proto?.PreviewSourceRect;
        var width = (src?.Width ?? _map.TileSize) * camera.Zoom;
        var height = (src?.Height ?? _map.TileSize) * camera.Zoom;

        var worldCenter = GetEntityWorldPosition(entity);
        var screenCenter = camera.WorldToScreen(worldCenter);
        rect = new Rectangle(
            (int)MathF.Round(screenCenter.X - width / 2f),
            (int)MathF.Round(screenCenter.Y - height / 2f),
            Math.Max(1, (int)MathF.Round(width)),
            Math.Max(1, (int)MathF.Round(height)));
        return true;
    }

    private Vector2 GetEntityWorldPosition(MapEntityData entity)
    {
        if (entity.WorldSpace)
            return new Vector2(entity.X, entity.Y);

        return new Vector2(
            (entity.X + 0.5f) * _map.TileSize,
            (entity.Y + 0.5f) * _map.TileSize);
    }

    private void DrawEntityOutline(SpriteBatch spriteBatch, MapEntityData entity, Camera camera, PrototypeManager prototypes, AssetManager assets, Color outlineColor)
    {
        if (!TryGetEntityScreenRect(entity, camera, prototypes, assets, out var rect))
            return;

        var proto = prototypes.GetEntity(entity.ProtoId);
        if (proto?.SpritePath == null || proto.PreviewSourceRect == null)
        {
            DrawOutlineRect(spriteBatch, assets, rect, outlineColor);
            return;
        }

        var texture = assets.LoadFromFile(proto.SpritePath);
        if (texture == null)
        {
            DrawOutlineRect(spriteBatch, assets, rect, outlineColor);
            return;
        }

        var mask = GetSpriteMaskData(texture, proto.PreviewSourceRect.Value, proto.SpritePath);
        if (mask.EdgePixels.Length == 0)
        {
            DrawOutlineRect(spriteBatch, assets, rect, outlineColor);
            return;
        }

        var glowColor = outlineColor * 0.30f;
        var pixel = assets.GetColorTexture("#ffffff");
        var pixelSize = Math.Max(1, (int)MathF.Round(camera.Zoom));
        foreach (var edge in mask.EdgePixels)
        {
            var px = rect.X + (int)MathF.Round(edge.X * camera.Zoom);
            var py = rect.Y + (int)MathF.Round(edge.Y * camera.Zoom);
            var edgeRect = new Rectangle(px, py, pixelSize, pixelSize);
            spriteBatch.Draw(pixel, new Rectangle(px - 1, py - 1, pixelSize + 2, pixelSize + 2), glowColor);
            spriteBatch.Draw(pixel, edgeRect, outlineColor);
        }
    }

    private SpriteMaskData GetSpriteMaskData(Texture2D texture, Rectangle source, string cachePrefix)
    {
        var key = $"{cachePrefix}:{source.X}:{source.Y}:{source.Width}:{source.Height}";
        if (_spriteMaskCache.TryGetValue(key, out var cached))
            return cached;

        try
        {
            var pixels = new Color[source.Width * source.Height];
            texture.GetData(0, source, pixels, 0, pixels.Length);

            var edgePixels = new List<Point>();
            var minX = source.Width;
            var minY = source.Height;
            var maxX = -1;
            var maxY = -1;

            for (var y = 0; y < source.Height; y++)
            {
                for (var x = 0; x < source.Width; x++)
                {
                    var color = pixels[y * source.Width + x];
                    if (color.A <= 10)
                        continue;

                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);

                    if (IsEdgePixel(pixels, source.Width, source.Height, x, y))
                        edgePixels.Add(new Point(x, y));
                }
            }

            var mask = new SpriteMaskData
            {
                OpaqueBounds = maxX >= minX && maxY >= minY
                    ? new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1)
                    : new Rectangle(0, 0, source.Width, source.Height),
                EdgePixels = edgePixels.ToArray()
            };
            _spriteMaskCache[key] = mask;
            return mask;
        }
        catch
        {
            var fallback = new SpriteMaskData
            {
                OpaqueBounds = new Rectangle(0, 0, source.Width, source.Height),
                EdgePixels = Array.Empty<Point>()
            };
            _spriteMaskCache[key] = fallback;
            return fallback;
        }
    }

    private static bool IsEdgePixel(Color[] pixels, int width, int height, int x, int y)
    {
        static bool IsTransparent(Color[] data, int w, int h, int px, int py)
            => px < 0 || py < 0 || px >= w || py >= h || data[py * w + px].A <= 10;

        return IsTransparent(pixels, width, height, x - 1, y)
            || IsTransparent(pixels, width, height, x + 1, y)
            || IsTransparent(pixels, width, height, x, y - 1)
            || IsTransparent(pixels, width, height, x, y + 1);
    }

    private static Rectangle BuildSelectionRect(Point start, Point end)
    {
        var x = Math.Min(start.X, end.X);
        var y = Math.Min(start.Y, end.Y);
        var width = Math.Abs(end.X - start.X) + 1;
        var height = Math.Abs(end.Y - start.Y) + 1;
        return new Rectangle(x, y, width, height);
    }

    private bool PlaceEntity(Vector2 worldPos, string selectedEntityId)
    {
        if (!IsInBounds(worldPos))
            return false;

        _map.Entities.RemoveAll(entity => Vector2.Distance(new Vector2(entity.X, entity.Y), worldPos) < 8f);
        _map.Entities.Add(new MapEntityData
        {
            X = worldPos.X,
            Y = worldPos.Y,
            ProtoId = selectedEntityId,
            WorldSpace = true
        });
        return true;
    }

    private bool RemoveEntity(Vector2 worldPos)
    {
        if (!IsInBounds(worldPos))
            return false;

        var removed = _map.Entities.RemoveAll(entity => Vector2.Distance(new Vector2(entity.X, entity.Y), worldPos) < 8f);
        _selectedEntities.RemoveAll(entity => Vector2.Distance(new Vector2(entity.X, entity.Y), worldPos) < 8f);
        return removed > 0;
    }

    private IEnumerable<Vector2> EnumerateShapePoints(Vector2 start, Vector2 end, BrushShape brushShape)
    {
        return brushShape switch
        {
            BrushShape.Line => EnumerateLine(start, end),
            BrushShape.FilledRectangle => EnumerateFilledRectangle(start, end),
            BrushShape.HollowRectangle => EnumerateHollowRectangle(start, end),
            _ => new[] { end }
        };
    }

    private IEnumerable<Vector2> EnumerateLine(Vector2 start, Vector2 end)
    {
        var distance = Vector2.Distance(start, end);
        var steps = Math.Max(1, (int)(distance / _map.TileSize));
        for (var i = 0; i <= steps; i++)
        {
            var t = steps == 0 ? 0f : i / (float)steps;
            yield return Vector2.Lerp(start, end, t);
        }
    }

    private IEnumerable<Vector2> EnumerateFilledRectangle(Vector2 start, Vector2 end)
    {
        var minX = Math.Min(start.X, end.X);
        var maxX = Math.Max(start.X, end.X);
        var minY = Math.Min(start.Y, end.Y);
        var maxY = Math.Max(start.Y, end.Y);

        for (var y = minY; y <= maxY; y += _map.TileSize)
            for (var x = minX; x <= maxX; x += _map.TileSize)
                yield return new Vector2(x, y);
    }

    private IEnumerable<Vector2> EnumerateHollowRectangle(Vector2 start, Vector2 end)
    {
        var minX = Math.Min(start.X, end.X);
        var maxX = Math.Max(start.X, end.X);
        var minY = Math.Min(start.Y, end.Y);
        var maxY = Math.Max(start.Y, end.Y);

        for (var x = minX; x <= maxX; x += _map.TileSize)
        {
            yield return new Vector2(x, minY);
            if (maxY != minY)
                yield return new Vector2(x, maxY);
        }

        for (var y = minY + _map.TileSize; y < maxY; y += _map.TileSize)
        {
            yield return new Vector2(minX, y);
            if (maxX != minX)
                yield return new Vector2(maxX, y);
        }
    }

    private static void DrawOutlineRect(SpriteBatch spriteBatch, AssetManager assets, Rectangle rect, Color outlineColor)
    {
        var glowColor = outlineColor * 0.30f;
        var pixel = assets.GetColorTexture("#ffffff");
        spriteBatch.Draw(pixel, new Rectangle(rect.X - 1, rect.Y - 1, rect.Width + 2, 1), glowColor);
        spriteBatch.Draw(pixel, new Rectangle(rect.X - 1, rect.Bottom, rect.Width + 2, 1), glowColor);
        spriteBatch.Draw(pixel, new Rectangle(rect.X - 1, rect.Y, 1, rect.Height), glowColor);
        spriteBatch.Draw(pixel, new Rectangle(rect.Right, rect.Y, 1, rect.Height), glowColor);

        spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), outlineColor);
        spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), outlineColor);
        spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Y, 1, rect.Height), outlineColor);
        spriteBatch.Draw(pixel, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), outlineColor);
    }

    private bool IsInBounds(Vector2 worldPos)
        => worldPos.X >= 0 && worldPos.X < _map.Width * _map.TileSize && worldPos.Y >= 0 && worldPos.Y < _map.Height * _map.TileSize;

    private static bool IsSnapModifierDown()
    {
        var keyboard = Keyboard.GetState();
        return keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
    }

    private Vector2 NormalizeBrushPosition(Vector2 worldPos, bool snapToGrid)
        => snapToGrid ? SnapToTileCenter(worldPos) : worldPos;

    private Vector2 SnapToTileCenter(Vector2 worldPos)
    {
        var tileX = Math.Clamp((int)MathF.Floor(worldPos.X / _map.TileSize), 0, _map.Width - 1);
        var tileY = Math.Clamp((int)MathF.Floor(worldPos.Y / _map.TileSize), 0, _map.Height - 1);
        return new Vector2((tileX + 0.5f) * _map.TileSize, (tileY + 0.5f) * _map.TileSize);
    }

    private void CommitPendingBrushSnapshot()
    {
        if (_pendingBrushSnapshot == null)
            return;

        if (_brushChanged)
            CommitSnapshot(_pendingBrushSnapshot);

        _pendingBrushSnapshot = null;
        _brushChanged = false;
    }

    private void CommitPendingDragSnapshot()
    {
        if (_pendingDragSnapshot == null)
            return;

        if (_dragChanged)
            CommitSnapshot(_pendingDragSnapshot);

        _pendingDragSnapshot = null;
        _dragChanged = false;
    }

    private void CommitSnapshot(List<MapEntityData> before)
    {
        var after = CloneEntities(_map.Entities);
        if (SnapshotsEqual(before, after))
            return;

        _undo.Push(new EntitySnapshotAction
        {
            Before = before,
            After = after
        });
        _redo.Clear();
    }

    private void RestoreEntities(List<MapEntityData> snapshot)
    {
        _map.Entities.Clear();
        foreach (var entity in snapshot)
            _map.Entities.Add(CloneEntity(entity));

        _selectedEntities.Clear();
        _dragStartPositions.Clear();
        _hoveredEntity = null;
        _isDraggingSelection = false;
        _isBoxSelecting = false;
    }

    private static List<MapEntityData> CloneEntities(IEnumerable<MapEntityData> entities)
        => entities.Select(CloneEntity).ToList();

    private static MapEntityData CloneEntity(MapEntityData entity)
        => new()
        {
            X = entity.X,
            Y = entity.Y,
            ProtoId = entity.ProtoId,
            WorldSpace = entity.WorldSpace
        };

    private static bool SnapshotsEqual(IReadOnlyList<MapEntityData> a, IReadOnlyList<MapEntityData> b)
    {
        if (a.Count != b.Count)
            return false;

        for (var i = 0; i < a.Count; i++)
        {
            if (a[i].ProtoId != b[i].ProtoId
                || a[i].WorldSpace != b[i].WorldSpace
                || MathF.Abs(a[i].X - b[i].X) > 0.01f
                || MathF.Abs(a[i].Y - b[i].Y) > 0.01f)
                return false;
        }

        return true;
    }
}
