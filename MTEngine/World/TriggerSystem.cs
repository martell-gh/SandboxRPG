using Microsoft.Xna.Framework;

namespace MTEngine.World;

/// <summary>
/// Рантайм-система проверки триггеров.
/// Может использоваться игроком и в будущем НПС.
/// </summary>
public class TriggerSystem
{
    private readonly MapManager _mapManager;
    private MapData? _currentMap;

    public TriggerSystem(MapManager mapManager)
    {
        _mapManager = mapManager;
    }

    public void SetMap(MapData map)
    {
        _currentMap = map;
    }

    /// <summary>
    /// Проверяет, находится ли позиция (в мировых координатах) внутри триггер-зоны.
    /// Возвращает первый найденный триггер или null.
    /// </summary>
    public TriggerZoneData? CheckTriggerAt(Vector2 worldPosition)
    {
        if (_currentMap == null) return null;

        var tileX = (int)(worldPosition.X / _currentMap.TileSize);
        var tileY = (int)(worldPosition.Y / _currentMap.TileSize);

        return CheckTriggerAtTile(tileX, tileY);
    }

    public TriggerZoneData? CheckTriggerAtBounds(Rectangle worldBounds)
    {
        if (_currentMap == null) return null;

        var startX = worldBounds.Left / _currentMap.TileSize;
        var startY = worldBounds.Top / _currentMap.TileSize;
        var endX = Math.Max(startX, (worldBounds.Right - 1) / _currentMap.TileSize);
        var endY = Math.Max(startY, (worldBounds.Bottom - 1) / _currentMap.TileSize);

        for (var y = startY; y <= endY; y++)
        {
            for (var x = startX; x <= endX; x++)
            {
                var trigger = CheckTriggerAtTile(x, y);
                if (trigger != null)
                    return trigger;
            }
        }

        return null;
    }

    /// <summary>
    /// Проверяет триггер по тайловым координатам.
    /// </summary>
    public TriggerZoneData? CheckTriggerAtTile(int tileX, int tileY)
    {
        if (_currentMap == null) return null;

        foreach (var trigger in _currentMap.Triggers)
        {
            if (trigger.ContainsTile(tileX, tileY))
                return trigger;
        }

        return null;
    }

    /// <summary>
    /// Выполняет действие триггера. Возвращает true, если действие было обработано.
    /// Вызывается при входе игрока/НПС в зону триггера.
    /// </summary>
    public bool ExecuteTrigger(TriggerZoneData trigger)
    {
        switch (trigger.Action.Type)
        {
            case TriggerActionTypes.LocationTransition:
                return ExecuteLocationTransition(trigger.Action);

            default:
                Console.WriteLine($"[TriggerSystem] Unknown trigger type: {trigger.Action.Type}");
                return false;
        }
    }

    private bool ExecuteLocationTransition(TriggerActionData action)
    {
        if (string.IsNullOrWhiteSpace(action.TargetMapId))
        {
            Console.WriteLine("[TriggerSystem] Location transition has no target map");
            return false;
        }

        var spawnId = action.SpawnPointId ?? "default";
        Console.WriteLine($"[TriggerSystem] Transitioning to {action.TargetMapId} @ {spawnId}");

        var (tileMap, spawn) = _mapManager.TransitionTo(action.TargetMapId, spawnId);
        if (tileMap == null)
        {
            Console.WriteLine($"[TriggerSystem] Failed to load map: {action.TargetMapId}");
            return false;
        }

        _currentMap = _mapManager.CurrentMap;
        return true;
    }
}
