using MTEngine.ECS;

namespace MTEngine.Core;

/// <summary>
/// Причина смерти. Расширяется по мере появления новых источников урона.
/// </summary>
public enum DeathCause
{
    Unknown,
    Combat,
    Bleeding,
    OldAge,
    Starvation,
    Dehydration,
    Drowning,
    Fire,
    Poison
}

/// <summary>
/// Опубликовано в EventBus, когда entity с HealthComponent умирает (Health 0 в первый раз).
/// На него подписываются: система мести, журнал, освобождение профессий, дома.
/// </summary>
public readonly struct EntityDied
{
    public Entity Victim { get; }
    public DeathCause Cause { get; }
    public Entity? Killer { get; }
    public long DayIndex { get; }

    public EntityDied(Entity victim, DeathCause cause, Entity? killer, long dayIndex)
    {
        Victim = victim;
        Cause = cause;
        Killer = killer;
        DayIndex = dayIndex;
    }
}
