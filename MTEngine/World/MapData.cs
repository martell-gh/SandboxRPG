using System;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace MTEngine.World;

public class SpawnPoint
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }
}

public class TileData
{
    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("protoId")]
    public string ProtoId { get; set; } = "";

    [JsonPropertyName("layer")]
    public int Layer { get; set; } = 0;
}

public class MapEntityData
{
    [JsonPropertyName("x")]
    public float X { get; set; }

    [JsonPropertyName("y")]
    public float Y { get; set; }

    [JsonPropertyName("protoId")]
    public string ProtoId { get; set; } = "";

    [JsonPropertyName("worldSpace")]
    public bool WorldSpace { get; set; }

    [JsonPropertyName("componentOverrides")]
    public Dictionary<string, JsonObject> ComponentOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("containedEntities")]
    public List<MapEntityData> ContainedEntities { get; set; } = new();
}

public class MapData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("width")]
    public int Width { get; set; } = 50;

    [JsonPropertyName("height")]
    public int Height { get; set; } = 50;

    [JsonPropertyName("tileSize")]
    public int TileSize { get; set; } = 32;

    [JsonPropertyName("ingame")]
    public bool InGame { get; set; }

    [JsonPropertyName("locationKind")]
    public string LocationKind { get; set; } = LocationKinds.Wilds;

    [JsonPropertyName("factionId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FactionId { get; set; }

    [JsonPropertyName("cityId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CityId { get; set; }

    [JsonPropertyName("wantedTags")]
    public List<string> WantedTags { get; set; } = new();

    [JsonPropertyName("unwantedTags")]
    public List<string> UnwantedTags { get; set; } = new();

    [JsonPropertyName("spawnPoints")]
    public List<SpawnPoint> SpawnPoints { get; set; } = new();

    [JsonPropertyName("tiles")]
    public List<TileData> Tiles { get; set; } = new();

    [JsonPropertyName("entities")]
    public List<MapEntityData> Entities { get; set; } = new();

    [JsonPropertyName("triggers")]
    public List<TriggerZoneData> Triggers { get; set; } = new();

    [JsonPropertyName("areas")]
    public List<AreaZoneData> Areas { get; set; } = new();

    public (bool valid, string error) Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
            return (false, "Map id is empty");
        if (string.IsNullOrWhiteSpace(Name))
            return (false, "Map name is empty");
        if (string.IsNullOrWhiteSpace(LocationKind))
            return (false, "Map location kind is empty");
        if (SpawnPoints.Count == 0)
            return (false, "No spawn points! Add at least one.");
        if (SpawnPoints.Any(s => string.IsNullOrWhiteSpace(s.Id)))
            return (false, "Spawn point has empty id");
        if (Tiles.Any(t => string.IsNullOrWhiteSpace(t.ProtoId)))
            return (false, "Tile has empty proto id");
        if (Entities.Any(e => string.IsNullOrWhiteSpace(e.ProtoId)))
            return (false, "Entity has empty proto id");
        if (Triggers.Any(t => string.IsNullOrWhiteSpace(t.Id)))
            return (false, "Trigger has empty id");
        if (Triggers.Any(t => t.Tiles.Count == 0))
            return (false, "Trigger has no tiles");
        if (Areas.Any(a => string.IsNullOrWhiteSpace(a.Id)))
            return (false, "Area has empty id");
        if (Areas.Any(a => string.IsNullOrWhiteSpace(a.Kind)))
            return (false, "Area has empty kind");
        if (Areas.Any(a => a.Tiles.Count == 0))
            return (false, "Area has no tiles");
        if (Areas.SelectMany(a => a.Points).Any(p => string.IsNullOrWhiteSpace(p.Id)))
            return (false, "Area point has empty id");
        return (true, "");
    }
}
