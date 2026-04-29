using MTEngine.ECS;

namespace MTEngine.Npc;

// MTLiving — социальный слой.

public enum RelationshipStatus
{
    Single,
    Dating,
    Engaged,
    Married,
    Widowed,
    Separated
}

/// <summary>
/// Статус отношений NPC: одинок / встречается / помолвлен / женат / вдовец / расстался.
/// Совмещает «текущего партнёра» и расписание событий (свидание, свадьба, рождение).
/// На P4.1 используется только для запоминания пары и дат — переходы статусов
/// обрабатывает RelationshipTickSystem (P4.2+).
/// </summary>
[RegisterComponent("relationships")]
public class RelationshipsComponent : Component
{
    [DataField("status")]
    [SaveField("status")]
    public RelationshipStatus Status { get; set; } = RelationshipStatus.Single;

    /// <summary>SaveId партнёра-NPC. Пустая строка = нет партнёра.</summary>
    [DataField("partnerNpcSaveId")]
    [SaveField("partnerNpcSaveId")]
    public string PartnerNpcSaveId { get; set; } = "";

    /// <summary>true — партнёр это игрок (а не NPC). Используется в P4.5/P8.</summary>
    [DataField("partnerIsPlayer")]
    [SaveField("partnerIsPlayer")]
    public bool PartnerIsPlayer { get; set; }

    /// <summary>День, в который пара начнёт «встречаться». −1 = не запланировано.</summary>
    [DataField("scheduledDateDayIndex")]
    [SaveField("scheduledDateDayIndex")]
    public long ScheduledDateDayIndex { get; set; } = -1L;

    /// <summary>Фактический день начала Dating. Нужен для расчёта ночёвок.</summary>
    [DataField("datingStartedDayIndex")]
    [SaveField("datingStartedDayIndex")]
    public long DatingStartedDayIndex { get; set; } = -1L;

    /// <summary>День свадьбы. −1 = не запланирована.</summary>
    [DataField("scheduledWeddingDayIndex")]
    [SaveField("scheduledWeddingDayIndex")]
    public long ScheduledWeddingDayIndex { get; set; } = -1L;

    /// <summary>Фактический день свадьбы. −1 = ещё не женаты/не замужем.</summary>
    [DataField("marriageDayIndex")]
    [SaveField("marriageDayIndex")]
    public long MarriageDayIndex { get; set; } = -1L;

    /// <summary>День следующего рождения ребёнка. −1 = не запланировано (см. §6).</summary>
    [DataField("scheduledBirthDayIndex")]
    [SaveField("scheduledBirthDayIndex")]
    public long ScheduledBirthDayIndex { get; set; } = -1L;

    /// <summary>Когда последний раз искали пару. Защита от каждодневного скана.</summary>
    [DataField("lastMatchSearchDayIndex")]
    [SaveField("lastMatchSearchDayIndex")]
    public long LastMatchSearchDayIndex { get; set; } = -1L;

    /// <summary>Сколько ночей подряд оставались вместе (для p_overnight, см. §5.4).</summary>
    [DataField("overnightStreak")]
    [SaveField("overnightStreak")]
    public int OvernightStreak { get; set; }

    /// <summary>
    /// Отношение к игроку (-100..100). Падает при агрессии игрока, растёт от подарков/услуг.
    /// 0 — нейтрально. Используется AI для враждебности по умолчанию.
    /// </summary>
    [DataField("playerOpinion")]
    [SaveField("playerOpinion")]
    public int PlayerOpinion { get; set; }

    /// <summary>День, когда NPC последний раз был в одной локации с игроком.
    /// Используется <see cref="PlayerCohabitationSystem"/> для расчёта "игрока долго нет" (см. §5.6).</summary>
    [DataField("lastSeenWithPlayerDayIndex")]
    [SaveField("lastSeenWithPlayerDayIndex")]
    public long LastSeenWithPlayerDayIndex { get; set; } = -1L;

    /// <summary>Партнёр-NPC решил уйти, но физический переезд произойдёт когда игрок войдёт в локацию.
    /// Чистится в P8.4 при фактическом телепорте. См. §5.6.</summary>
    [DataField("pendingRelocation")]
    [SaveField("pendingRelocation")]
    public bool PendingRelocation { get; set; }
}
