using MTEngine.Core;
using MTEngine.ECS;

namespace MTEngine.Npc;

public static class NpcLod
{
    public static bool IsWithin(Entity npc, SimulationLodZone maxZone)
    {
        if (!ServiceLocator.Has<SimulationLodSystem>())
            return true;

        return ServiceLocator.Get<SimulationLodSystem>().GetZone(npc) <= maxZone;
    }

    public static bool IsActive(Entity npc)
        => IsWithin(npc, SimulationLodZone.Active);

    public static bool IsActiveOrBackground(Entity npc)
        => IsWithin(npc, SimulationLodZone.Background);
}
