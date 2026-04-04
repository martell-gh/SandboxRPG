using System.Text.Json.Serialization;

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

    [JsonPropertyName("spawnPoints")]
    public List<SpawnPoint> SpawnPoints { get; set; } = new();

    [JsonPropertyName("tiles")]
    public List<TileData> Tiles { get; set; } = new();

    [JsonPropertyName("entities")]
    public List<MapEntityData> Entities { get; set; } = new();

    public (bool valid, string error) Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
            return (false, "Map id is empty");
        if (string.IsNullOrWhiteSpace(Name))
            return (false, "Map name is empty");
        if (SpawnPoints.Count == 0)
            return (false, "No spawn points! Add at least one.");
        if (SpawnPoints.Any(s => string.IsNullOrWhiteSpace(s.Id)))
            return (false, "Spawn point has empty id");
        if (Tiles.Any(t => string.IsNullOrWhiteSpace(t.ProtoId)))
            return (false, "Tile has empty proto id");
        if (Entities.Any(e => string.IsNullOrWhiteSpace(e.ProtoId)))
            return (false, "Entity has empty proto id");
        return (true, "");
    }
}
