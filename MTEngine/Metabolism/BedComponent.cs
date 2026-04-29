using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Interactions;
using MTEngine.Systems;

namespace MTEngine.Metabolism;

public class BedSlot
{
    public string Id { get; set; } = "";
    public string OwnerSaveId { get; set; } = "";
    public string OccupantSaveId { get; set; } = "";

    /// <summary>Sleep point offset in world pixels from the bed entity center.</summary>
    public float LieOffsetX { get; set; }
    public float LieOffsetY { get; set; }
    public float LieRotation { get; set; }

    public bool IsFree => string.IsNullOrWhiteSpace(OccupantSaveId);
}

[RegisterComponent("bed")]
public class BedComponent : Component, IInteractionSource
{
    private float _sleepOffsetX;
    private float _sleepOffsetY;

    [DataField("name")]
    [SaveField("name")]
    public string BedName { get; set; } = "Bed";

    [DataField("wakeHour")]
    [SaveField("wakeHour")]
    public float WakeHour { get; set; } = 6f;

    [DataField("sleepTimeScale")]
    [SaveField("sleepTimeScale")]
    public float SleepTimeScale { get; set; } = 2400f;

    [DataField("healingPerSecond")]
    [SaveField("healingPerSecond")]
    public float HealingPerSecond { get; set; } = 0.6f;

    /// <summary>
    /// Optional per-bed sleep slots. Use two entries for double beds.
    /// The serialized key is "slots"; the property name avoids the generic save skip for equipment slots.
    /// </summary>
    [DataField("slots")]
    [SaveField("slots")]
    public List<BedSlot> SleepSlots { get; set; } = new();

    [DataField("ownerSaveId")]
    [SaveField("ownerSaveId")]
    public string OwnerSaveId { get; set; } = "";

    /// <summary>
    /// Sleep point offset in world pixels from the bed entity center.
    /// Zero means the center of the bed.
    /// </summary>
    [DataField("sleepOffsetX")]
    public float SleepOffsetX
    {
        get => _sleepOffsetX;
        set => _sleepOffsetX = value;
    }

    [DataField("sleepOffsetY")]
    public float SleepOffsetY
    {
        get => _sleepOffsetY;
        set => _sleepOffsetY = value;
    }

    [DataField("lieOffsetX")]
    [SaveField("lieOffsetX")]
    public float LieOffsetX
    {
        get => _sleepOffsetX;
        set => _sleepOffsetX = value;
    }

    [DataField("lieOffsetY")]
    [SaveField("lieOffsetY")]
    public float LieOffsetY
    {
        get => _sleepOffsetY;
        set => _sleepOffsetY = value;
    }

    [DataField("lieRotation")]
    [SaveField("lieRotation")]
    public float LieRotation { get; set; }

    public IEnumerable<BedSlot> GetEffectiveSlots()
    {
        if (SleepSlots.Count > 0)
            return SleepSlots;

        return new[]
        {
            new BedSlot
            {
                Id = "slot_1",
                OwnerSaveId = OwnerSaveId,
                LieOffsetX = LieOffsetX,
                LieOffsetY = LieOffsetY,
                LieRotation = LieRotation
            }
        };
    }

    public BedSlot? FindFreeSlot(string actorSaveId = "")
        => GetEffectiveSlots().FirstOrDefault(slot =>
            slot.IsFree
            && (string.IsNullOrWhiteSpace(slot.OwnerSaveId)
                || string.Equals(slot.OwnerSaveId, actorSaveId, StringComparison.OrdinalIgnoreCase)));

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

        if (!sleepSystem.CanActorSleepInBed(ctx.Actor, Owner, this, out _))
            yield break;

        yield return new InteractionEntry
        {
            Id = "bed.sleepUntilMorning",
            Label = $"Лечь спать до {WakeHour:00}:00 ({LocalizationManager.T(BedName)})",
            Priority = 18,
            Execute = c => sleepSystem.TryStartSleep(c.Actor, Owner, this)
        };
    }
}
