using Microsoft.Xna.Framework;
using MTEngine.ECS;

namespace MTEngine.Components;

[RegisterComponent("collider")]
public class ColliderComponent : Component
{
    [DataField("width")]
    [SaveField("width")]
    public int Width { get; set; } = 12;

    [DataField("height")]
    [SaveField("height")]
    public int Height { get; set; } = 12;

    public Vector2 Offset { get; set; } = new Vector2(2, 2);

    [DataField("offsetX")]
    [SaveField("offsetX")]
    public float OffsetX
    {
        get => Offset.X;
        set => Offset = new Vector2(value, Offset.Y);
    }

    [DataField("offsetY")]
    [SaveField("offsetY")]
    public float OffsetY
    {
        get => Offset.Y;
        set => Offset = new Vector2(Offset.X, value);
    }

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
