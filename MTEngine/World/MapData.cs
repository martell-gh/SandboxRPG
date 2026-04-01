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
        return (true, "");
    }
}