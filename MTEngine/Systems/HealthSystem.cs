using Microsoft.Xna.Framework;
using MTEngine.Components;
using MTEngine.ECS;

namespace MTEngine.Systems;

public class HealthSystem : GameSystem
{
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
            }

            if (health.IsDead)
                ApplyDeathState(entity, health);
        }
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
}
