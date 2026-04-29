using MTEngine.ECS;

namespace MTEngine.Components;

[RegisterComponent("blocker")]
public class BlockerComponent : Component
{
    [DataField("blocksMovement")]
    [SaveField("blocksMovement")]
    public bool BlocksMovement { get; set; } = true;

    [DataField("blocksVision")]
    [SaveField("blocksVision")]
    public bool BlocksVision { get; set; } = true;
}
