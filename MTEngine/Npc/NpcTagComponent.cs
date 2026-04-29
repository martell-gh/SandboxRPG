using MTEngine.ECS;

namespace MTEngine.Npc;

/// <summary>
/// Маркер NPC. Нужен только для фильтрации запросов
/// (типа "все entity, которые управляются AI").
/// </summary>
[RegisterComponent("npc")]
public class NpcTagComponent : Component { }
