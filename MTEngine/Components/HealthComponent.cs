using MTEngine.ECS;

namespace MTEngine.Components;

[RegisterComponent("health")]
public class HealthComponent : Component
{
    [SaveField("health")]
    [DataField("health")]
    public float Health { get; set; } = 100f;

    [SaveField("maxHealth")]
    [DataField("maxHealth")]
    public float MaxHealth { get; set; } = 100f;

    [SaveField]
    public bool IsDead { get; set; }

    [SaveField]
    public bool DeathPoseApplied { get; set; }
}
