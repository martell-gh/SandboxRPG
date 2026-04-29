using MTEngine.ECS;

namespace MTEngine.Npc;

/// <summary>
/// Уровни навыков 0..10. id -> уровень.
/// Список доступных навыков лежит в Data/skills.json (в P5+).
/// </summary>
[RegisterComponent("npcSkills")]
public class SkillsComponent : Component
{
    [DataField("values")] [SaveField("values")]
    public Dictionary<string, float> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public float Get(string id)
        => Values.TryGetValue(id, out var v) ? v : 0f;

    public void Set(string id, float value)
        => Values[id] = Math.Clamp(value, 0f, 10f);

    public void Add(string id, float delta)
        => Set(id, Get(id) + delta);

    /// <summary>Лучший навык (id, value). Если все нули — вернёт null.</summary>
    public (string Id, float Value)? Best()
    {
        if (Values.Count == 0) return null;
        var top = Values.MaxBy(kv => kv.Value);
        return top.Value > 0f ? (top.Key, top.Value) : null;
    }
}
