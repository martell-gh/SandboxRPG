using Microsoft.Xna.Framework;
using MTEngine.ECS;

namespace MTEngine.Components;

[RegisterComponent("transform")]
public class TransformComponent : Component
{
    [SaveField("position")]
    public Vector2 Position { get; set; }

    [SaveField("rotation")]
    public float Rotation { get; set; } = 0f;

    [SaveField("scale")]
    public Vector2 Scale { get; set; } = Vector2.One;

    [DataField("x")]
    public float X
    {
        get => Position.X;
        set => Position = new Vector2(value, Position.Y);
    }

    [DataField("y")]
    public float Y
    {
        get => Position.Y;
        set => Position = new Vector2(Position.X, value);
    }

    [DataField("rotation")]
    public float ProtoRotation
    {
        get => Rotation;
        set => Rotation = value;
    }

    [DataField("scaleX")]
    public float ScaleX
    {
        get => Scale.X;
        set => Scale = new Vector2(value, Scale.Y);
    }

    [DataField("scaleY")]
    public float ScaleY
    {
        get => Scale.Y;
        set => Scale = new Vector2(Scale.X, value);
    }

    public TransformComponent() { }

    public TransformComponent(Vector2 position)
    {
        Position = position;
    }

    public TransformComponent(float x, float y)
    {
        Position = new Vector2(x, y);
    }
}
