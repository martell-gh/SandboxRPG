using System.Collections.Generic;
using MTEngine.ECS;

namespace MTEngine.Combat;

[RegisterComponent("weapon")]
public class WeaponComponent : Component
{
    [DataField("materials")]
    [SaveField("materials")]
    public List<string> Materials { get; set; } = new();

    [DataField("damageType")]
    [SaveField("damageType")]
    public Wounds.DamageType DamageType { get; set; } = Wounds.DamageType.Blunt;

    [DataField("damage")]
    [SaveField("damage")]
    public float Damage { get; set; } = 12f;

    [DataField("minDamage")]
    [SaveField("minDamage")]
    public float MinDamage { get; set; }

    [DataField("maxDamage")]
    [SaveField("maxDamage")]
    public float MaxDamage { get; set; }

    [DataField("range")]
    [SaveField("range")]
    public float Range { get; set; } = 52f;

    [DataField("windup")]
    [SaveField("windup")]
    public float Windup { get; set; } = 0.45f;

    [DataField("accuracy")]
    [SaveField("accuracy")]
    public float Accuracy { get; set; } = 0.92f;

    [DataField("verb")]
    [SaveField("verb")]
    public string AttackVerb { get; set; } = "Ударить";

    [DataField("blockBonus")]
    [SaveField("blockBonus")]
    public float BlockBonus { get; set; } = 0f;

    public float EffectiveMinDamage => MinDamage > 0f ? MinDamage : Damage;
    public float EffectiveMaxDamage => MaxDamage > 0f ? MaxDamage : EffectiveMinDamage;
}
