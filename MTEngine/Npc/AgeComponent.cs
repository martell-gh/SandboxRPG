using MTEngine.Core;
using MTEngine.ECS;

namespace MTEngine.Npc;

/// <summary>
/// Возраст entity, считается через <see cref="Calendar"/> и <see cref="GameClock.DayIndex"/>.
/// BirthDayIndex = -1 означает "не инициализирован" — спаунер должен проставить.
/// </summary>
[RegisterComponent("age")]
public class AgeComponent : Component
{
    [DataField("birthDayIndex")]
    [SaveField("birthDayIndex")]
    public long BirthDayIndex { get; set; } = -1L;

    /// <summary>Кешированный возраст в годах. Пересчитывается AgingSystem раз в день.</summary>
    [SaveField("years")]
    public int Years { get; set; }

    [SaveField("isPensioner")]
    public bool IsPensioner { get; set; }

    /// <summary>
    /// Прокси-поле для прототипа: позволяет указать "ageYears" вместо BirthDayIndex.
    /// EntityFactory увидит DataField и заполнит, а AgingSystem конвертирует в BirthDayIndex
    /// при первом тике, если BirthDayIndex == -1.
    /// </summary>
    [DataField("ageYears")]
    public int InitialAgeYears { get; set; } = -1;
}
