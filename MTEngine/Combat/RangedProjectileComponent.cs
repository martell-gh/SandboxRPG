using Microsoft.Xna.Framework;
using MTEngine.ECS;
using MTEngine.Wounds;

namespace MTEngine.Combat;

[RegisterComponent("rangedProjectile")]
public class RangedProjectileComponent : Component
{
    public int ShooterEntityId { get; set; }
    public Vector2 Velocity { get; set; }
    public float RemainingRange { get; set; }
    public float Damage { get; set; }
    public DamageType DamageType { get; set; } = DamageType.Slash;
    public bool Stuck { get; set; }
    public float StuckSeconds { get; set; }
    public float StuckLifetime { get; set; } = 5f;
    public float TravelledDistance { get; set; }
    public string SourceName { get; set; } = "Стрела";
}
