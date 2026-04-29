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
    private const string WorldDataFileName = "world_data.json";

    private readonly string _mapsDirectory;
    private readonly string _worldDataPath;
    private readonly PrototypeManager _prototypes;
    private MapData? _currentMap;
    private TileMap? _currentTileMap;
    private WorldData _worldData = new();

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
        _worldDataPath = Path.Combine(mapsDirectory, WorldDataFileName);
        _prototypes = prototypes;

        if (!Directory.Exists(mapsDirectory))
            Directory.CreateDirectory(mapsDirectory);

        ReloadWorldData();
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

            NormalizeMapMetadata(mapData);

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
        NormalizeMapMetadata(mapData);
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

                NormalizeMapMetadata(map);

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
            var map = JsonSerializer.Deserialize<MapData>(json, _jsonOptions);
            if (map != null)
                NormalizeMapMetadata(map);
            return map;
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

    public WorldData GetWorldData()
    {
        return CloneWorldData(_worldData);
    }

    public void ReloadWorldData()
    {
        if (!File.Exists(_worldDataPath))
        {
            _worldData = new WorldData();
            return;
        }

        try
        {
            var json = File.ReadAllText(_worldDataPath);
            _worldData = JsonSerializer.Deserialize<WorldData>(json, _jsonOptions) ?? new WorldData();
            _worldData.Normalize();
        }
        catch (Exception e)
        {
            Console.WriteLine($"[MapManager] Error loading world data: {e.Message}");
            _worldData = new WorldData();
        }
    }

    public bool SaveWorldData(WorldData worldData)
    {
        try
        {
            var clone = CloneWorldData(worldData);
            clone.Normalize();
            var json = JsonSerializer.Serialize(clone, _jsonOptions);
            File.WriteAllText(_worldDataPath, json);
            _worldData = clone;
            Console.WriteLine($"[MapManager] Saved world data: {_worldDataPath}");
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[MapManager] Error saving world data: {e.Message}");
            return false;
        }
    }

    public FactionData? GetFaction(string? factionId)
    {
        var faction = _worldData.GetFaction(factionId);
        return faction == null ? null : CloneFaction(faction);
    }

    public CityData? GetCity(string? cityId)
    {
        var city = _worldData.GetCity(cityId);
        return city == null ? null : new CityData
        {
            Id = city.Id,
            Name = city.Name
        };
    }

    public string? GetCurrentLocationId()
        => _currentMap?.Id;

    public string? GetCurrentFactionId()
        => _currentMap?.FactionId;

    public string? GetCurrentCityId()
        => _currentMap?.CityId;

    public LocationContext? GetCurrentLocationContext()
        => _currentMap == null ? null : BuildLocationContext(_currentMap);

    public LocationContext? GetLocationContext(string mapId)
    {
        if (_currentMap != null && string.Equals(_currentMap.Id, mapId, StringComparison.OrdinalIgnoreCase))
            return BuildLocationContext(_currentMap);

        var map = LoadMapDataForLookup(mapId);
        return map == null ? null : BuildLocationContext(map);
    }

    public int ReplaceFactionReferences(string oldFactionId, string? newFactionId)
        => RewriteMapMetadata(map =>
        {
            if (!string.Equals(map.FactionId, oldFactionId, StringComparison.OrdinalIgnoreCase))
                return false;

            map.FactionId = string.IsNullOrWhiteSpace(newFactionId) ? null : newFactionId;
            return true;
        });

    public int ReplaceCityReferences(string oldCityId, string? newCityId)
        => RewriteMapMetadata(map =>
        {
            if (!string.Equals(map.CityId, oldCityId, StringComparison.OrdinalIgnoreCase))
                return false;

            map.CityId = string.IsNullOrWhiteSpace(newCityId) ? null : newCityId;
            return true;
        });

    public int ReplaceProfessionReferences(string oldProfessionId, string? newProfessionId)
        => RewriteMapMetadata(map =>
        {
            var changed = false;
            foreach (var area in map.Areas)
            {
                if (!area.Properties.TryGetValue("professionId", out var current)
                    || !string.Equals(current, oldProfessionId, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (string.IsNullOrWhiteSpace(newProfessionId))
                    area.Properties.Remove("professionId");
                else
                    area.Properties["professionId"] = newProfessionId;
                changed = true;
            }

            return changed;
        });

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

    private int RewriteMapMetadata(Func<MapData, bool> mutate)
    {
        var updated = 0;
        foreach (var path in Directory.GetFiles(_mapsDirectory, "*.map.json"))
        {
            try
            {
                var json = File.ReadAllText(path);
                var map = JsonSerializer.Deserialize<MapData>(json, _jsonOptions);
                if (map == null)
                    continue;

                NormalizeMapMetadata(map);
                if (!mutate(map))
                    continue;

                var updatedJson = JsonSerializer.Serialize(map, _jsonOptions);
                File.WriteAllText(path, updatedJson);
                updated++;
            }
            catch (Exception e)
            {
                Console.WriteLine($"[MapManager] Error updating map metadata in {path}: {e.Message}");
            }
        }

        return updated;
    }

    private MapData? LoadMapDataForLookup(string mapId)
    {
        if (ServiceLocator.Has<IMapStateSource>())
        {
            var overrideMap = ServiceLocator.Get<IMapStateSource>().GetMapOverride(mapId);
            if (overrideMap != null)
            {
                NormalizeMapMetadata(overrideMap);
                return overrideMap;
            }
        }

        return LoadBaseMapData(mapId);
    }

    private LocationContext BuildLocationContext(MapData map)
    {
        NormalizeMapMetadata(map);

        var faction = _worldData.GetFaction(map.FactionId);
        var city = _worldData.GetCity(map.CityId);
        return new LocationContext
        {
            LocationId = map.Id,
            LocationName = string.IsNullOrWhiteSpace(map.Name) ? map.Id : map.Name,
            LocationKind = map.LocationKind,
            FactionId = map.FactionId,
            FactionName = LocalizationManager.T(faction?.Name),
            CityId = map.CityId,
            CityName = city?.Name
        };
    }

    private static void NormalizeMapMetadata(MapData map)
    {
        map.LocationKind = LocationKinds.Normalize(map.LocationKind);
        map.FactionId = NormalizeReferenceId(map.FactionId);
        map.CityId = NormalizeReferenceId(map.CityId);
        map.WantedTags = NormalizeTagList(map.WantedTags);
        map.UnwantedTags = NormalizeTagList(map.UnwantedTags)
            .Where(tag => !map.WantedTags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    private static string? NormalizeReferenceId(string? id)
        => string.IsNullOrWhiteSpace(id) ? null : id.Trim();

    private static List<string> NormalizeTagList(IEnumerable<string>? tags)
        => (tags ?? Array.Empty<string>())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private WorldData CloneWorldData(WorldData data)
        => JsonSerializer.Deserialize<WorldData>(JsonSerializer.Serialize(data, _jsonOptions), _jsonOptions) ?? new WorldData();

    private FactionData CloneFaction(FactionData faction)
        => JsonSerializer.Deserialize<FactionData>(JsonSerializer.Serialize(faction, _jsonOptions), _jsonOptions) ?? new FactionData();
}
