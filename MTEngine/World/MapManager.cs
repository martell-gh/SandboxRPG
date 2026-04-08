using System.Text.Json;
using Microsoft.Xna.Framework;
using MTEngine.Core;

namespace MTEngine.World;

public class MapLoadedEvent
{
    public MapData Map { get; set; } = null!;
    public string SpawnPointId { get; set; } = "default";
}

public class MapCatalogEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool InGame { get; set; }
}

public class MapManager
{
    private readonly string _mapsDirectory;
    private readonly PrototypeManager _prototypes;
    private MapData? _currentMap;
    private TileMap? _currentTileMap;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public MapData? CurrentMap => _currentMap;
    public TileMap? CurrentTileMap => _currentTileMap;

    // место под будущую систему сохранений
    // public SaveSystem? SaveSystem { get; set; }

    public MapManager(string mapsDirectory, PrototypeManager prototypes)
    {
        _mapsDirectory = mapsDirectory;
        _prototypes = prototypes;

        if (!Directory.Exists(mapsDirectory))
            Directory.CreateDirectory(mapsDirectory);
    }

    // загрузка карты по id файла
    public (TileMap? tileMap, SpawnPoint? spawn) LoadMap(string mapId, string spawnId = "default")
    {
        var path = Path.Combine(_mapsDirectory, $"{mapId}.map.json");

        if (!File.Exists(path) && (!ServiceLocator.Has<IMapStateSource>() || ServiceLocator.Get<IMapStateSource>().GetMapOverride(mapId) == null))
        {
            Console.WriteLine($"[MapManager] Map not found: {path}");
            return (null, null);
        }

        try
        {
            MapData? mapData = null;
            if (ServiceLocator.Has<IMapStateSource>())
                mapData = ServiceLocator.Get<IMapStateSource>().GetMapOverride(mapId);

            if (mapData == null)
            {
                var json = File.ReadAllText(path);
                mapData = JsonSerializer.Deserialize<MapData>(json, _jsonOptions);
            }

            if (mapData == null)
            {
                Console.WriteLine($"[MapManager] Failed to parse: {path}");
                return (null, null);
            }

            _currentMap = mapData;
            _currentTileMap = BuildTileMap(mapData);

            var spawn = mapData.SpawnPoints.FirstOrDefault(s => s.Id == spawnId)
                     ?? mapData.SpawnPoints.FirstOrDefault();

            Console.WriteLine($"[MapManager] Loaded map: {mapData.Name}, spawn: {spawn?.Id}");

            // публикуем событие
            if (ServiceLocator.Has<EventBus>())
                ServiceLocator.Get<EventBus>().Publish(new MapLoadedEvent
                {
                    Map = mapData,
                    SpawnPointId = spawn?.Id ?? "default"
                });

            return (_currentTileMap, spawn);
        }
        catch (Exception e)
        {
            Console.WriteLine($"[MapManager] Error loading map: {e.Message}");
            return (null, null);
        }
    }

    // сохранение карты
    public bool SaveMap(MapData mapData)
    {
        var (valid, error) = mapData.Validate();
        if (!valid)
        {
            Console.WriteLine($"[MapManager] Cannot save: {error}");
            return false;
        }

        var path = Path.Combine(_mapsDirectory, $"{mapData.Id}.map.json");

        try
        {
            var json = JsonSerializer.Serialize(mapData, _jsonOptions);
            File.WriteAllText(path, json);
            Console.WriteLine($"[MapManager] Saved map: {path}");
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[MapManager] Error saving: {e.Message}");
            return false;
        }
    }

    // переход между локациями — потом используем для повозок
    public (TileMap? tileMap, SpawnPoint? spawn) TransitionTo(string mapId, string spawnId = "default")
    {
        Console.WriteLine($"[MapManager] Transitioning to {mapId} @ {spawnId}");

        // место под будущую систему сохранений:
        // SaveSystem?.SaveCurrentState(_currentMap?.Id);

        return LoadMap(mapId, spawnId);
    }

    // список доступных карт
    public List<string> GetAvailableMaps()
    {
        return Directory.GetFiles(_mapsDirectory, "*.map.json")
            .Select(f => Path.GetFileNameWithoutExtension(f).Replace(".map", ""))
            .ToList();
    }

    public List<MapCatalogEntry> GetMapCatalog()
    {
        var result = new List<MapCatalogEntry>();

        foreach (var path in Directory.GetFiles(_mapsDirectory, "*.map.json"))
        {
            try
            {
                var json = File.ReadAllText(path);
                var map = JsonSerializer.Deserialize<MapData>(json, _jsonOptions);
                if (map == null)
                    continue;

                result.Add(new MapCatalogEntry
                {
                    Id = map.Id,
                    Name = string.IsNullOrWhiteSpace(map.Name) ? map.Id : map.Name,
                    InGame = map.InGame
                });
            }
            catch (Exception e)
            {
                Console.WriteLine($"[MapManager] Error reading map catalog entry from {path}: {e.Message}");
            }
        }

        return result
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void ClearCurrentMap()
    {
        _currentMap = null;
        _currentTileMap = null;
    }

    public MapData? LoadBaseMapData(string mapId)
    {
        var path = Path.Combine(_mapsDirectory, $"{mapId}.map.json");
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<MapData>(json, _jsonOptions);
        }
        catch (Exception e)
        {
            Console.WriteLine($"[MapManager] Error reading base map data {mapId}: {e.Message}");
            return null;
        }
    }

    public bool SetMapInGameFlag(string mapId, bool inGame)
    {
        var map = LoadBaseMapData(mapId);
        if (map == null)
            return false;

        map.InGame = inGame;
        return SaveMap(map);
    }

    private TileMap BuildTileMap(MapData data)
    {
        var layerCount = Math.Max(3, data.Tiles.Count == 0 ? 1 : data.Tiles.Max(tile => tile.Layer) + 1);
        var map = new TileMap(data.Width, data.Height, data.TileSize, layerCount);

        foreach (var tileData in data.Tiles)
        {
            var proto = _prototypes.GetTile(tileData.ProtoId);
            if (proto == null)
            {
                Console.WriteLine($"[MapManager] Unknown tile proto: {tileData.ProtoId}");
                continue;
            }

            map.SetTile(tileData.X, tileData.Y, new Tile
            {
                ProtoId = tileData.ProtoId,
                Solid = proto.Solid,
                Transparent = proto.Transparent,
                Opaque = proto.Opaque,
                Type = proto.Solid ? TileType.Wall : TileType.Floor
            }, tileData.Layer);
        }

        return map;
    }
}
