using System;

namespace MTEngine.ECS;

/// <summary>
/// Маркер с устойчивым SaveId для сущностей, на которые ссылаются по id (например, NPC,
/// упоминаемые в kin-связях, профессиях, отношениях). SaveGameManager выставляет/читает SaveId
/// при сохранении и загрузке. Живёт в MTEngine, чтобы движковые системы могли его читать без
/// зависимости от слоя сохранений.
/// </summary>
public class SaveEntityIdComponent : Component
{
    public string SaveId { get; set; } = Guid.NewGuid().ToString("N");
}
