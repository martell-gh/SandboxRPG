using MTEngine.ECS;

namespace MTEngine.Components;

[RegisterComponent("health")]
public class HealthComponent : Component
{
    [DataField("health")]
    public float Health { get; set; } = 100f;

    [DataField("maxHealth")]
    public float MaxHealth { get; set; } = 100f;

    public bool IsDead { get; set; }
    public bool DeathPoseApplied { get; set; }
}
