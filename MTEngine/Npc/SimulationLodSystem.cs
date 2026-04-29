using MTEngine.Core;
using MTEngine.Components;
using MTEngine.ECS;
using MTEngine.World;

namespace MTEngine.Npc;

public enum SimulationLodZone
{
    Active = 0,
    Background = 1,
    Distant = 2
}

public class SimulationLodSystem : GameSystem
{
    private MapManager? _mapManager;
    private LocationGraph? _locationGraph;
    private WorldRegistry? _registry;

    public override void OnInitialize()
    {
        ResolveServices();
    }

    public override void Update(float deltaTime) { }

    public SimulationLodZone GetZone(Entity npc)
        => GetZone(ResolveNpcMapId(npc));

    public bool IsWithin(Entity npc, SimulationLodZone maxZone)
        => GetZone(npc) <= maxZone;

    public SimulationLodZone GetZone(NpcSnapshot snapshot)
        => GetZone(snapshot.MapId);

    public bool IsWithin(NpcSnapshot snapshot, SimulationLodZone maxZone)
        => GetZone(snapshot) <= maxZone;

    public SimulationLodZone GetZone(string? npcMapId)
    {
        ResolveServices();

        var playerMapId = _mapManager?.CurrentMap?.Id;
        if (string.IsNullOrWhiteSpace(playerMapId) || string.IsNullOrWhiteSpace(npcMapId))
            return SimulationLodZone.Distant;

        var distance = _locationGraph?.Distance(playerMapId, npcMapId) ?? int.MaxValue;
        if (distance == 0)
            return SimulationLodZone.Active;

        return distance <= 2 ? SimulationLodZone.Background : SimulationLodZone.Distant;
    }

    public int GetDistanceFromPlayerMap(string? npcMapId)
    {
        ResolveServices();

        var playerMapId = _mapManager?.CurrentMap?.Id;
        if (string.IsNullOrWhiteSpace(playerMapId) || string.IsNullOrWhiteSpace(npcMapId))
            return int.MaxValue;

        return _locationGraph?.Distance(playerMapId, npcMapId) ?? int.MaxValue;
    }

    public string ResolveNpcMapId(Entity npc)
    {
        ResolveServices();

        if (npc.Active && npc.HasComponent<TransformComponent>() && !string.IsNullOrWhiteSpace(_mapManager?.CurrentMap?.Id))
            return _mapManager!.CurrentMap!.Id;

        var intentMap = npc.GetComponent<NpcIntentComponent>()?.TargetMapId;
        if (!string.IsNullOrWhiteSpace(intentMap))
            return intentMap!;

        var residence = npc.GetComponent<ResidenceComponent>();
        if (!string.IsNullOrWhiteSpace(residence?.HouseId)
            && _registry?.Houses.TryGetValue(residence.HouseId, out var house) == true
            && !string.IsNullOrWhiteSpace(house.MapId))
        {
            return house.MapId;
        }

        var profession = npc.GetComponent<ProfessionComponent>();
        if (!string.IsNullOrWhiteSpace(profession?.SlotId)
            && _registry?.Professions.TryGetValue(profession.SlotId, out var slot) == true
            && !string.IsNullOrWhiteSpace(slot.MapId))
        {
            return slot.MapId;
        }

        var identity = npc.GetComponent<IdentityComponent>();
        if (!string.IsNullOrWhiteSpace(identity?.SettlementId)
            && _registry?.Settlements.TryGetValue(identity.SettlementId, out var settlement) == true)
        {
            foreach (var districtId in settlement.DistrictIds)
            {
                if (_registry.Districts.TryGetValue(districtId, out var district)
                    && !string.IsNullOrWhiteSpace(district.MapId))
                {
                    return district.MapId;
                }
            }
        }

        return "";
    }

    private void ResolveServices()
    {
        _mapManager ??= ServiceLocator.Has<MapManager>() ? ServiceLocator.Get<MapManager>() : null;
        _locationGraph ??= ServiceLocator.Has<LocationGraph>() ? ServiceLocator.Get<LocationGraph>() : null;
        _registry ??= ServiceLocator.Has<WorldRegistry>() ? ServiceLocator.Get<WorldRegistry>() : null;
    }
}
