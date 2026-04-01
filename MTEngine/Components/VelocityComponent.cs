using Microsoft.Xna.Framework;
using MTEngine.ECS;

namespace MTEngine.Components;

public class VelocityComponent : Component
{
    public Vector2 Velocity { get; set; } = Vector2.Zero;
    public float Speed { get; set; } = 100f; // пикселей в секунду
}