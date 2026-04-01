using Microsoft.Xna.Framework;

namespace MTEngine.World;

public enum TileType
{
    Empty,
    Floor,
    Wall,
    Water,
    Grass,
    Stone,
    Wood
}

public class Tile
{
    public TileType Type { get; set; }
    public string? ProtoId { get; set; }
    public bool Solid { get; set; }
    public bool Transparent { get; set; } = true;
    public Rectangle SourceRect { get; set; }
    public float LayerDepth { get; set; } = 0f;

    public static Tile Empty => new() { Type = TileType.Empty, Solid = false };

    // глубокая копия — критично для истории
    public Tile Clone() => new()
    {
        Type = Type,
        ProtoId = ProtoId,
        Solid = Solid,
        Transparent = Transparent,
        SourceRect = SourceRect,
        LayerDepth = LayerDepth
    };
}