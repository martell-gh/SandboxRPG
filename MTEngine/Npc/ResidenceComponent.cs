using MTEngine.ECS;

namespace MTEngine.Npc;

/// <summary>
/// Где NPC живёт: id дома (HouseDef.Id) + id точки кровати (bed_slot_a / inn_bed_3 / orphan_bed_2 ...).
/// Если HouseId пустой — NPC бездомный (например, безработный без места в Inn).
/// </summary>
[RegisterComponent("residence")]
public class ResidenceComponent : Component
{
    [DataField("houseId")] [SaveField("houseId")]
    public string HouseId { get; set; } = "";

    [DataField("bedSlotId")] [SaveField("bedSlotId")]
    public string BedSlotId { get; set; } = "";

    public bool IsHomeless => string.IsNullOrEmpty(HouseId);
}
