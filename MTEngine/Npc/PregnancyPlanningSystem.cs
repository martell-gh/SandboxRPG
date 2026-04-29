using MTEngine.Core;
using MTEngine.ECS;

namespace MTEngine.Npc;

/// <summary>
/// §6.1: раз в игровой месяц катаем шанс зачатия для женатых пар обоих фертильных
/// (18..40). Шанс = avgChildWish * 0.10. При успехе и наличии свободного child_bed
/// в их доме на обоих партнёрах ставится <c>ScheduledBirthDayIndex</c>.
/// </summary>
public class PregnancyPlanningSystem : GameSystem
{
    private const int MinFertileAge = 18;
    private const int MaxFertileAge = 40;
    private const int MinDaysToBirth = 14;
    private const int MaxDaysToBirth = 35;

    private EventBus _bus = null!;
    private Calendar? _calendar;
    private WorldRegistry? _registry;
    private readonly Random _rng = new();

    public override void OnInitialize()
    {
        _bus = ServiceLocator.Get<EventBus>();
        if (ServiceLocator.Has<Calendar>())
            _calendar = ServiceLocator.Get<Calendar>();
        if (ServiceLocator.Has<WorldRegistry>())
            _registry = ServiceLocator.Get<WorldRegistry>();
        _bus.Subscribe<DayChanged>(OnDayChanged);
    }

    public override void Update(float deltaTime) { }

    public override void OnDestroy() => _bus.Unsubscribe<DayChanged>(OnDayChanged);

    private void OnDayChanged(DayChanged evt)
    {
        if (_calendar == null || _registry == null)
            return;

        // Раз в "игровой месяц" — на 1-е число.
        var date = _calendar.FromDayIndex(evt.NewDayIndex);
        if (date.Day != 1)
            return;

        var entries = CollectEntries();
        if (entries.Count == 0)
            return;

        var bySaveId = entries.ToDictionary(e => e.SaveId, e => e, StringComparer.OrdinalIgnoreCase);
        var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            if (processed.Contains(entry.SaveId))
                continue;

            var rel = entry.Relationships;
            if (rel.Status != RelationshipStatus.Married || string.IsNullOrWhiteSpace(rel.PartnerNpcSaveId))
                continue;
            if (rel.ScheduledBirthDayIndex >= 0L)
                continue;
            if (!bySaveId.TryGetValue(rel.PartnerNpcSaveId, out var partner))
                continue;
            if (partner.Relationships.Status != RelationshipStatus.Married)
                continue;
            if (partner.Relationships.ScheduledBirthDayIndex >= 0L)
                continue;

            // Возраст обоих 18..40.
            if (!IsFertile(entry) || !IsFertile(partner))
                continue;

            // Дом и свободный child_bed.
            var residence = entry.Entity.GetComponent<ResidenceComponent>()
                ?? partner.Entity.GetComponent<ResidenceComponent>();
            if (residence == null || string.IsNullOrWhiteSpace(residence.HouseId)
                || !_registry.Houses.TryGetValue(residence.HouseId, out var house))
                continue;
            if (HouseHasNoFreeChildBed(house))
                continue;

            var entryWish = ClampWish(entry.Personality?.ChildWish);
            var partnerWish = ClampWish(partner.Personality?.ChildWish);
            var avgWish = (entryWish + partnerWish) / 2f;
            var p = avgWish * 0.10f;
            if (_rng.NextDouble() >= p)
            {
                processed.Add(entry.SaveId);
                processed.Add(partner.SaveId);
                continue;
            }

            var daysToBirth = _rng.Next(MinDaysToBirth, MaxDaysToBirth + 1);
            var birthDay = evt.NewDayIndex + daysToBirth;

            rel.ScheduledBirthDayIndex = birthDay;
            partner.Relationships.ScheduledBirthDayIndex = birthDay;

            var (motherSaveId, fatherSaveId) = ResolveParents(entry, partner);
            _bus.Publish(new PregnancyScheduled(motherSaveId, fatherSaveId, birthDay));

            processed.Add(entry.SaveId);
            processed.Add(partner.SaveId);
        }
    }

    private bool IsFertile(Entry entry)
    {
        var age = entry.Entity.GetComponent<AgeComponent>();
        if (age == null) return false;
        return age.Years >= MinFertileAge && age.Years <= MaxFertileAge;
    }

    private bool HouseHasNoFreeChildBed(HouseDef house)
    {
        if (house.ChildBedSlots.Count == 0)
            return true;

        var occupied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in World.GetEntitiesWith<ResidenceComponent>())
        {
            var residence = entity.GetComponent<ResidenceComponent>()!;
            if (string.Equals(residence.HouseId, house.Id, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(residence.BedSlotId))
            {
                occupied.Add(residence.BedSlotId);
            }
        }

        return house.GetFreeChildBedSlot(occupied) == null;
    }

    private List<Entry> CollectEntries()
    {
        var list = new List<Entry>();
        foreach (var entity in World.GetEntitiesWith<NpcTagComponent, RelationshipsComponent>())
        {
            if (!entity.Active)
                continue;
            if (!NpcLod.IsActiveOrBackground(entity))
                continue;

            var marker = entity.GetComponent<SaveEntityIdComponent>();
            if (marker == null || string.IsNullOrWhiteSpace(marker.SaveId))
                continue;

            list.Add(new Entry
            {
                Entity = entity,
                SaveId = marker.SaveId,
                Relationships = entity.GetComponent<RelationshipsComponent>()!,
                Personality = entity.GetComponent<PersonalityComponent>()
            });
        }
        return list;
    }

    private static int ClampWish(int? wish) => Math.Clamp(wish ?? 0, 0, 10);

    private static (string MotherSaveId, string FatherSaveId) ResolveParents(Entry a, Entry b)
    {
        var aGender = a.Entity.GetComponent<IdentityComponent>()?.Gender;
        var bGender = b.Entity.GetComponent<IdentityComponent>()?.Gender;
        if (aGender == Gender.Female) return (a.SaveId, b.SaveId);
        if (bGender == Gender.Female) return (b.SaveId, a.SaveId);
        return (a.SaveId, b.SaveId); // fallback
    }

    private sealed class Entry
    {
        public required Entity Entity { get; init; }
        public required string SaveId { get; init; }
        public required RelationshipsComponent Relationships { get; init; }
        public PersonalityComponent? Personality { get; init; }
    }
}
