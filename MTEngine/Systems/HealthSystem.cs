using Microsoft.Xna.Framework;
using MTEngine.Combat;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;

namespace MTEngine.Systems;

public class HealthSystem : GameSystem
{
    private const double KillerMemoryMaxAgeSeconds = 12d * 3600d;

    private EventBus? _bus;
    private GameClock? _clock;
    private readonly Dictionary<int, DamageMemory> _lastDamagers = new();

    public override void OnInitialize()
    {
        if (ServiceLocator.Has<EventBus>())
        {
            _bus = ServiceLocator.Get<EventBus>();
            _bus.Subscribe<EntityDamagedEvent>(OnEntityDamaged);
        }

        _clock = ServiceLocator.Has<GameClock>() ? ServiceLocator.Get<GameClock>() : null;
    }

    public override void Update(float deltaTime)
    {
        foreach (var entity in World.GetEntitiesWith<HealthComponent>())
        {
            var health = entity.GetComponent<HealthComponent>()!;
            health.MaxHealth = Math.Max(1f, health.MaxHealth);
            health.Health = Math.Clamp(health.Health, 0f, health.MaxHealth);

            if (!health.IsDead && health.Health <= 0f)
            {
                health.IsDead = true;
                health.Health = 0f;

                if (entity.HasComponent<PlayerTagComponent>())
                    PopupTextSystem.Show(entity, "Ты умер.", Color.OrangeRed, lifetime: 2f);

                PublishDeath(entity);
            }

            if (health.IsDead)
                ApplyDeathState(entity, health);
        }
    }

    public override void OnDestroy()
    {
        _bus?.Unsubscribe<EntityDamagedEvent>(OnEntityDamaged);
    }

    private void OnEntityDamaged(EntityDamagedEvent ev)
    {
        if (ev.Attacker == ev.Target)
            return;

        _lastDamagers[ev.Target.Id] = new DamageMemory(
            ev.Attacker,
            _clock?.TotalSecondsAbsolute ?? 0d);
    }

    private static void ApplyDeathState(Entity entity, HealthComponent health)
    {
        var velocity = entity.GetComponent<VelocityComponent>();
        if (velocity != null)
        {
            velocity.Speed = 0f;
            velocity.Velocity = Vector2.Zero;
        }

        if (health.DeathPoseApplied)
            return;

        var transform = entity.GetComponent<TransformComponent>();
        if (transform != null)
            transform.Rotation = MathHelper.PiOver2;

        health.DeathPoseApplied = true;
    }

    private void PublishDeath(Entity entity)
    {
        if (!ServiceLocator.Has<EventBus>())
            return;

        var dayIndex = ServiceLocator.Has<GameClock>()
            ? ServiceLocator.Get<GameClock>().DayIndex
            : 0L;

        var killer = ResolveKiller(entity);
        var cause = killer != null ? DeathCause.Combat : DeathCause.Unknown;
        _lastDamagers.Remove(entity.Id);

        ServiceLocator.Get<EventBus>().Publish(new EntityDied(entity, cause, killer, dayIndex));
    }

    private Entity? ResolveKiller(Entity entity)
    {
        if (!_lastDamagers.TryGetValue(entity.Id, out var memory))
            return null;

        var now = _clock?.TotalSecondsAbsolute ?? memory.AbsoluteSeconds;
        return now - memory.AbsoluteSeconds <= KillerMemoryMaxAgeSeconds
            ? memory.Attacker
            : null;
    }

    private readonly record struct DamageMemory(Entity Attacker, double AbsoluteSeconds);
}
