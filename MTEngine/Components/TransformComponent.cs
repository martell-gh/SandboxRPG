using Microsoft.Xna.Framework;
using MTEngine.ECS;

namespace MTEngine.Components;

public class TransformComponent : Component
{
    // Позиция в мировых координатах (в пикселях)
    public Vector2 Position { get; set; }
    public float Rotation { get; set; } = 0f;
    public Vector2 Scale { get; set; } = Vector2.One;

    public TransformComponent(Vector2 position)
    {
        Position = position;
    }

    public TransformComponent(float x, float y)
    {
        Position = new Vector2(x, y);
    }
}