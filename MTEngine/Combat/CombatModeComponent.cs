using MTEngine.ECS;

namespace MTEngine.Combat;

[RegisterComponent("combatMode")]
public class CombatModeComponent : Component
{
    [SaveField("enabled")]
    [DataField("enabled")]
    public bool CombatEnabled { get; set; }
}
