using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;

namespace MTEngine.Npc;

public static class MerchantWorkRules
{
    public static bool IsTradeOpenNow(Entity merchant)
    {
        if (!merchant.HasComponent<NpcTagComponent>())
            return true;

        if (merchant.GetComponent<HealthComponent>()?.IsDead == true)
            return false;

        var schedule = merchant.GetComponent<ScheduleComponent>();
        if (schedule == null || !ServiceLocator.Has<GameClock>())
            return true;

        var clock = ServiceLocator.Get<GameClock>();
        var action = merchant.GetComponent<NpcIntentComponent>()?.Action
                     ?? schedule.FindSlot(clock.HourInt)?.Action
                     ?? ScheduleAction.Free;

        return action == ScheduleAction.Work
               || (IsInnkeeper(merchant) && action == ScheduleAction.StayInTavern);
    }

    public static bool IsInnkeeper(Entity entity)
        => string.Equals(entity.GetComponent<ProfessionComponent>()?.ProfessionId, "innkeeper", StringComparison.OrdinalIgnoreCase);
}
