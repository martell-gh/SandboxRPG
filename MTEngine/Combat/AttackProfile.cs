using MTEngine.Wounds;

namespace MTEngine.Combat;

public readonly record struct AttackProfile(
    DamageType DamageType,
    float MinDamage,
    float MaxDamage,
    float Range,
    float Windup,
    float Accuracy,
    string Verb,
    string SourceName,
    SkillType AttackSkill,
    bool IsUnarmed = false);
