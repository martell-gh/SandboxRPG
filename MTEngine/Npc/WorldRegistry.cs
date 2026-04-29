using MTEngine.Core;
using MTEngine.World;

namespace MTEngine.Npc;

/// <summary>
/// Глобальный реестр иерархии мира: фракции → поселения → районы → дома + слоты профессий.
/// Строится один раз при старте — пробегает по всем .map.json и собирает area-zones.
/// Сохраняется через [SaveField] (только динамические поля — кто живёт, кто работает).
/// Конфигурация фракций/поселений выводится из карт + опционально из Data/world.json.
/// </summary>
[SaveObject("worldRegistry")]
public class WorldRegistry
{
    public Dictionary<string, FactionDef> Factions { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, SettlementDef> Settlements { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, DistrictDef> Districts { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, HouseDef> Houses { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ProfessionSlotDef> Professions { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Динамическая часть, попадающая в save: какие NPC где живут и какие профессии заняты.
    /// При загрузке мы перестраиваем статическую часть (Factions/Settlements/Districts/Houses/Professions)
    /// из карт, а потом накладываем эти словари сверху.
    /// </summary>
    [SaveField("residency")]
    public Dictionary<string, List<string>> HouseResidency { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [SaveField("occupation")]
    public Dictionary<string, string> ProfessionOccupation { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Прочитать все карты из директории и построить статическую часть реестра.
    /// Должно вызываться один раз при старте, ДО загрузки save.
    /// Если <paramref name="prototypes"/> передан — дома также наследуют кровати-сущности,
    /// размещённые внутри их тайлов (без необходимости вручную ставить bed_slot_* точки).
    /// </summary>
    public void RebuildFromMaps(MapManager mapManager, MTEngine.Core.PrototypeManager? prototypes = null)
    {
        Factions.Clear();
        Settlements.Clear();
        Districts.Clear();
        Houses.Clear();
        Professions.Clear();

        foreach (var mapId in mapManager.GetAvailableMaps())
        {
            var map = mapManager.LoadBaseMapData(mapId);
            if (map == null) continue;
            IngestMap(map, prototypes);
        }

        // Прокидываем родительские связи фракции/поселения, если в area не указано явно — наследуем от района
        foreach (var house in Houses.Values)
        {
            if (string.IsNullOrEmpty(house.SettlementId) && Districts.TryGetValue(house.DistrictId, out var d))
                house.SettlementId = d.SettlementId;
            if (string.IsNullOrEmpty(house.FactionId) && Settlements.TryGetValue(house.SettlementId, out var s))
                house.FactionId = s.FactionId;
        }
        foreach (var prof in Professions.Values)
        {
            if (string.IsNullOrEmpty(prof.SettlementId) && Districts.TryGetValue(prof.DistrictId, out var d))
                prof.SettlementId = d.SettlementId;
        }
    }

    /// <summary>
    /// После RebuildFromMaps + ApplyClockState — перенести динамику из словарей в HouseDef/ProfessionSlotDef.
    /// </summary>
    public void RehydrateDynamicState()
    {
        foreach (var house in Houses.Values)
            house.ResidentNpcSaveIds.Clear();
        foreach (var (houseId, npcIds) in HouseResidency)
        {
            if (!Houses.TryGetValue(houseId, out var h)) continue;
            foreach (var id in npcIds) h.ResidentNpcSaveIds.Add(id);
        }
        foreach (var prof in Professions.Values)
            prof.OccupiedNpcSaveId = null;
        foreach (var (slotId, npcId) in ProfessionOccupation)
            if (Professions.TryGetValue(slotId, out var p))
                p.OccupiedNpcSaveId = npcId;
    }

    /// <summary>Перед save — записать актуальные ResidentNpcSaveIds/Occupation в словари.</summary>
    public void CaptureDynamicState()
    {
        HouseResidency.Clear();
        foreach (var house in Houses.Values)
            if (house.ResidentNpcSaveIds.Count > 0)
                HouseResidency[house.Id] = house.ResidentNpcSaveIds.ToList();

        ProfessionOccupation.Clear();
        foreach (var prof in Professions.Values)
            if (!string.IsNullOrEmpty(prof.OccupiedNpcSaveId))
                ProfessionOccupation[prof.Id] = prof.OccupiedNpcSaveId!;
    }

    public void ClearDynamicState()
    {
        HouseResidency.Clear();
        ProfessionOccupation.Clear();

        foreach (var house in Houses.Values)
            house.ResidentNpcSaveIds.Clear();
        foreach (var profession in Professions.Values)
        {
            profession.OccupiedNpcSaveId = null;
            profession.OccupiedSinceDayIndex = null;
        }
    }

    private void IngestMap(MapData map, MTEngine.Core.PrototypeManager? prototypes)
    {
        EnsureMapSettlement(map);

        foreach (var area in map.Areas)
        {
            switch (area.Kind)
            {
                case AreaZoneKinds.District:
                    EnsureDistrict(area, map);
                    break;
                case AreaZoneKinds.House:
                    EnsureHouse(area, map, prototypes);
                    break;
                case AreaZoneKinds.Profession:
                    EnsureProfession(area, map);
                    break;
                case AreaZoneKinds.Tavern when IsProfessionSlotArea(area):
                    EnsureProfession(area, map);
                    break;
                // school/inn/orphanage/wander пока не материализуем как Def —
                // их находят системы напрямую через AreaZoneData по тегу района/поселения.
            }
        }
    }

    private void EnsureMapSettlement(MapData map)
    {
        var settlementId = GetMapSettlementId(map);
        if (string.IsNullOrWhiteSpace(settlementId))
            return;

        if (!Settlements.TryGetValue(settlementId, out var settlement))
        {
            settlement = new SettlementDef
            {
                Id = settlementId,
                Name = string.IsNullOrWhiteSpace(map.Name) ? settlementId : map.Name
            };
            Settlements[settlementId] = settlement;
        }

        var factionId = FirstNonEmpty(map.FactionId);
        if (!string.IsNullOrEmpty(factionId))
        {
            settlement.FactionId = factionId;
            if (!Factions.TryGetValue(factionId, out var faction))
            {
                faction = new FactionDef { Id = factionId, Name = factionId };
                Factions[factionId] = faction;
            }
            if (!faction.SettlementIds.Contains(settlementId)) faction.SettlementIds.Add(settlementId);
        }
    }

    private void EnsureDistrict(AreaZoneData area, MapData map)
    {
        var settlementId = FirstNonEmpty(area.Properties.GetValueOrDefault("settlementId", ""), GetMapSettlementId(map));
        var district = new DistrictDef
        {
            Id = area.Id,
            Name = area.Properties.GetValueOrDefault("name", area.Id),
            SettlementId = settlementId,
            MapId = map.Id,
            AreaId = area.Id
        };
        Districts[area.Id] = district;
        if (!string.IsNullOrEmpty(settlementId) && Settlements.TryGetValue(settlementId, out var s)
            && !s.DistrictIds.Contains(area.Id))
            s.DistrictIds.Add(area.Id);
    }

    private void EnsureHouse(AreaZoneData area, MapData map, MTEngine.Core.PrototypeManager? prototypes)
    {
        var bedSlots = area.GetPointsByPrefix("bed_slot").ToList();
        if (prototypes != null)
        {
            foreach (var autoSlot in HouseBedScanner.EnumerateAutoBedPoints(area, map, prototypes))
                bedSlots.Add(autoSlot);
        }

        var house = new HouseDef
        {
            Id = area.Id,
            MapId = map.Id,
            DistrictId = area.Properties.GetValueOrDefault("districtId", ""),
            SettlementId = FirstNonEmpty(area.Properties.GetValueOrDefault("settlementId", ""), GetMapSettlementId(map)),
            FactionId = FirstNonEmpty(area.Properties.GetValueOrDefault("factionId", ""), map.FactionId),
            Tiles = area.Tiles.ToList(),
            BedSlots = bedSlots,
            ChildBedSlots = area.GetPointsByPrefix("child_bed").ToList()
        };
        Houses[area.Id] = house;
    }

    private void EnsureProfession(AreaZoneData area, MapData map)
    {
        var slot = new ProfessionSlotDef
        {
            Id = area.Id,
            ProfessionId = ResolveProfessionId(area),
            DistrictId = area.Properties.GetValueOrDefault("districtId", ""),
            SettlementId = FirstNonEmpty(area.Properties.GetValueOrDefault("settlementId", ""), GetMapSettlementId(map)),
            MapId = map.Id,
            WorkAnchor = ResolveWorkAnchor(area)
        };
        Professions[area.Id] = slot;
    }

    private static bool IsProfessionSlotArea(AreaZoneData area)
    {
        if (string.Equals(area.Kind, AreaZoneKinds.Profession, StringComparison.OrdinalIgnoreCase))
            return true;

        return string.Equals(area.Kind, AreaZoneKinds.Tavern, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveProfessionId(AreaZoneData area)
        => string.Equals(area.Kind, AreaZoneKinds.Tavern, StringComparison.OrdinalIgnoreCase)
            ? "innkeeper"
            : area.Properties.GetValueOrDefault("professionId", "");

    private static AreaPointData? ResolveWorkAnchor(AreaZoneData area)
    {
        var point = area.GetPoint("work_anchor")
                    ?? area.GetPointsByPrefix("work_").FirstOrDefault()
                    ?? area.GetPointsByPrefix("seat_").FirstOrDefault()
                    ?? area.GetPointsByPrefix("table_").FirstOrDefault()
                    ?? area.GetPointsByPrefix("eat_").FirstOrDefault()
                    ?? area.GetPointsByPrefix("wander_").FirstOrDefault();
        if (point != null)
            return point;

        var tile = PickAreaCenterTile(area);
        return tile == null
            ? null
            : new AreaPointData { Id = "work_anchor:auto", X = tile.X, Y = tile.Y };
    }

    private static TriggerTile? PickAreaCenterTile(AreaZoneData area)
    {
        if (area.Tiles.Count == 0)
            return null;

        var minX = area.Tiles.Min(tile => tile.X);
        var maxX = area.Tiles.Max(tile => tile.X);
        var minY = area.Tiles.Min(tile => tile.Y);
        var maxY = area.Tiles.Max(tile => tile.Y);
        var centerX = (minX + maxX) / 2f;
        var centerY = (minY + maxY) / 2f;

        return area.Tiles
            .OrderBy(tile => MathF.Abs(tile.X - centerX) + MathF.Abs(tile.Y - centerY))
            .ThenBy(tile => tile.Y)
            .ThenBy(tile => tile.X)
            .FirstOrDefault();
    }

    private static string GetMapSettlementId(MapData map)
    {
        if (!string.IsNullOrWhiteSpace(map.CityId))
            return map.CityId.Trim();

        return string.Equals(map.LocationKind, LocationKinds.Settlement, StringComparison.OrdinalIgnoreCase)
            ? map.Id
            : "";
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        return "";
    }

    // === Удобные query-методы ===

    public IEnumerable<HouseDef> HousesInDistrict(string districtId)
        => Houses.Values.Where(h => string.Equals(h.DistrictId, districtId, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<HouseDef> HousesInSettlement(string settlementId)
        => Houses.Values.Where(h => string.Equals(h.SettlementId, settlementId, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<HouseDef> EmptyHousesInSettlement(string settlementId)
        => HousesInSettlement(settlementId).Where(h => h.ForSale);

    public IEnumerable<ProfessionSlotDef> VacantProfessionsInSettlement(string settlementId)
        => Professions.Values.Where(p => string.Equals(p.SettlementId, settlementId, StringComparison.OrdinalIgnoreCase) && p.IsVacant);

    public HouseDef? FindHouseByMapAndTile(string mapId, int x, int y)
        => Houses.Values.FirstOrDefault(h => string.Equals(h.MapId, mapId, StringComparison.OrdinalIgnoreCase)
            && h.Tiles.Any(t => t.X == x && t.Y == y));
}
