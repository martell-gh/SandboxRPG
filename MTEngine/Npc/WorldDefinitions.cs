using MTEngine.World;

namespace MTEngine.Npc;

/// <summary>
/// Фракция — верхний уровень иерархии. Пока без своих свойств кроме id/имени.
/// В будущем сюда лягут отношения между фракциями, общие имена, налоги и т.д.
/// </summary>
public class FactionDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> SettlementIds { get; set; } = new();
}

/// <summary>
/// Поселение (город или деревня). Содержит районы.
/// </summary>
public class SettlementDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string FactionId { get; set; } = "";
    public List<string> DistrictIds { get; set; } = new();
}

/// <summary>
/// Район поселения. У деревни обычно один район = "main".
/// Каждый район привязан к одной карте (mapId) и опционально к area-zone типа "district".
/// </summary>
public class DistrictDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string SettlementId { get; set; } = "";
    public string MapId { get; set; } = "";
    public string? AreaId { get; set; }
}

/// <summary>
/// Жилой дом. Не сущность на сцене, а запись в реестре —
/// бьёт по area-zone типа "house".
/// </summary>
public class HouseDef
{
    public string Id { get; set; } = "";
    public string MapId { get; set; } = "";
    public string DistrictId { get; set; } = "";
    public string SettlementId { get; set; } = "";
    public string FactionId { get; set; } = "";

    public List<TriggerTile> Tiles { get; set; } = new();

    /// <summary>Слоты двуспальной кровати (точки bed_slot_a, bed_slot_b и т.д., парами).</summary>
    public List<AreaPointData> BedSlots { get; set; } = new();

    /// <summary>Точки для детских кроватей (child_bed_*). Кол-во = максимум детей.</summary>
    public List<AreaPointData> ChildBedSlots { get; set; } = new();

    /// <summary>SaveId-ы NPC, прописанных в доме. Заполняется HouseRegistrySystem.</summary>
    public HashSet<string> ResidentNpcSaveIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool ForSale => ResidentNpcSaveIds.Count == 0;

    public AreaPointData? GetFreeChildBedSlot(IEnumerable<string> occupiedPointIds)
    {
        var occupied = new HashSet<string>(occupiedPointIds, StringComparer.OrdinalIgnoreCase);
        return ChildBedSlots.FirstOrDefault(p => !occupied.Contains(p.Id));
    }
}

/// <summary>
/// Слот профессии в реестре. Бьёт по area-zone типа "profession".
/// </summary>
public class ProfessionSlotDef
{
    public string Id { get; set; } = "";
    public string ProfessionId { get; set; } = "";
    public string SettlementId { get; set; } = "";
    public string DistrictId { get; set; } = "";
    public string MapId { get; set; } = "";
    public AreaPointData? WorkAnchor { get; set; }

    public string? OccupiedNpcSaveId { get; set; }
    public long? OccupiedSinceDayIndex { get; set; }

    public bool IsVacant => string.IsNullOrEmpty(OccupiedNpcSaveId);
}
