using MTEngine.ECS;

namespace MTEngine.Npc;

/// <summary>
/// Связь NPC с занимаемым ProfessionSlot. Полная логика (рост навыка, торговля)
/// в P5; здесь хватит ссылок, чтобы ScheduleSystem мог найти work_anchor.
/// </summary>
[RegisterComponent("profession")]
public class ProfessionComponent : Component
{
    [DataField("professionId")] [SaveField("professionId")]
    public string ProfessionId { get; set; } = "";

    [DataField("slotId")] [SaveField("slotId")]
    public string SlotId { get; set; } = "";

    [SaveField("joinedDayIndex")]
    public long JoinedDayIndex { get; set; }
}
