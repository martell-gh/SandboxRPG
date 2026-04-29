using System.Text.Json.Serialization;
using MTEngine.ECS;

namespace MTEngine.Npc;

/// <summary>
/// Что NPC должен делать в данный час. Resolver выбирает один слот на основе текущего часа.
/// </summary>
public enum ScheduleAction
{
    Sleep,          // в свою кровать в Residence (под защитой HomeIntrusionSystem)
    EatAtHome,      // дом
    Work,           // на свой ProfessionSlot
    Wander,         // wander_*-точки в одной из ближайших area
    Socialize,      // фритайм: подойти к другому NPC и коротко поговорить
    Visit,          // конкретная entity (партнёр / друг)
    StayInTavern,   // безработные ночью
    SchoolDay,      // ребёнок
    StayAtHome,     // просто на территории своего дома (бродит, НЕ спит, не атакует)
    Free            // → выбрать из Freetime
}

public class ScheduleSlot
{
    [JsonPropertyName("start")]
    public int StartHour { get; set; }                // 0..23

    [JsonPropertyName("end")]
    public int EndHour { get; set; }                  // exclusive, может быть до 24

    [JsonPropertyName("action")]
    public ScheduleAction Action { get; set; }

    [JsonPropertyName("targetAreaId")]
    public string TargetAreaId { get; set; } = "";    // "$house", "$profession", или конкретный area-id

    [JsonPropertyName("priority")]
    public int Priority { get; set; }
}

public class FreetimeOption
{
    [JsonPropertyName("action")]
    public ScheduleAction Action { get; set; }

    [JsonPropertyName("targetAreaId")]
    public string TargetAreaId { get; set; } = "";

    [JsonPropertyName("priority")]
    public int Priority { get; set; }

    [JsonPropertyName("dayOnly")]
    public bool DayOnly { get; set; }

    [JsonPropertyName("nightOnly")]
    public bool NightOnly { get; set; }

    [JsonPropertyName("conditions")]
    public List<string> Conditions { get; set; } = new();   // "has_partner", "in_relationship"
}

/// <summary>
/// Расписание NPC. Заполняется из шаблона профессии или роли.
/// Slots — фиксированные интервалы суток. Freetime — фоллбэк, если активный слот = Free.
/// </summary>
[RegisterComponent("schedule")]
public class ScheduleComponent : Component
{
    [DataField("templateId")] [SaveField("templateId")]
    public string TemplateId { get; set; } = "";

    [DataField("slots")] [SaveField("slots")]
    public List<ScheduleSlot> Slots { get; set; } = new();

    [DataField("freetime")] [SaveField("freetime")]
    public List<FreetimeOption> Freetime { get; set; } = new();

    public ScheduleSlot? FindSlot(int hour)
    {
        foreach (var s in Slots)
        {
            if (s.StartHour <= s.EndHour)
            {
                if (hour >= s.StartHour && hour < s.EndHour) return s;
            }
            else
            {
                // wrap (e.g. 22..6)
                if (hour >= s.StartHour || hour < s.EndHour) return s;
            }
        }
        return null;
    }
}
