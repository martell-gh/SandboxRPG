using Microsoft.Xna.Framework;
using MTEngine.ECS;

namespace MTEngine.Components;

[RegisterComponent("velocity")]
public class VelocityComponent : Component
{
    [SaveField]
    public Vector2 Velocity { get; set; } = Vector2.Zero;

    [SaveField("speed")]
    [DataField("speed")]
    public float Speed { get; set; } = 100f;
}
