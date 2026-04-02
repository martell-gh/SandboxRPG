using Microsoft.Xna.Framework;
using MTEngine.ECS;

namespace MTEngine.Components;

[RegisterComponent("velocity")]
public class VelocityComponent : Component
{
    public Vector2 Velocity { get; set; } = Vector2.Zero;

    [DataField("speed")]
    public float Speed { get; set; } = 100f;
}