using MTEngine.ECS;
using MTEngine.Wounds;

namespace MTEngine.Combat;

/// <summary>
/// Опубликовано в EventBus, когда CombatSystem нанёс урон цели (после применения брони/блока).
/// Используется AI для триггера ответных реакций (см. NpcCombatReactionSystem).
/// </summary>
public readonly record struct EntityDamagedEvent(
    Entity Attacker,
    Entity Target,
    float Damage,
    DamageType DamageType,
    bool TargetIsDead)
{
    public bool IsWeaponAttack { get; init; }
    public bool IsRangedAttack { get; init; }
}
