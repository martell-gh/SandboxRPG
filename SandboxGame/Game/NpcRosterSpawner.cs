#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Xna.Framework;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Items;
using MTEngine.Npc;
using MTEngine.World;
using SandboxGame.Save;

namespace SandboxGame.Game;

/// <summary>
/// При загрузке карты подбирает соответствующий roster-файл (Maps/&lt;mapId&gt;.npc)
/// и спаунит NPC по их прописанным домам, если для этой карты ещё нет savestate.
///
/// Старый формат Maps/&lt;mapId&gt;.npcs.json поддерживается как fallback.
/// Если savestate есть — ничего не делает, NPC уже восстановлены SaveGameManager.
/// </summary>
public class NpcRosterSpawner
{
    private readonly PrototypeManager _prototypes;
    private readonly EntityFactory _factory;
    private readonly World _world;
    private readonly MapManager _mapManager;
    private readonly WorldRegistry _registry;
    private readonly Random _rng = new();
    private readonly string _rosterDirectory;
    private string _reachableSpawnMapId = "";
    private HashSet<Point>? _reachableSpawnTiles;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public NpcRosterSpawner(
        PrototypeManager prototypes,
        EntityFactory factory,
        World world,
        MapManager mapManager,
        WorldRegistry registry,
        EventBus eventBus,
        string rosterDirectory)
    {
        _prototypes = prototypes;
        _factory = factory;
        _world = world;
        _mapManager = mapManager;
        _registry = registry;
        _rosterDirectory = rosterDirectory;
        eventBus.Subscribe<MapLoadedEvent>(OnMapLoaded);
    }

    private void OnMapLoaded(MapLoadedEvent ev)
    {
        _reachableSpawnMapId = "";
        _reachableSpawnTiles = null;

        // Если savestate уже есть — NPC восстановлены SaveGameManager
        if (ServiceLocator.Has<SaveGameManager>())
        {
            var sg = ServiceLocator.Get<SaveGameManager>();
            if (sg.GetMapEntityStates(ev.Map.Id) != null) return;
        }

        var rosterPath = ResolveRosterPath(ev.Map.Id);
        if (!File.Exists(rosterPath)) return;

        List<NpcRosterEntry>? roster;
        try
        {
            roster = JsonSerializer.Deserialize<List<NpcRosterEntry>>(File.ReadAllText(rosterPath), JsonOptions);
        }
        catch (Exception e)
        {
            Console.WriteLine($"[NpcRosterSpawner] Failed to read {rosterPath}: {e.Message}");
            return;
        }
        if (roster == null) return;

        NormalizeRosterBedSlots(roster, ev.Map);

        var clock = ServiceLocator.Get<GameClock>();
        var calendar = ServiceLocator.Get<Calendar>();
        var saveIdByEntryId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 1) Сначала спауним всех — kin-ссылки заполним вторым проходом
        var spawned = new List<(NpcRosterEntry Entry, Entity Entity)>();
        foreach (var entry in roster)
        {
            var ent = SpawnOne(entry, ev.Map, clock, calendar);
            if (ent == null) continue;
            spawned.Add((entry, ent));
            var saveMarker = ent.GetComponent<SaveEntityIdComponent>();
            if (saveMarker != null) saveIdByEntryId[entry.Id] = saveMarker.SaveId;
        }

        // 2) Kin: переводим entry.id -> saveId
        foreach (var (entry, ent) in spawned)
        {
            var kin = ent.GetComponent<KinComponent>();
            if (kin == null) continue;
            foreach (var k in entry.Kin)
            {
                if (!saveIdByEntryId.TryGetValue(k.NpcId, out var sid)) continue;
                if (Enum.TryParse<KinKind>(k.Kind, true, out var kind))
                    kin.Add(sid, kind);
            }
        }
    }

    private Entity? SpawnOne(NpcRosterEntry entry, MapData map, GameClock clock, Calendar calendar)
    {
        var proto = _prototypes.GetEntity(entry.PrototypeId);
        if (proto == null)
        {
            Console.WriteLine($"[NpcRosterSpawner] Unknown proto: {entry.PrototypeId}");
            return null;
        }

        var effectiveProto = BuildEffectivePrototype(proto, entry);
        var spawnPos = ResolveSpawnPosition(entry, map);
        var entity = _factory.CreateFromPrototype(effectiveProto, spawnPos);
        if (entity == null) return null;

        entity.Name = string.IsNullOrWhiteSpace(entry.Identity.FirstName) ? "NPC" : entry.Identity.FirstName;
        entity.AddComponent(new MapEntityTagComponent());
        entity.AddComponent(new SaveEntityIdComponent());

        // identity
        var identity = entity.GetComponent<IdentityComponent>();
        if (identity != null)
        {
            identity.FirstName = entry.Identity.FirstName;
            identity.LastName = entry.Identity.LastName;
            identity.FactionId = entry.Identity.FactionId;
            identity.SettlementId = entry.Identity.SettlementId;
            identity.DistrictId = entry.Identity.DistrictId;
            if (Enum.TryParse<Gender>(entry.Identity.Gender, true, out var g))
                identity.Gender = g;

            // Fallback: если roster не указал settlement/faction явно, подтягиваем
            // из карты (LocationKind=Settlement → mapId; иначе CityId, если задан).
            // Так wilderness-карты не плодят "бомжей без settlement", и MatchmakingSystem
            // / JobMarketSystem видят их как жителей этой карты.
            if (string.IsNullOrWhiteSpace(identity.SettlementId))
                identity.SettlementId = ResolveMapSettlementFallback(map);
            if (string.IsNullOrWhiteSpace(identity.FactionId)
                && !string.IsNullOrWhiteSpace(map.FactionId))
                identity.FactionId = map.FactionId;

            // Подменить базовый спрайт под пол: man_sprite.png / woman_sprite.png.
            entity.GetComponent<GenderedAppearanceComponent>()?.ApplyForGender(identity.Gender);
        }

        // age — пересчитать BirthDayIndex от ageYears
        var age = entity.GetComponent<AgeComponent>();
        if (age != null)
        {
            age.InitialAgeYears = entry.AgeYears;
            age.BirthDayIndex = Math.Max(0L, clock.DayIndex - (long)entry.AgeYears * calendar.DaysPerYear);
            age.Years = entry.AgeYears;
        }

        // personality
        var personality = entity.GetComponent<PersonalityComponent>();
        if (personality != null)
        {
            if (entry.Personality != null)
            {
                personality.Infidelity   = entry.Personality.Infidelity;
                personality.Vengefulness = entry.Personality.Vengefulness;
                personality.ChildWish    = entry.Personality.ChildWish;
                personality.MarriageWish = entry.Personality.MarriageWish;
                personality.Sociability  = entry.Personality.Sociability;
                personality.Pacifist     = entry.Personality.Pacifist;
            }
            personality.RollMissing(_rng);
        }

        // skills
        var skills = entity.GetComponent<SkillsComponent>();
        if (skills != null)
            foreach (var (k, v) in entry.Skills) skills.Set(k, v);

        // residence
        var residence = entity.GetComponent<ResidenceComponent>();
        if (entry.Residence != null)
        {
            residence ??= entity.AddComponent(new ResidenceComponent());
            residence.HouseId = entry.Residence.HouseId;
            residence.BedSlotId = entry.Residence.BedSlotId;
            if (!string.IsNullOrEmpty(residence.HouseId)
                && _registry.Houses.TryGetValue(residence.HouseId, out var house))
            {
                if (string.IsNullOrWhiteSpace(residence.BedSlotId)
                    || house.BedSlots.All(slot => !string.Equals(slot.Id, residence.BedSlotId, StringComparison.OrdinalIgnoreCase)))
                {
                    residence.BedSlotId = NpcBedAssignment.PickFreeBedSlot(
                        house,
                        _world,
                        entity.GetComponent<SaveEntityIdComponent>()!.SaveId);
                }

                house.ResidentNpcSaveIds.Add(entity.GetComponent<SaveEntityIdComponent>()!.SaveId);
            }
        }

        // info description (из roster)
        if (!string.IsNullOrWhiteSpace(entry.Description))
        {
            var info = entity.GetComponent<InfoComponent>();
            if (info != null) info.Description = entry.Description;
        }

        // interactable display name = полное имя
        var interactable = entity.GetComponent<InteractableComponent>();
        if (interactable != null && identity != null && !string.IsNullOrWhiteSpace(identity.FullName))
            interactable.DisplayName = identity.FullName;

        // profession
        if (entry.Profession != null
            && !string.IsNullOrWhiteSpace(entry.Profession.SlotId)
            && _registry.Professions.TryGetValue(entry.Profession.SlotId, out var slot))
        {
            slot.OccupiedNpcSaveId = entity.GetComponent<SaveEntityIdComponent>()!.SaveId;
            slot.OccupiedSinceDayIndex = clock.DayIndex;
            entity.AddComponent(new ProfessionComponent
            {
                ProfessionId = slot.ProfessionId,
                SlotId = slot.Id,
                JoinedDayIndex = clock.DayIndex
            });
        }

        ApplyLoadout(entry, entity, spawnPos);

        Console.WriteLine($"[NpcRosterSpawner] Spawned NPC {entry.Id}: {identity?.FullName} @ {entry.Residence?.HouseId}");
        return entity;
    }

    private string ResolveRosterPath(string mapId)
    {
        var npcPath = Path.Combine(_rosterDirectory, $"{mapId}.npc");
        if (File.Exists(npcPath))
            return npcPath;

        return Path.Combine(_rosterDirectory, $"{mapId}.npcs.json");
    }

    private void NormalizeRosterBedSlots(List<NpcRosterEntry> roster, MapData map)
    {
        var usedByHouse = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var usedByInn = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in roster)
        {
            var residence = entry.Residence;
            if (residence == null || string.IsNullOrWhiteSpace(residence.HouseId))
            {
                TryAssignInnBed(entry, map, usedByInn);
                continue;
            }

            if (!_registry.Houses.TryGetValue(residence.HouseId, out var house) || house.BedSlots.Count == 0)
            {
                if (IsInnArea(map, residence.HouseId))
                    NormalizeInnBed(entry, map, usedByInn);
                continue;
            }

            if (!string.Equals(house.MapId, map.Id, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!usedByHouse.TryGetValue(house.Id, out var used))
            {
                used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                usedByHouse[house.Id] = used;
            }

            var validCurrent = !string.IsNullOrWhiteSpace(residence.BedSlotId)
                && house.BedSlots.Any(slot => string.Equals(slot.Id, residence.BedSlotId, StringComparison.OrdinalIgnoreCase))
                && !used.Contains(residence.BedSlotId);

            if (!validCurrent)
            {
                residence.BedSlotId = house.BedSlots
                    .OrderBy(slot => slot.Id, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault(slot => !used.Contains(slot.Id))
                    ?.Id
                    ?? house.BedSlots
                        .OrderBy(slot => slot.Id, StringComparer.OrdinalIgnoreCase)
                        .First().Id;
            }

            used.Add(residence.BedSlotId);
        }
    }

    private static bool IsInnArea(MapData map, string areaId)
        => map.Areas.Any(area =>
            string.Equals(area.Kind, AreaZoneKinds.Inn, StringComparison.OrdinalIgnoreCase)
            && string.Equals(area.Id, areaId, StringComparison.OrdinalIgnoreCase));

    private void TryAssignInnBed(
        NpcRosterEntry entry,
        MapData map,
        Dictionary<string, HashSet<string>> usedByInn)
    {
        foreach (var inn in map.Areas
                     .Where(area => string.Equals(area.Kind, AreaZoneKinds.Inn, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(area => area.Id, StringComparer.OrdinalIgnoreCase))
        {
            var slots = GetInnBedPoints(inn, map).ToList();
            if (slots.Count == 0)
                continue;

            if (!usedByInn.TryGetValue(inn.Id, out var used))
            {
                used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                usedByInn[inn.Id] = used;
            }

            var free = slots.FirstOrDefault(slot => !used.Contains(slot.Id));
            if (free == null)
                continue;

            entry.Residence = new NpcRosterResidence { HouseId = inn.Id, BedSlotId = free.Id };
            used.Add(free.Id);
            return;
        }
    }

    private void NormalizeInnBed(
        NpcRosterEntry entry,
        MapData map,
        Dictionary<string, HashSet<string>> usedByInn)
    {
        var residence = entry.Residence;
        if (residence == null)
            return;

        var inn = map.Areas.FirstOrDefault(area =>
            string.Equals(area.Kind, AreaZoneKinds.Inn, StringComparison.OrdinalIgnoreCase)
            && string.Equals(area.Id, residence.HouseId, StringComparison.OrdinalIgnoreCase));
        if (inn == null)
            return;

        var slots = GetInnBedPoints(inn, map).ToList();
        if (slots.Count == 0)
        {
            residence.BedSlotId = "";
            return;
        }

        if (!usedByInn.TryGetValue(inn.Id, out var used))
        {
            used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            usedByInn[inn.Id] = used;
        }

        var current = residence.BedSlotId.Trim();
        var keepCurrent = slots.Any(slot => string.Equals(slot.Id, current, StringComparison.OrdinalIgnoreCase))
            && !used.Contains(current);
        residence.BedSlotId = keepCurrent
            ? current
            : slots.FirstOrDefault(slot => !used.Contains(slot.Id))?.Id ?? slots[0].Id;
        used.Add(residence.BedSlotId);
    }

    private List<AreaPointData> GetInnBedPoints(AreaZoneData inn, MapData map)
    {
        return inn.GetPointsByPrefix("inn_bed_")
            .Concat(HouseBedScanner.EnumerateAutoBedPoints(inn, map, _prototypes))
            .GroupBy(point => point.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(point => point.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static EntityPrototype BuildEffectivePrototype(EntityPrototype proto, NpcRosterEntry entry)
    {
        if (entry.Components.Count == 0)
            return proto;

        var components = proto.Components?.DeepClone().AsObject() ?? new JsonObject();
        foreach (var (componentId, componentData) in entry.Components)
            components[componentId] = componentData.DeepClone();

        return new EntityPrototype
        {
            Id = proto.Id,
            Name = proto.Name,
            Category = proto.Category,
            BaseId = proto.BaseId,
            Abstract = proto.Abstract,
            Components = components,
            AnimationsPath = proto.AnimationsPath,
            SpritePath = proto.SpritePath,
            DirectoryPath = proto.DirectoryPath,
            PreviewSourceRect = proto.PreviewSourceRect,
            PreviewColor = proto.PreviewColor
        };
    }

    private void ApplyLoadout(NpcRosterEntry entry, Entity npc, Vector2 position)
    {
        var hands = npc.GetComponent<HandsComponent>();
        var equipment = npc.GetComponent<EquipmentComponent>();

        if (entry.Outfit.Count > 0 && hands != null && equipment != null)
        {
            foreach (var (slotId, itemProtoId) in entry.Outfit)
            {
                var item = CreateItem(itemProtoId, position);
                if (item == null)
                    continue;

                if (hands.TryPickUp(item))
                    equipment.TryEquipItem(hands, item, slotId);
            }
        }

        if (entry.Hands.Count > 0 && hands != null)
        {
            foreach (var itemProtoId in entry.Hands.Where(id => !string.IsNullOrWhiteSpace(id)))
            {
                var item = CreateItem(itemProtoId, position);
                if (item != null)
                    hands.TryPickUp(item);
            }
        }

        if (entry.Inventory.Count > 0)
        {
            var storage = npc.GetComponent<StorageComponent>();
            if (storage == null)
            {
                storage = npc.AddComponent(new StorageComponent
                {
                    StorageName = "Inventory",
                    MaxSlots = Math.Max(10, entry.Inventory.Count + 4)
                });
            }

            foreach (var itemProtoId in entry.Inventory.Where(id => !string.IsNullOrWhiteSpace(id)))
            {
                var item = CreateItem(itemProtoId, position);
                if (item != null)
                    storage.TryInsertInitial(item);
            }

            if (storage.Contents.Count > 0)
                storage.MarkInitialContentsResolved();
        }
    }

    private Entity? CreateItem(string prototypeId, Vector2 position)
    {
        var itemProto = _prototypes.GetEntity(prototypeId);
        if (itemProto == null)
        {
            Console.WriteLine($"[NpcRosterSpawner] Unknown item proto in NPC loadout: {prototypeId}");
            return null;
        }

        return _factory.CreateFromPrototype(itemProto, position);
    }

    private Vector2 ResolveSpawnPosition(NpcRosterEntry entry, MapData map)
    {
        // приоритет: bed_slot_id -> любая точка дома -> первый тайл дома -> spawn point default
        var ts = map.TileSize;
        if (entry.Residence != null
            && !string.IsNullOrEmpty(entry.Residence.HouseId)
            && _registry.Houses.TryGetValue(entry.Residence.HouseId, out var house))
        {
            if (!string.IsNullOrEmpty(entry.Residence.BedSlotId))
            {
                var slot = house.BedSlots.FirstOrDefault(p =>
                    string.Equals(p.Id, entry.Residence.BedSlotId, StringComparison.OrdinalIgnoreCase));
                if (slot != null) return NormalizeSpawnPosition(new Vector2((slot.X + 0.5f) * ts, (slot.Y + 0.5f) * ts), map);
            }
            var anyBed = house.BedSlots.FirstOrDefault();
            if (anyBed != null) return NormalizeSpawnPosition(new Vector2((anyBed.X + 0.5f) * ts, (anyBed.Y + 0.5f) * ts), map);
            var anyTile = house.Tiles.FirstOrDefault();
            if (anyTile != null) return NormalizeSpawnPosition(new Vector2((anyTile.X + 0.5f) * ts, (anyTile.Y + 0.5f) * ts), map);
        }
        if (entry.Residence != null && !string.IsNullOrWhiteSpace(entry.Residence.HouseId))
        {
            var inn = map.Areas.FirstOrDefault(area =>
                string.Equals(area.Kind, AreaZoneKinds.Inn, StringComparison.OrdinalIgnoreCase)
                && string.Equals(area.Id, entry.Residence.HouseId, StringComparison.OrdinalIgnoreCase));
            if (inn != null)
            {
                var slots = GetInnBedPoints(inn, map);
                var slot = slots.FirstOrDefault(point =>
                    string.Equals(point.Id, entry.Residence.BedSlotId, StringComparison.OrdinalIgnoreCase))
                    ?? slots.FirstOrDefault();
                if (slot != null)
                    return NormalizeSpawnPosition(new Vector2((slot.X + 0.5f) * ts, (slot.Y + 0.5f) * ts), map);
                var anyTile = inn.Tiles.FirstOrDefault();
                if (anyTile != null)
                    return NormalizeSpawnPosition(new Vector2((anyTile.X + 0.5f) * ts, (anyTile.Y + 0.5f) * ts), map);
            }
        }
        var spawn = map.SpawnPoints.FirstOrDefault();
        return NormalizeSpawnPosition(spawn != null ? new Vector2(spawn.X, spawn.Y) : Vector2.Zero, map);
    }

    private Vector2 NormalizeSpawnPosition(Vector2 desired, MapData map)
    {
        var tileMap = _mapManager.CurrentTileMap;
        if (tileMap == null)
            return desired;

        var desiredTile = tileMap.WorldToTile(desired);
        var reachable = GetReachableSpawnTiles(map, tileMap);
        if (IsSpawnTileValid(desiredTile, map, tileMap)
            && (reachable.Count == 0 || reachable.Contains(desiredTile)))
        {
            return desired;
        }

        var replacement = FindNearestSpawnTile(desiredTile, map, tileMap, reachable)
                          ?? FindNearestSpawnTile(desiredTile, map, tileMap, reachable: null);
        if (replacement.HasValue)
            return new Vector2((replacement.Value.X + 0.5f) * map.TileSize, (replacement.Value.Y + 0.5f) * map.TileSize);

        return desired;
    }

    private HashSet<Point> GetReachableSpawnTiles(MapData map, TileMap tileMap)
    {
        if (_reachableSpawnTiles != null
            && string.Equals(_reachableSpawnMapId, map.Id, StringComparison.OrdinalIgnoreCase))
        {
            return _reachableSpawnTiles;
        }

        _reachableSpawnMapId = map.Id;
        _reachableSpawnTiles = new HashSet<Point>();

        var spawn = map.SpawnPoints.FirstOrDefault(s => string.Equals(s.Id, "default", StringComparison.OrdinalIgnoreCase))
                    ?? map.SpawnPoints.FirstOrDefault();
        if (spawn == null)
            return _reachableSpawnTiles;

        var start = tileMap.WorldToTile(new Vector2(spawn.X, spawn.Y));
        if (!IsSpawnTileValid(start, map, tileMap))
            start = FindNearestSpawnTile(start, map, tileMap, reachable: null) ?? start;
        if (!IsSpawnTileValid(start, map, tileMap))
            return _reachableSpawnTiles;

        var queue = new Queue<Point>();
        _reachableSpawnTiles.Add(start);
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var next in AdjacentTiles(current))
            {
                if (_reachableSpawnTiles.Contains(next) || !IsSpawnTileValid(next, map, tileMap))
                    continue;

                _reachableSpawnTiles.Add(next);
                queue.Enqueue(next);
            }
        }

        return _reachableSpawnTiles;
    }

    private static Point? FindNearestSpawnTile(
        Point origin,
        MapData map,
        TileMap tileMap,
        HashSet<Point>? reachable)
    {
        const int maxRadius = 14;
        for (var radius = 0; radius <= maxRadius; radius++)
        {
            foreach (var point in EnumerateSquareRing(origin, radius))
            {
                if (!IsSpawnTileValid(point, map, tileMap))
                    continue;
                if (reachable != null && reachable.Count > 0 && !reachable.Contains(point))
                    continue;

                return point;
            }
        }

        return null;
    }

    private static bool IsSpawnTileValid(Point point, MapData map, TileMap tileMap)
        => tileMap.IsInBounds(point.X, point.Y)
           && !tileMap.IsSolid(point.X, point.Y)
           && !IsLocationTransitionTile(point, map);

    private static bool IsLocationTransitionTile(Point point, MapData map)
        => map.Triggers.Any(trigger =>
            string.Equals(trigger.Action.Type, TriggerActionTypes.LocationTransition, StringComparison.OrdinalIgnoreCase)
            && trigger.Tiles.Any(tile => tile.X == point.X && tile.Y == point.Y));

    private static IEnumerable<Point> AdjacentTiles(Point point)
    {
        yield return new Point(point.X + 1, point.Y);
        yield return new Point(point.X - 1, point.Y);
        yield return new Point(point.X, point.Y + 1);
        yield return new Point(point.X, point.Y - 1);
    }

    private static IEnumerable<Point> EnumerateSquareRing(Point center, int radius)
    {
        if (radius <= 0)
        {
            yield return center;
            yield break;
        }

        for (var y = center.Y - radius; y <= center.Y + radius; y++)
        {
            for (var x = center.X - radius; x <= center.X + radius; x++)
            {
                if (Math.Abs(x - center.X) != radius && Math.Abs(y - center.Y) != radius)
                    continue;

                yield return new Point(x, y);
            }
        }
    }

    private static string ResolveMapSettlementFallback(MapData map)
    {
        if (!string.IsNullOrWhiteSpace(map.CityId))
            return map.CityId.Trim();
        return string.Equals(map.LocationKind, LocationKinds.Settlement, StringComparison.OrdinalIgnoreCase)
            ? map.Id
            : "";
    }
}
