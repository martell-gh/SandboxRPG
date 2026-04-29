using System;
using System.Collections.Generic;
using MTEngine.Core;
using MTEngine.ECS;

namespace MTEngine.Npc;

// MTLiving — социальный слой. P4.2-P4.4.

/// <summary>
/// Обрабатывает наступление запланированных отношений: Single -> Dating -> Married.
/// MatchmakingSystem только назначает даты; эта система применяет их на DayChanged.
/// </summary>
public class RelationshipTickSystem : GameSystem
{
    private EventBus _bus = null!;
    private WorldRegistry? _registry;

    public override void OnInitialize()
    {
        _bus = ServiceLocator.Get<EventBus>();
        if (ServiceLocator.Has<WorldRegistry>())
            _registry = ServiceLocator.Get<WorldRegistry>();
        _bus.Subscribe<DayChanged>(OnDayChanged);
    }

    public override void Update(float deltaTime)
    {
        // Никакой работы в кадре — система событийная.
    }

    private void OnDayChanged(DayChanged evt)
    {
        var today = evt.NewDayIndex;
        var entries = CollectEntries();
        if (entries.Count == 0)
            return;

        var bySaveId = new Dictionary<string, RelationshipEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (!bySaveId.ContainsKey(entry.SaveId))
                bySaveId.Add(entry.SaveId, entry);
        }

        var dirty = false;
        var processedStarts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
            dirty |= TryStartDating(entry, bySaveId, processedStarts, today);

        var processedWeddings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
            dirty |= TryMarry(entry, bySaveId, processedWeddings, today);

        if (dirty && ServiceLocator.Has<IWorldStateTracker>())
            ServiceLocator.Get<IWorldStateTracker>().MarkDirty();
    }

    private List<RelationshipEntry> CollectEntries()
    {
        var result = new List<RelationshipEntry>();

        foreach (var entity in World.GetEntitiesWith<NpcTagComponent, RelationshipsComponent>())
        {
            if (!entity.Active)
                continue;
            if (!NpcLod.IsActiveOrBackground(entity))
                continue;

            var saveMarker = entity.GetComponent<SaveEntityIdComponent>();
            if (saveMarker == null || string.IsNullOrWhiteSpace(saveMarker.SaveId))
                continue;

            result.Add(new RelationshipEntry
            {
                Entity = entity,
                SaveId = saveMarker.SaveId,
                Relationships = entity.GetComponent<RelationshipsComponent>()!
            });
        }

        return result;
    }

    private bool TryStartDating(
        RelationshipEntry entry,
        IReadOnlyDictionary<string, RelationshipEntry> bySaveId,
        HashSet<string> processed,
        long today)
    {
        var r = entry.Relationships;
        if (r.Status != RelationshipStatus.Single ||
            r.PartnerIsPlayer ||
            string.IsNullOrWhiteSpace(r.PartnerNpcSaveId) ||
            r.ScheduledDateDayIndex < 0L ||
            today < r.ScheduledDateDayIndex)
        {
            return false;
        }

        if (!bySaveId.TryGetValue(r.PartnerNpcSaveId, out var partner) ||
            !IsMirroredPair(entry, partner) ||
            partner.Relationships.Status != RelationshipStatus.Single)
        {
            return false;
        }

        var pairKey = PairKey(entry.SaveId, partner.SaveId);
        if (!processed.Add(pairKey))
            return false;

        r.Status = RelationshipStatus.Dating;
        partner.Relationships.Status = RelationshipStatus.Dating;
        r.DatingStartedDayIndex = today;
        partner.Relationships.DatingStartedDayIndex = today;
        r.ScheduledDateDayIndex = -1L;
        partner.Relationships.ScheduledDateDayIndex = -1L;

        _bus.Publish(new RelationshipStarted(entry.SaveId, partner.SaveId, today));
        return true;
    }

    private bool TryMarry(
        RelationshipEntry entry,
        IReadOnlyDictionary<string, RelationshipEntry> bySaveId,
        HashSet<string> processed,
        long today)
    {
        var r = entry.Relationships;
        if (!IsWeddingEligibleStatus(r.Status) ||
            r.PartnerIsPlayer ||
            string.IsNullOrWhiteSpace(r.PartnerNpcSaveId) ||
            r.ScheduledWeddingDayIndex < 0L ||
            today < r.ScheduledWeddingDayIndex)
        {
            return false;
        }

        if (!bySaveId.TryGetValue(r.PartnerNpcSaveId, out var partner) ||
            !IsMirroredPair(entry, partner) ||
            !IsWeddingEligibleStatus(partner.Relationships.Status))
        {
            return false;
        }

        var pairKey = PairKey(entry.SaveId, partner.SaveId);
        if (!processed.Add(pairKey))
            return false;

        r.Status = RelationshipStatus.Married;
        partner.Relationships.Status = RelationshipStatus.Married;
        r.MarriageDayIndex = today;
        partner.Relationships.MarriageDayIndex = today;
        r.ScheduledDateDayIndex = -1L;
        partner.Relationships.ScheduledDateDayIndex = -1L;
        r.ScheduledWeddingDayIndex = -1L;
        partner.Relationships.ScheduledWeddingDayIndex = -1L;
        r.OvernightStreak = 0;
        partner.Relationships.OvernightStreak = 0;

        TryMoveSpouseIntoSharedHome(entry, partner, today);

        _bus.Publish(new RelationshipMarried(entry.SaveId, partner.SaveId, today));
        return true;
    }

    private bool TryMoveSpouseIntoSharedHome(RelationshipEntry a, RelationshipEntry b, long today)
    {
        if (_registry == null)
            return false;

        var (mover, host) = ChooseMovePair(a, b);
        var moverResidence = mover.Entity.GetComponent<ResidenceComponent>();
        var hostResidence = host.Entity.GetComponent<ResidenceComponent>();
        if (moverResidence == null || hostResidence == null)
            return false;

        if (!_registry.Houses.TryGetValue(hostResidence.HouseId, out var newHouse))
        {
            (mover, host) = (host, mover);
            moverResidence = mover.Entity.GetComponent<ResidenceComponent>();
            hostResidence = host.Entity.GetComponent<ResidenceComponent>();
            if (moverResidence == null || hostResidence == null ||
                !_registry.Houses.TryGetValue(hostResidence.HouseId, out newHouse))
            {
                return false;
            }
        }

        if (string.Equals(moverResidence.HouseId, newHouse.Id, StringComparison.OrdinalIgnoreCase))
            return false;

        var oldHouseId = moverResidence.HouseId;
        if (!string.IsNullOrEmpty(oldHouseId) && _registry.Houses.TryGetValue(oldHouseId, out var oldHouse))
            oldHouse.ResidentNpcSaveIds.Remove(mover.SaveId);

        moverResidence.HouseId = newHouse.Id;
        moverResidence.BedSlotId = PickFreeBedSlot(newHouse, mover.SaveId);
        newHouse.ResidentNpcSaveIds.Add(mover.SaveId);
        ApplyHouseIdentity(mover.Entity, newHouse);

        _bus.Publish(new NpcMovedHouse(mover.SaveId, oldHouseId, newHouse.Id, today));
        return true;
    }

    private (RelationshipEntry Mover, RelationshipEntry Host) ChooseMovePair(RelationshipEntry a, RelationshipEntry b)
    {
        var aGender = a.Entity.GetComponent<IdentityComponent>()?.Gender;
        var bGender = b.Entity.GetComponent<IdentityComponent>()?.Gender;

        if (aGender == Gender.Female && bGender == Gender.Male)
            return (a, b);
        if (bGender == Gender.Female && aGender == Gender.Male)
            return (b, a);

        return HasValidHouse(a) && !HasValidHouse(b)
            ? (b, a)
            : (a, b);
    }

    private bool HasValidHouse(RelationshipEntry entry)
    {
        var residence = entry.Entity.GetComponent<ResidenceComponent>();
        return residence != null
               && !string.IsNullOrWhiteSpace(residence.HouseId)
               && _registry != null
               && _registry.Houses.ContainsKey(residence.HouseId);
    }

    private string PickFreeBedSlot(HouseDef house, string moverSaveId)
    {
        if (house.BedSlots.Count == 0)
            return "";

        var occupied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in World.GetEntitiesWith<ResidenceComponent>())
        {
            var marker = entity.GetComponent<SaveEntityIdComponent>();
            if (marker != null && string.Equals(marker.SaveId, moverSaveId, StringComparison.OrdinalIgnoreCase))
                continue;

            var residence = entity.GetComponent<ResidenceComponent>()!;
            if (string.Equals(residence.HouseId, house.Id, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(residence.BedSlotId))
            {
                occupied.Add(residence.BedSlotId);
            }
        }

        return house.BedSlots
                   .Where(slot => !occupied.Contains(slot.Id))
                   .Select(slot => slot.Id)
                   .FirstOrDefault()
               ?? house.BedSlots[0].Id;
    }

    private static void ApplyHouseIdentity(Entity entity, HouseDef house)
    {
        var identity = entity.GetComponent<IdentityComponent>();
        if (identity == null)
            return;

        identity.FactionId = house.FactionId;
        identity.SettlementId = house.SettlementId;
        identity.DistrictId = house.DistrictId;
    }

    private static bool IsMirroredPair(RelationshipEntry a, RelationshipEntry b)
        => !a.Relationships.PartnerIsPlayer
           && !b.Relationships.PartnerIsPlayer
           && string.Equals(a.Relationships.PartnerNpcSaveId, b.SaveId, StringComparison.OrdinalIgnoreCase)
           && string.Equals(b.Relationships.PartnerNpcSaveId, a.SaveId, StringComparison.OrdinalIgnoreCase);

    private static bool IsWeddingEligibleStatus(RelationshipStatus status)
        => status is RelationshipStatus.Dating or RelationshipStatus.Engaged;

    private static string PairKey(string a, string b)
        => string.Compare(a, b, StringComparison.OrdinalIgnoreCase) <= 0
            ? $"{a}|{b}"
            : $"{b}|{a}";

    public override void OnDestroy()
        => _bus.Unsubscribe<DayChanged>(OnDayChanged);

    private sealed class RelationshipEntry
    {
        public required Entity Entity { get; init; }
        public required string SaveId { get; init; }
        public required RelationshipsComponent Relationships { get; init; }
    }
}
