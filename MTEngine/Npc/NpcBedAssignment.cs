using MTEngine.ECS;
using ECSWorld = MTEngine.ECS.World;

namespace MTEngine.Npc;

public static class NpcBedAssignment
{
    public static string EnsureAssigned(Entity npc, WorldRegistry registry, ECSWorld world)
    {
        var residence = npc.GetComponent<ResidenceComponent>();
        if (residence == null || string.IsNullOrWhiteSpace(residence.HouseId))
            return "";

        if (!registry.Houses.TryGetValue(residence.HouseId, out var house) || house.BedSlots.Count == 0)
            return "";

        var current = residence.BedSlotId;
        if (!string.IsNullOrWhiteSpace(current)
            && house.BedSlots.Any(slot => string.Equals(slot.Id, current, StringComparison.OrdinalIgnoreCase)))
        {
            return current;
        }

        var saveId = npc.GetComponent<SaveEntityIdComponent>()?.SaveId ?? "";
        residence.BedSlotId = PickFreeBedSlot(house, world, saveId);
        return residence.BedSlotId;
    }

    public static string PickFreeBedSlot(HouseDef house, ECSWorld world, string selfSaveId = "")
    {
        if (house.BedSlots.Count == 0)
            return "";

        var occupied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in world.GetEntitiesWith<ResidenceComponent>())
        {
            var marker = entity.GetComponent<SaveEntityIdComponent>();
            if (!string.IsNullOrWhiteSpace(selfSaveId)
                && marker != null
                && string.Equals(marker.SaveId, selfSaveId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var residence = entity.GetComponent<ResidenceComponent>()!;
            if (string.Equals(residence.HouseId, house.Id, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(residence.BedSlotId))
            {
                occupied.Add(residence.BedSlotId);
            }
        }

        return house.BedSlots
                   .OrderBy(slot => slot.Id, StringComparer.OrdinalIgnoreCase)
                   .FirstOrDefault(slot => !occupied.Contains(slot.Id))
                   ?.Id
               ?? house.BedSlots
                   .OrderBy(slot => slot.Id, StringComparer.OrdinalIgnoreCase)
                   .First().Id;
    }
}
