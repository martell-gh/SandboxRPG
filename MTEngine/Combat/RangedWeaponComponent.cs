using MTEngine.ECS;
using MTEngine.Wounds;

namespace MTEngine.Combat;

public enum RangedWeaponKind
{
    Bow,
    Crossbow
}

[RegisterComponent("rangedWeapon")]
public class RangedWeaponComponent : Component
{
    [DataField("kind")]
    [SaveField("kind")]
    public RangedWeaponKind Kind { get; set; } = RangedWeaponKind.Bow;

    [DataField("projectile")]
    [SaveField("projectile")]
    public string ProjectilePrototypeId { get; set; } = "arrow_projectile";

    [DataField("damageType")]
    [SaveField("damageType")]
    public DamageType DamageType { get; set; } = DamageType.Slash;

    [DataField("minDamage")]
    [SaveField("minDamage")]
    public float MinDamage { get; set; } = 8f;

    [DataField("maxDamage")]
    [SaveField("maxDamage")]
    public float MaxDamage { get; set; } = 12f;

    [DataField("projectileSpeed")]
    [SaveField("projectileSpeed")]
    public float ProjectileSpeed { get; set; } = 420f;

    [DataField("maxRange")]
    [SaveField("maxRange")]
    public float MaxRange { get; set; } = 520f;

    [DataField("cooldown")]
    [SaveField("cooldown")]
    public float Cooldown { get; set; } = 0.42f;

    [DataField("accuracy")]
    [SaveField("accuracy")]
    public float Accuracy { get; set; } = 0.82f;

    [DataField("spreadDegrees")]
    [SaveField("spreadDegrees")]
    public float SpreadDegrees { get; set; } = 8f;

    [DataField("bowDrawSeconds")]
    [SaveField("bowDrawSeconds")]
    public float BowDrawSeconds { get; set; } = 0.9f;

    [DataField("bowMinPower")]
    [SaveField("bowMinPower")]
    public float BowMinPower { get; set; } = 0.34f;

    [DataField("crossbowReloadSeconds")]
    [SaveField("crossbowReloadSeconds")]
    public float CrossbowReloadSeconds { get; set; } = 0.5f;
}
