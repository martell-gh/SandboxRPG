using System;
using System.Linq;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;

namespace MTEngine.Npc;

/// <summary>
/// P8.2 — измена со стороны партнёра-NPC, который женат на игроке.
///
/// Каждое <see cref="DayChanged"/>:
///  1. Если NPC сейчас в Active LOD (в одной локации с игроком) — обновляем
///     <see cref="RelationshipsComponent.LastSeenWithPlayerDayIndex"/>.
///  2. Для женатых на игроке считаем "терпение" по <see cref="PersonalityComponent.Infidelity"/>:
///     при Infidelity ≤ 0 партнёр верен по определению; иначе порог ≈ (11 - Infidelity) * cheatSensitivity дней.
///  3. Когда дней без встречи > порога, прокатываем шанс — и если выпало, помечаем NPC как
///     "ушёл": Status = Separated, PartnerIsPlayer = false, PendingRelocation = true,
///     публикуем <see cref="RelationshipSeparated"/>. Физический переезд/записку
///     обрабатывают P8.3/P8.4 на <see cref="World.MapLoadedEvent"/>.
/// </summary>
public class PlayerCohabitationSystem : GameSystem
{
    /// <summary>Базовая шкала терпения. Порог в днях ≈ (11 - Infidelity) * этот коэффициент.</summary>
    private const float CheatSensitivityDaysPerStep = 5f;
    private const int MinThresholdDays = 3;
    /// <summary>Шанс, что NPC уходит в день, когда порог уже пройден.</summary>
    private const double ChancePerDayOnceTriggered = 0.5;

    private EventBus _bus = null!;
    private readonly Random _rng = new();

    public override void OnInitialize()
    {
        _bus = ServiceLocator.Get<EventBus>();
        _bus.Subscribe<DayChanged>(OnDayChanged);
    }

    public override void Update(float deltaTime)
    {
        // Событийная.
    }

    public override void OnDestroy()
    {
        _bus.Unsubscribe<DayChanged>(OnDayChanged);
    }

    private void OnDayChanged(DayChanged evt)
    {
        var today = evt.NewDayIndex;
        var player = World.GetEntitiesWith<PlayerTagComponent>().FirstOrDefault();
        var dirty = false;

        foreach (var npc in World.GetEntitiesWith<NpcTagComponent, RelationshipsComponent>())
        {
            if (!npc.Active)
                continue;

            var rel = npc.GetComponent<RelationshipsComponent>()!;
            if (!rel.PartnerIsPlayer)
                continue;
            if (rel.Status != RelationshipStatus.Married)
                continue;
            if (npc.GetComponent<HealthComponent>()?.IsDead == true)
                continue;

            // 1) Обновить "последний раз видели с игроком".
            if (player != null && NpcLod.IsActive(npc))
                rel.LastSeenWithPlayerDayIndex = today;

            // 2) Расчёт порога терпения.
            var infidelity = npc.GetComponent<PersonalityComponent>()?.Infidelity ?? 0;
            if (infidelity <= 0)
                continue;

            var threshold = Math.Max(
                MinThresholdDays,
                (int)Math.Round((11 - infidelity) * CheatSensitivityDaysPerStep));

            // Если LastSeenWithPlayerDayIndex ещё не выставлен (старый сейв / только что поженились) —
            // считаем, что встреча была "сегодня", чтобы не уйти в день женитьбы.
            if (rel.LastSeenWithPlayerDayIndex < 0)
            {
                rel.LastSeenWithPlayerDayIndex = today;
                continue;
            }

            var daysSinceSeen = today - rel.LastSeenWithPlayerDayIndex;
            if (daysSinceSeen < threshold)
                continue;

            // 3) Прокат шанса.
            if (_rng.NextDouble() > ChancePerDayOnceTriggered)
                continue;

            var npcSaveId = npc.GetComponent<SaveEntityIdComponent>()?.SaveId ?? "";
            rel.Status = RelationshipStatus.Separated;
            rel.PartnerIsPlayer = false;
            rel.PartnerNpcSaveId = "";
            rel.OvernightStreak = 0;
            rel.PendingRelocation = true;

            _bus.Publish(new RelationshipSeparated(
                npcSaveId,
                otherSaveId: "player",
                dayIndex: today,
                cause: "cohabitation_neglect"));

            dirty = true;
        }

        if (dirty && ServiceLocator.Has<IWorldStateTracker>())
            ServiceLocator.Get<IWorldStateTracker>().MarkDirty();
    }
}
