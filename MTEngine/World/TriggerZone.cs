using System.Text.Json.Serialization;

namespace MTEngine.World;

/// <summary>
/// Тип действия триггера. Расширяется по мере добавления новых механик.
/// </summary>
public static class TriggerActionTypes
{
    public const string LocationTransition = "location_transition";
    // будущие типы: "script", "dialog", "spawn_enemy", "quest_update", etc.
}

/// <summary>
/// Данные действия триггера. Расширяемая модель —
/// новые типы триггеров добавляют свои nullable-поля.
/// </summary>
public class TriggerActionData
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = TriggerActionTypes.LocationTransition;

    // === Location Transition ===
    [JsonPropertyName("targetMapId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetMapId { get; set; }

    [JsonPropertyName("spawnPointId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SpawnPointId { get; set; } = "default";
}

/// <summary>
/// Одна ячейка триггер-зоны.
/// </summary>
public class TriggerTile
{
    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }
}

/// <summary>
/// Триггер-зона: набор тайлов + действие.
/// Зона может быть произвольной формы (не обязательно прямоугольник).
/// Используется игроком и в будущем НПС для активации событий.
/// </summary>
public class TriggerZoneData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("tiles")]
    public List<TriggerTile> Tiles { get; set; } = new();

    [JsonPropertyName("action")]
    public TriggerActionData Action { get; set; } = new();

    public bool ContainsTile(int x, int y)
        => Tiles.Any(t => t.X == x && t.Y == y);

    public void AddTile(int x, int y)
    {
        if (!ContainsTile(x, y))
            Tiles.Add(new TriggerTile { X = x, Y = y });
    }

    public void RemoveTile(int x, int y)
        => Tiles.RemoveAll(t => t.X == x && t.Y == y);
}
