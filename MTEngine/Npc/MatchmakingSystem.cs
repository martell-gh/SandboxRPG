using System;
using System.Collections.Generic;
using System.Linq;
using MTEngine.Core;
using MTEngine.ECS;

namespace MTEngine.Npc;

// MTLiving — социальный слой. P4.1.

/// <summary>
/// Раз в день перебирает одиноких NPC, ищет им пару в одном поселении и назначает
/// даты «свидания» и «свадьбы». Сами переходы Single→Dating→Married делает
/// RelationshipTickSystem (P4.2+) — здесь только планирование.
/// </summary>
public class MatchmakingSystem : GameSystem
{
    private const int SearchCooldownDays = 7;
    private const int MinAgeForRelationship = 18;
    private const int MinDaysToDate = 2;
    private const int MaxDaysToDate = 14;
    private const int MinDaysToWedding = 30;
    private const int MaxDaysToWedding = 120;

    private GameClock _clock = null!;
    private EventBus _bus = null!;
    private readonly Random _random = new();
    // MapLoadedEvent приходит раньше, чем NpcRosterSpawner материализует NPC,
    // поэтому делаем initial-sweep отложенно через Update.
    private bool _pendingInitialSweep;

    public override void OnInitialize()
    {
        _clock = ServiceLocator.Get<GameClock>();
        _bus = ServiceLocator.Get<EventBus>();
        _bus.Subscribe<DayChanged>(OnDayChanged);
        _bus.Subscribe<MTEngine.World.MapLoadedEvent>(_ => _pendingInitialSweep = true);
    }

    public override void Update(float deltaTime)
    {
        if (!_pendingInitialSweep) return;
        _pendingInitialSweep = false;
        // Прокатываем матч-цикл сразу после загрузки карты, не ждём midnight —
        // иначе впервые загруженные NPC простаивают до следующего дня.
        RunMatchPass(_clock.DayIndex);
    }

    private void OnDayChanged(DayChanged evt) => RunMatchPass(evt.NewDayIndex);

    private void RunMatchPass(long today)
    {
        var singles = CollectEligibleSingles(today);
        if (singles.Count < 2)
            return;

        // Группируем по поселению — ищем пару только внутри settlement-а.
        var bySettlement = singles
            .GroupBy(s => s.Identity.SettlementId, StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key));

        foreach (var group in bySettlement)
            ProcessSettlement(group.ToList(), today);
    }

    private void ProcessSettlement(List<SingleEntry> singles, long today)
    {
        // Берём только тех, кому пора искать (cooldown 7 дней).
        var ready = singles.Where(s => CanSearchToday(s.Relationships, today)).ToList();
        if (ready.Count == 0)
            return;

        // Перетасуем, чтобы порядок не давал детерминированной пары на одном и том же seed.
        for (var i = ready.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (ready[i], ready[j]) = (ready[j], ready[i]);
        }

        var paired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var seeker in ready)
        {
            seeker.Relationships.LastMatchSearchDayIndex = today;

            if (paired.Contains(seeker.SaveId))
                continue;

            // Кандидаты: противоположный пол, не родственник, не уже спарен в этом тике, eligible.
            var candidates = singles
                .Where(other => !ReferenceEquals(other, seeker))
                .Where(other => !paired.Contains(other.SaveId))
                .Where(other => CanSearchPartnerToday(other.Relationships, today))
                .Where(other => other.Identity.Gender != seeker.Identity.Gender)
                .Where(other => !AreKin(seeker, other))
                .ToList();

            if (candidates.Count == 0)
                continue;

            var partner = candidates[_random.Next(candidates.Count)];
            partner.Relationships.LastMatchSearchDayIndex = today;

            var daysToDate = (RandomBetween(MinDaysToDate, MaxDaysToDate)
                              + RandomBetween(MinDaysToDate, MaxDaysToDate)) / 2;
            var daysToWedding = (RandomBetween(MinDaysToWedding, MaxDaysToWedding)
                                 + RandomBetween(MinDaysToWedding, MaxDaysToWedding)) / 2;

            var dateDay = today + daysToDate;
            var weddingDay = dateDay + daysToWedding;

            seeker.Relationships.PartnerNpcSaveId = partner.SaveId;
            seeker.Relationships.PartnerIsPlayer = false;
            seeker.Relationships.ScheduledDateDayIndex = dateDay;
            seeker.Relationships.ScheduledWeddingDayIndex = weddingDay;

            partner.Relationships.PartnerNpcSaveId = seeker.SaveId;
            partner.Relationships.PartnerIsPlayer = false;
            partner.Relationships.ScheduledDateDayIndex = dateDay;
            partner.Relationships.ScheduledWeddingDayIndex = weddingDay;

            paired.Add(seeker.SaveId);
            paired.Add(partner.SaveId);

            _bus.Publish(new RelationshipDateScheduled(seeker.SaveId, partner.SaveId, dateDay, weddingDay));
        }
    }

    private List<SingleEntry> CollectEligibleSingles(long today)
    {
        var result = new List<SingleEntry>();

        foreach (var entity in World.GetEntitiesWith<NpcTagComponent, RelationshipsComponent>())
        {
            if (!NpcLod.IsActiveOrBackground(entity))
                continue;

            var relationships = entity.GetComponent<RelationshipsComponent>()!;
            if (relationships.Status != RelationshipStatus.Single)
                continue;

            if (!string.IsNullOrEmpty(relationships.PartnerNpcSaveId))
                continue;

            var identity = entity.GetComponent<IdentityComponent>();
            if (identity == null || string.IsNullOrWhiteSpace(identity.SettlementId))
                continue;

            var age = entity.GetComponent<AgeComponent>();
            if (age == null || age.Years < MinAgeForRelationship)
                continue;

            var saveMarker = entity.GetComponent<SaveEntityIdComponent>();
            if (saveMarker == null || string.IsNullOrWhiteSpace(saveMarker.SaveId))
                continue;

            result.Add(new SingleEntry
            {
                SaveId = saveMarker.SaveId,
                Identity = identity,
                Age = age,
                Relationships = relationships,
                Kin = entity.GetComponent<KinComponent>()
            });
        }

        return result;
    }

    private static bool CanSearchToday(RelationshipsComponent r, long today)
        => r.LastMatchSearchDayIndex < 0L
           || today - r.LastMatchSearchDayIndex >= SearchCooldownDays;

    /// <summary>
    /// Партнёра можно «брать» даже если у него своё окно поиска ещё открыто —
    /// главное, чтобы он сам ещё не нашёл пару. Cooldown проверяется только для seeker.
    /// </summary>
    private static bool CanSearchPartnerToday(RelationshipsComponent r, long today)
        => string.IsNullOrEmpty(r.PartnerNpcSaveId);

    private static bool AreKin(SingleEntry a, SingleEntry b)
    {
        if (a.Kin != null && a.Kin.Links.Any(l =>
                string.Equals(l.NpcSaveId, b.SaveId, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (b.Kin != null && b.Kin.Links.Any(l =>
                string.Equals(l.NpcSaveId, a.SaveId, StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    private int RandomBetween(int minInclusive, int maxInclusive)
        => _random.Next(minInclusive, maxInclusive + 1);

    private sealed class SingleEntry
    {
        public required string SaveId { get; init; }
        public required IdentityComponent Identity { get; init; }
        public required AgeComponent Age { get; init; }
        public required RelationshipsComponent Relationships { get; init; }
        public KinComponent? Kin { get; init; }
    }
}
