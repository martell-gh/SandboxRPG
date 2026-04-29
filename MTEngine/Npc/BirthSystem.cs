using Microsoft.Xna.Framework;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.World;

namespace MTEngine.Npc;

/// <summary>
/// §6.2: на DayChanged находит жён, у которых наступил <c>ScheduledBirthDayIndex</c>,
/// спаунит ребёнка в их доме (proto <c>npc_base</c>), заполняет identity/age/residence/kin,
/// фиксирует целевые навыки в <see cref="ChildGrowthComponent"/>, делит ChildWish обоих
/// родителей пополам.
/// </summary>
public class BirthSystem : GameSystem
{
    private const string BabyPrototypeId = "npc_base";
    private const string ChildScheduleTemplateId = "default_child";

    private EventBus _bus = null!;
    private GameClock? _clock;
    private Calendar? _calendar;
    private WorldRegistry? _registry;
    private PrototypeManager? _prototypes;
    private EntityFactory? _factory;
    private MapManager? _mapManager;
    private readonly Random _rng = new();

    public override void OnInitialize()
    {
        _bus = ServiceLocator.Get<EventBus>();
        _bus.Subscribe<DayChanged>(OnDayChanged);
    }

    public override void Update(float deltaTime) { }

    public override void OnDestroy() => _bus.Unsubscribe<DayChanged>(OnDayChanged);

    private bool EnsureServices()
    {
        _clock ??= ServiceLocator.Has<GameClock>() ? ServiceLocator.Get<GameClock>() : null;
        _calendar ??= ServiceLocator.Has<Calendar>() ? ServiceLocator.Get<Calendar>() : null;
        _registry ??= ServiceLocator.Has<WorldRegistry>() ? ServiceLocator.Get<WorldRegistry>() : null;
        _prototypes ??= ServiceLocator.Has<PrototypeManager>() ? ServiceLocator.Get<PrototypeManager>() : null;
        _factory ??= ServiceLocator.Has<EntityFactory>() ? ServiceLocator.Get<EntityFactory>() : null;
        _mapManager ??= ServiceLocator.Has<MapManager>() ? ServiceLocator.Get<MapManager>() : null;
        return _clock != null && _calendar != null && _registry != null
               && _prototypes != null && _factory != null && _mapManager != null;
    }

    private void OnDayChanged(DayChanged evt)
    {
        if (!EnsureServices())
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
            if (rel.ScheduledBirthDayIndex < 0L || evt.NewDayIndex < rel.ScheduledBirthDayIndex)
                continue;
            if (string.IsNullOrWhiteSpace(rel.PartnerNpcSaveId))
                continue;
            if (!bySaveId.TryGetValue(rel.PartnerNpcSaveId, out var partner))
                continue;

            // Только пара по обе стороны (защита от рассинхронизации).
            if (partner.Relationships.ScheduledBirthDayIndex != rel.ScheduledBirthDayIndex)
                continue;

            var (mother, father) = ResolveParents(entry, partner);
            TrySpawnChild(mother, father, evt.NewDayIndex);

            // Сбросим планы и поделим ChildWish.
            rel.ScheduledBirthDayIndex = -1L;
            partner.Relationships.ScheduledBirthDayIndex = -1L;
            HalveChildWish(entry);
            HalveChildWish(partner);

            processed.Add(entry.SaveId);
            processed.Add(partner.SaveId);
        }
    }

    private void TrySpawnChild(Entry mother, Entry father, long today)
    {
        var motherResidence = mother.Entity.GetComponent<ResidenceComponent>();
        var fatherResidence = father.Entity.GetComponent<ResidenceComponent>();
        var residence = motherResidence ?? fatherResidence;
        if (residence == null || string.IsNullOrWhiteSpace(residence.HouseId))
            return;
        if (!_registry!.Houses.TryGetValue(residence.HouseId, out var house))
            return;

        var childGender = _rng.Next(2) == 0 ? Gender.Male : Gender.Female;
        var motherIdentity = mother.Entity.GetComponent<IdentityComponent>();
        var fatherIdentity = father.Entity.GetComponent<IdentityComponent>();

        var lastName = !string.IsNullOrWhiteSpace(fatherIdentity?.LastName)
            ? fatherIdentity.LastName
            : motherIdentity?.LastName ?? "";
        var firstName = ChildNameGenerator.Pick(childGender, _rng);

        var proto = _prototypes!.GetEntity(BabyPrototypeId);
        if (proto == null)
        {
            Console.WriteLine($"[BirthSystem] Prototype '{BabyPrototypeId}' not found.");
            return;
        }

        var currentMapId = _mapManager!.CurrentMap?.Id ?? "";
        if (!string.Equals(currentMapId, house.MapId, StringComparison.OrdinalIgnoreCase))
        {
            // Карта дома сейчас не загружена — рождение «вне зоны».
            // API лёгких снапшотов уже есть; автоматическая интеграция off-map birth остаётся в P6.5.
            return;
        }
        var spawnPos = ResolveSpawnPosition(house, mother, father);

        var child = _factory!.CreateFromPrototype(proto, spawnPos);
        if (child == null)
            return;

        child.AddComponent(new SaveEntityIdComponent());
        var childMarker = child.GetComponent<SaveEntityIdComponent>()!;

        var identity = child.GetComponent<IdentityComponent>() ?? child.AddComponent(new IdentityComponent());
        identity.FirstName = firstName;
        identity.LastName = lastName;
        identity.Gender = childGender;
        identity.FactionId = motherIdentity?.FactionId ?? fatherIdentity?.FactionId ?? "";
        identity.SettlementId = motherIdentity?.SettlementId ?? fatherIdentity?.SettlementId ?? "";
        identity.DistrictId = motherIdentity?.DistrictId ?? fatherIdentity?.DistrictId ?? "";

        var age = child.GetComponent<AgeComponent>() ?? child.AddComponent(new AgeComponent());
        age.BirthDayIndex = today;
        age.InitialAgeYears = 0;
        age.Years = 0;
        age.IsPensioner = false;

        // Расписание ребёнка.
        var schedule = child.GetComponent<ScheduleComponent>() ?? child.AddComponent(new ScheduleComponent());
        schedule.TemplateId = ChildScheduleTemplateId;
        schedule.Slots.Clear();
        schedule.Freetime.Clear();

        // Прописка в детскую кровать.
        var residenceComp = child.GetComponent<ResidenceComponent>() ?? child.AddComponent(new ResidenceComponent());
        residenceComp.HouseId = house.Id;
        residenceComp.BedSlotId = PickFreeChildBedSlot(house);
        house.ResidentNpcSaveIds.Add(childMarker.SaveId);

        // Семейные связи.
        ApplyKinLinks(child, mother, father, childMarker.SaveId);

        // Целевые скиллы.
        var growth = child.GetComponent<ChildGrowthComponent>() ?? child.AddComponent(new ChildGrowthComponent());
        growth.FatherSaveId = father.SaveId;
        growth.MotherSaveId = mother.SaveId;
        growth.TargetSkills = ComputeTargetSkills(mother, father);

        // Скиллы изначально 0 (пересчитает ChildGrowthSystem).
        var skills = child.GetComponent<SkillsComponent>() ?? child.AddComponent(new SkillsComponent());
        skills.Values.Clear();
        foreach (var (k, _) in growth.TargetSkills)
            skills.Set(k, 0f);

        var personality = child.GetComponent<PersonalityComponent>();
        personality?.RollMissing(_rng);

        // Подменим базовый спрайт под пол ребёнка.
        child.GetComponent<GenderedAppearanceComponent>()?.ApplyForGender(childGender);
        ApplyInheritedHair(child, childGender, mother.Entity, father.Entity);

        // Имя сущности — для интеракций.
        child.Name = string.IsNullOrWhiteSpace(identity.FullName) ? "Ребёнок" : identity.FullName;
        var interactable = child.GetComponent<InteractableComponent>();
        if (interactable != null) interactable.DisplayName = identity.FullName;

        Console.WriteLine($"[BirthSystem] Born: {identity.FullName} ({childGender}) in house '{house.Id}'.");
        _bus.Publish(new NpcBorn(childMarker.SaveId, father.SaveId, mother.SaveId, house.Id, today));
    }

    private Vector2 ResolveSpawnPosition(HouseDef house, Entry mother, Entry father)
    {
        var ts = _mapManager!.CurrentMap?.TileSize ?? 32;
        var motherTransform = mother.Entity.GetComponent<TransformComponent>();
        if (motherTransform != null)
            return motherTransform.Position;
        var fatherTransform = father.Entity.GetComponent<TransformComponent>();
        if (fatherTransform != null)
            return fatherTransform.Position;
        var tile = house.Tiles.FirstOrDefault();
        return tile == null
            ? Vector2.Zero
            : new Vector2((tile.X + 0.5f) * ts, (tile.Y + 0.5f) * ts);
    }

    private string PickFreeChildBedSlot(HouseDef house)
    {
        if (house.ChildBedSlots.Count == 0)
            return "";

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

        var free = house.GetFreeChildBedSlot(occupied);
        return free?.Id ?? house.ChildBedSlots[0].Id;
    }

    private static void ApplyKinLinks(Entity child, Entry mother, Entry father, string childSaveId)
    {
        var childKin = child.GetComponent<KinComponent>() ?? child.AddComponent(new KinComponent());
        childKin.Add(mother.SaveId, KinKind.Mother);
        childKin.Add(father.SaveId, KinKind.Father);

        var motherKin = mother.Entity.GetComponent<KinComponent>() ?? mother.Entity.AddComponent(new KinComponent());
        motherKin.Add(childSaveId, KinKind.Child);

        var fatherKin = father.Entity.GetComponent<KinComponent>() ?? father.Entity.AddComponent(new KinComponent());
        fatherKin.Add(childSaveId, KinKind.Child);

        // Сиблинги по матери — пересечение ребёнок ↔ существующие дети матери.
        foreach (var existing in motherKin.OfKind(KinKind.Child))
        {
            if (string.Equals(existing.NpcSaveId, childSaveId, StringComparison.OrdinalIgnoreCase))
                continue;
            childKin.Add(existing.NpcSaveId, KinKind.Sibling);
        }
    }

    private static Dictionary<string, float> ComputeTargetSkills(Entry mother, Entry father)
    {
        var motherSkills = mother.Entity.GetComponent<SkillsComponent>();
        var fatherSkills = father.Entity.GetComponent<SkillsComponent>();
        var result = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        var motherBest = motherSkills?.Best()?.Id ?? "";
        var fatherBest = fatherSkills?.Best()?.Id ?? "";

        var allSkillIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (motherSkills != null) foreach (var k in motherSkills.Values.Keys) allSkillIds.Add(k);
        if (fatherSkills != null) foreach (var k in fatherSkills.Values.Keys) allSkillIds.Add(k);

        foreach (var skill in allSkillIds)
        {
            var m = motherSkills?.Get(skill) ?? 0f;
            var f = fatherSkills?.Get(skill) ?? 0f;
            var avg = (m + f) * 0.5f;
            var max = MathF.Max(m, f);
            var isBest = string.Equals(skill, motherBest, StringComparison.OrdinalIgnoreCase)
                         || string.Equals(skill, fatherBest, StringComparison.OrdinalIgnoreCase);
            result[skill] = isBest ? max * 0.8f : avg;
        }

        return result;
    }

    private void ApplyInheritedHair(Entity child, Gender childGender, Entity mother, Entity father)
    {
        var hair = child.GetComponent<HairAppearanceComponent>() ?? child.AddComponent(new HairAppearanceComponent());
        var pickedStyle = PickRandomHairStyleId(childGender);
        if (!string.IsNullOrWhiteSpace(pickedStyle))
            hair.StyleId = pickedStyle;

        hair.ColorHex = PickParentHairColor(mother, father, hair.ColorHex);
    }

    private string PickRandomHairStyleId(Gender gender)
    {
        if (_prototypes == null)
            return "";

        var styles = _prototypes.GetAllEntities()
            .Where(proto => HairAppearanceComponent.IsHairStylePrototype(proto, gender))
            .Select(proto => proto.Id)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return styles.Count == 0 ? "" : styles[_rng.Next(styles.Count)];
    }

    private string PickParentHairColor(Entity mother, Entity father, string fallback)
    {
        var colors = new[]
            {
                mother.GetComponent<HairAppearanceComponent>()?.ColorHex,
                father.GetComponent<HairAppearanceComponent>()?.ColorHex
            }
            .Where(color => !string.IsNullOrWhiteSpace(color))
            .ToList();

        return colors.Count == 0 ? fallback : colors[_rng.Next(colors.Count)]!;
    }

    private static void HalveChildWish(Entry entry)
    {
        var personality = entry.Entity.GetComponent<PersonalityComponent>();
        if (personality != null)
            personality.ChildWish = Math.Max(0, personality.ChildWish / 2);
    }

    private static (Entry Mother, Entry Father) ResolveParents(Entry a, Entry b)
    {
        var aGender = a.Entity.GetComponent<IdentityComponent>()?.Gender;
        if (aGender == Gender.Female) return (a, b);
        return (b, a);
    }

    private List<Entry> CollectEntries()
    {
        var list = new List<Entry>();
        foreach (var entity in World.GetEntitiesWith<NpcTagComponent, RelationshipsComponent>())
        {
            if (!entity.Active) continue;
            if (!NpcLod.IsActiveOrBackground(entity)) continue;
            var marker = entity.GetComponent<SaveEntityIdComponent>();
            if (marker == null || string.IsNullOrWhiteSpace(marker.SaveId)) continue;
            list.Add(new Entry
            {
                Entity = entity,
                SaveId = marker.SaveId,
                Relationships = entity.GetComponent<RelationshipsComponent>()!
            });
        }
        return list;
    }

    private sealed class Entry
    {
        public required Entity Entity { get; init; }
        public required string SaveId { get; init; }
        public required RelationshipsComponent Relationships { get; init; }
    }
}
