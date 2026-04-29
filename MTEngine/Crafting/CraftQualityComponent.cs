using MTEngine.ECS;

namespace MTEngine.Crafting;

[RegisterComponent("craftQuality")]
public class CraftQualityComponent : Component
{
    [DataField("value")]
    [SaveField("value")]
    public float Value { get; set; } = 1f;

    [DataField("label")]
    [SaveField("label")]
    public string Label { get; set; } = "Normal";
}
