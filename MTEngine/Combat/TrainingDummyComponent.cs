using MTEngine.ECS;

namespace MTEngine.Combat;

[RegisterComponent("trainingDummy")]
public class TrainingDummyComponent : Component
{
    [SaveField("lastDamage")]
    public float LastDamage { get; set; }
}
