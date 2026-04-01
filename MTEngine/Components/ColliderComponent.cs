using Microsoft.Xna.Framework;
using MTEngine.ECS;

namespace MTEngine.Components;

public class ColliderComponent : Component
{
    // размер хитбокса в пикселях
    public int Width { get; set; } = 12;
    public int Height { get; set; } = 12;

    // смещение от позиции entity (центрирование)
    public Vector2 Offset { get; set; } = new Vector2(2, 2);

    public Rectangle GetBounds(Vector2 position)
    {
        return new Rectangle(
            (int)(position.X + Offset.X),
            (int)(position.Y + Offset.Y),
            Width,
            Height
        );
    }
}