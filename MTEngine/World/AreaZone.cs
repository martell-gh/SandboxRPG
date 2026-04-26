using System.Text.Json.Serialization;

namespace MTEngine.World;

/// <summary>
/// Семантические типы area-zone. Расширяется по мере роста симуляции.
/// </summary>
public static class AreaZoneKinds
{
    public const string House       = "house";        // жилой дом, нужны bed_slot_a/b и опц. child_bed_*
    public const string Profession  = "profession";   // площадка профессии, нужен work_anchor + Properties.professionId
    public const string School      = "school";       // школа, точки wander_*
    public const string Inn         = "inn";          // отель — кровати inn_bed_* для безработных
    public const string Tavern      = "tavern";       // трактир, точки wander_*
    public const string Orphanage   = "orphanage";    // приют, кровати orphan_bed_*
    public const string Wander      = "wander";       // зона прогулок, точки wander_*
    public const string District    = "district";     // граница района (для приписки к району)
    public const string Settlement  = "settlement";   // граница поселения
}

/// <summary>
/// Именованная точка внутри area. Используется для bed_slot_a/b, child_bed_1, work_anchor, wander_1...
/// Координаты тайловые.
/// </summary>
public class AreaPointData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }
}

/// <summary>
/// Семантическая зона на карте: дом, профессия, школа и т.д.
/// В отличие от <see cref="TriggerZoneData"/> — ничего не "делает" сама по себе,
/// её используют системы AI/мира как разметку.
/// </summary>
public class AreaZoneData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>Один из <see cref="AreaZoneKinds"/>.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    /// <summary>
    /// Произвольные свойства: для house — settlement/district, для profession — professionId.
    /// </summary>
    [JsonPropertyName("properties")]
    public Dictionary<string, string> Properties { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Тайлы, входящие в зону.</summary>
    [JsonPropertyName("tiles")]
    public List<TriggerTile> Tiles { get; set; } = new();

    /// <summary>Именованные точки (bed_slot_a, work_anchor и т.д.).</summary>
    [JsonPropertyName("points")]
    public List<AreaPointData> Points { get; set; } = new();

    public bool ContainsTile(int x, int y)
        => Tiles.Any(t => t.X == x && t.Y == y);

    public void AddTile(int x, int y)
    {
        if (!ContainsTile(x, y))
            Tiles.Add(new TriggerTile { X = x, Y = y });
    }

    public void RemoveTile(int x, int y)
        => Tiles.RemoveAll(t => t.X == x && t.Y == y);

    public AreaPointData? GetPoint(string id)
        => Points.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<AreaPointData> GetPointsByPrefix(string prefix)
        => Points.Where(p => p.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
