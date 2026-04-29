using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using MTEngine.World;

namespace MTEditor;

public sealed class EditorMapSnapshot
{
    public required MapData Map { get; init; }
    public required TileMap TileMap { get; init; }

    public static EditorMapSnapshot Capture(MapData map, TileMap tileMap)
    {
        return new EditorMapSnapshot
        {
            Map = CloneMap(map, tileMap),
            TileMap = CloneTileMap(tileMap)
        };
    }

    public static MapData CloneMap(MapData map, TileMap tileMap)
    {
        return new MapData
        {
            Id = map.Id,
            Name = map.Name,
            Width = map.Width,
            Height = map.Height,
            TileSize = map.TileSize,
            InGame = map.InGame,
            LocationKind = map.LocationKind,
            FactionId = map.FactionId,
            CityId = map.CityId,
            SpawnPoints = map.SpawnPoints.Select(spawn => new SpawnPoint
            {
                Id = spawn.Id,
                X = spawn.X,
                Y = spawn.Y
            }).ToList(),
            Tiles = BuildTileData(tileMap),
            Entities = map.Entities.Select(CloneEntity).ToList(),
            Triggers = map.Triggers.Select(CloneTrigger).ToList(),
            Areas = map.Areas.Select(CloneArea).ToList()
        };
    }

    public static TileMap CloneTileMap(TileMap tileMap)
    {
        var clone = new TileMap(tileMap.Width, tileMap.Height, tileMap.TileSize, tileMap.LayerCount);
        for (var layer = 0; layer < tileMap.LayerCount; layer++)
        {
            for (var x = 0; x < tileMap.Width; x++)
            {
                for (var y = 0; y < tileMap.Height; y++)
                    clone.SetTile(x, y, tileMap.GetTile(x, y, layer).Clone(), layer);
            }
        }

        return clone;
    }

    private static List<TileData> BuildTileData(TileMap tileMap)
    {
        var result = new List<TileData>();
        for (var layer = 0; layer < tileMap.LayerCount; layer++)
        {
            for (var x = 0; x < tileMap.Width; x++)
            {
                for (var y = 0; y < tileMap.Height; y++)
                {
                    var tile = tileMap.GetTile(x, y, layer);
                    if (tile.Type == TileType.Empty || string.IsNullOrWhiteSpace(tile.ProtoId))
                        continue;

                    result.Add(new TileData
                    {
                        X = x,
                        Y = y,
                        Layer = layer,
                        ProtoId = tile.ProtoId
                    });
                }
            }
        }

        return result;
    }

    private static MapEntityData CloneEntity(MapEntityData entity)
    {
        return new MapEntityData
        {
            X = entity.X,
            Y = entity.Y,
            ProtoId = entity.ProtoId,
            WorldSpace = entity.WorldSpace,
            ComponentOverrides = entity.ComponentOverrides.ToDictionary(
                pair => pair.Key,
                pair => CloneJsonObject(pair.Value),
                StringComparer.OrdinalIgnoreCase),
            ContainedEntities = entity.ContainedEntities.Select(CloneEntity).ToList()
        };
    }

    private static TriggerZoneData CloneTrigger(TriggerZoneData trigger)
    {
        return new TriggerZoneData
        {
            Id = trigger.Id,
            Action = new TriggerActionData
            {
                Type = trigger.Action.Type,
                TargetMapId = trigger.Action.TargetMapId,
                SpawnPointId = trigger.Action.SpawnPointId
            },
            Tiles = trigger.Tiles.Select(tile => new TriggerTile
            {
                X = tile.X,
                Y = tile.Y
            }).ToList()
        };
    }

    private static AreaZoneData CloneArea(AreaZoneData area)
    {
        return new AreaZoneData
        {
            Id = area.Id,
            Kind = area.Kind,
            Properties = area.Properties.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase),
            Tiles = area.Tiles.Select(tile => new TriggerTile
            {
                X = tile.X,
                Y = tile.Y
            }).ToList(),
            Points = area.Points.Select(point => new AreaPointData
            {
                Id = point.Id,
                X = point.X,
                Y = point.Y
            }).ToList()
        };
    }

    private static JsonObject CloneJsonObject(JsonObject source)
        => JsonNode.Parse(source.ToJsonString())?.AsObject() ?? new JsonObject();
}

public sealed class ResizeMapHistory
{
    private sealed class ResizeMapAction
    {
        public required EditorMapSnapshot Before { get; init; }
        public required EditorMapSnapshot After { get; init; }
        public required long Order { get; init; }
    }

    private readonly EditorActionTracker _tracker;
    private readonly Stack<ResizeMapAction> _undo = new();
    private readonly Stack<ResizeMapAction> _redo = new();
    private long _redoGeneration = -1;

    public ResizeMapHistory(EditorActionTracker tracker)
    {
        _tracker = tracker;
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

    public void Record(EditorMapSnapshot before, EditorMapSnapshot after)
    {
        _undo.Push(new ResizeMapAction
        {
            Before = before,
            After = after,
            Order = _tracker.CommitNewOperation()
        });
        _redo.Clear();
        _redoGeneration = -1;
    }

    public bool TryUndo(out EditorMapSnapshot? snapshot)
    {
        snapshot = null;
        if (_undo.Count == 0)
            return false;

        var action = _undo.Pop();
        _redo.Push(action);
        _redoGeneration = _tracker.CurrentRedoGeneration;
        snapshot = action.Before;
        return true;
    }

    public bool TryRedo(out EditorMapSnapshot? snapshot)
    {
        snapshot = null;
        InvalidateRedoIfNeeded();
        if (_redo.Count == 0)
            return false;

        var action = _redo.Pop();
        _undo.Push(action);
        snapshot = action.After;
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
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
