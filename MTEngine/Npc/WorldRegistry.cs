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
    /// </summary>
    public void RebuildFromMaps(MapManager mapManager)
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
            IngestMap(map);
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

    private void IngestMap(MapData map)
    {
        foreach (var area in map.Areas)
        {
            switch (area.Kind)
            {
                case AreaZoneKinds.Settlement:
                    EnsureSettlement(area, map.Id);
                    break;
                case AreaZoneKinds.District:
                    EnsureDistrict(area, map.Id);
                    break;
                case AreaZoneKinds.House:
                    EnsureHouse(area, map.Id);
                    break;
                case AreaZoneKinds.Profession:
                    EnsureProfession(area, map.Id);
                    break;
                // school/inn/tavern/orphanage/wander пока не материализуем как Def —
                // их находят системы напрямую через AreaZoneData по тегу района/поселения.
            }
        }
    }

    private void EnsureSettlement(AreaZoneData area, string mapId)
    {
        var factionId = area.Properties.GetValueOrDefault("factionId", "");
        if (!Settlements.TryGetValue(area.Id, out var settlement))
        {
            settlement = new SettlementDef { Id = area.Id, Name = area.Properties.GetValueOrDefault("name", area.Id) };
            Settlements[area.Id] = settlement;
        }
        if (!string.IsNullOrEmpty(factionId))
        {
            settlement.FactionId = factionId;
            if (!Factions.TryGetValue(factionId, out var faction))
            {
                faction = new FactionDef { Id = factionId, Name = factionId };
                Factions[factionId] = faction;
            }
            if (!faction.SettlementIds.Contains(area.Id)) faction.SettlementIds.Add(area.Id);
        }
    }

    private void EnsureDistrict(AreaZoneData area, string mapId)
    {
        var settlementId = area.Properties.GetValueOrDefault("settlementId", "");
        var district = new DistrictDef
        {
            Id = area.Id,
            Name = area.Properties.GetValueOrDefault("name", area.Id),
            SettlementId = settlementId,
            MapId = mapId,
            AreaId = area.Id
        };
        Districts[area.Id] = district;
        if (!string.IsNullOrEmpty(settlementId) && Settlements.TryGetValue(settlementId, out var s)
            && !s.DistrictIds.Contains(area.Id))
            s.DistrictIds.Add(area.Id);
    }

    private void EnsureHouse(AreaZoneData area, string mapId)
    {
        var house = new HouseDef
        {
            Id = area.Id,
            MapId = mapId,
            DistrictId = area.Properties.GetValueOrDefault("districtId", ""),
            SettlementId = area.Properties.GetValueOrDefault("settlementId", ""),
            FactionId = area.Properties.GetValueOrDefault("factionId", ""),
            Tiles = area.Tiles.ToList(),
            BedSlots = area.GetPointsByPrefix("bed_slot").ToList(),
            ChildBedSlots = area.GetPointsByPrefix("child_bed").ToList()
        };
        Houses[area.Id] = house;
    }

    private void EnsureProfession(AreaZoneData area, string mapId)
    {
        var slot = new ProfessionSlotDef
        {
            Id = area.Id,
            ProfessionId = area.Properties.GetValueOrDefault("professionId", ""),
            DistrictId = area.Properties.GetValueOrDefault("districtId", ""),
            SettlementId = area.Properties.GetValueOrDefault("settlementId", ""),
            MapId = mapId,
            WorkAnchor = area.GetPoint("work_anchor")
        };
        Professions[area.Id] = slot;
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
