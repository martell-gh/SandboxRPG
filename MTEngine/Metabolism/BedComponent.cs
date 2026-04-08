using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Interactions;
using MTEngine.Systems;

namespace MTEngine.Metabolism;

[RegisterComponent("bed")]
public class BedComponent : Component, IInteractionSource
{
    [DataField("name")]
    public string BedName { get; set; } = "Bed";

    [DataField("wakeHour")]
    public float WakeHour { get; set; } = 6f;

    [DataField("sleepTimeScale")]
    public float SleepTimeScale { get; set; } = 2400f;

    [DataField("healingPerSecond")]
    public float HealingPerSecond { get; set; } = 0.6f;

    public IEnumerable<InteractionEntry> GetInteractions(InteractionContext ctx)
    {
        if (Owner == null || ctx.Target != Owner)
            yield break;

        var actorHealth = ctx.Actor.GetComponent<Components.HealthComponent>();
        if (actorHealth?.IsDead == true)
            yield break;

        if (!ServiceLocator.Has<GameClock>())
            yield break;

        var clock = ServiceLocator.Get<GameClock>();
        if (clock.IsDay)
            yield break;

        var sleepSystem = ctx.World.GetSystem<SleepSystem>();
        if (sleepSystem == null || sleepSystem.IsSleeping(ctx.Actor))
            yield break;

        yield return new InteractionEntry
        {
            Id = "bed.sleepUntilMorning",
            Label = $"Лечь спать до {WakeHour:00}:00 ({BedName})",
            Priority = 18,
            Execute = c => sleepSystem.TryStartSleep(c.Actor, Owner, this)
        };
    }
}
