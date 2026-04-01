using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MTEngine.Core;
using MTEngine.Rendering;

namespace MTEngine.World;

public class TileMap
{
    public int Width { get; }
    public int Height { get; }
    public int TileSize { get; }

    private readonly Tile[,] _tiles;
    private readonly Dictionary<string, AnimationPlayer> _tileAnimPlayers = new();

    public TileMap(int width, int height, int tileSize = 32)
    {
        Width = width;
        Height = height;
        TileSize = tileSize;
        _tiles = new Tile[width, height];

        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                _tiles[x, y] = Tile.Empty;
    }

    public Tile GetTile(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return Tile.Empty;
        return _tiles[x, y];
    }

    public void SetTile(int x, int y, Tile tile)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height) return;
        _tiles[x, y] = tile;
    }

    public Point WorldToTile(Vector2 worldPos)
        => new((int)(worldPos.X / TileSize), (int)(worldPos.Y / TileSize));

    public Vector2 TileToWorld(int tx, int ty)
        => new(tx * TileSize, ty * TileSize);

    public bool IsSolid(int x, int y) => GetTile(x, y).Solid;

    public void Update(float deltaTime, PrototypeManager prototypes)
    {
        foreach (var proto in prototypes.GetAllTiles())
        {
            if (proto.Animations == null) continue;

            if (!_tileAnimPlayers.TryGetValue(proto.Id, out var player))
            {
                player = new AnimationPlayer();
                var idleClip = proto.Animations.GetClip("idle")
                            ?? proto.Animations.GetAllClips().FirstOrDefault();
                if (idleClip != null)
                    player.Play(idleClip);
                _tileAnimPlayers[proto.Id] = player;
            }

            player.Update(deltaTime);
        }
    }

    public void DrawWithPrototypes(SpriteBatch spriteBatch, Rectangle visibleArea,
                                    PrototypeManager prototypes, AssetManager assets)
    {
        int startX = Math.Max(0, visibleArea.Left / TileSize);
        int startY = Math.Max(0, visibleArea.Top / TileSize);
        int endX = Math.Min(Width, visibleArea.Right / TileSize + 1);
        int endY = Math.Min(Height, visibleArea.Bottom / TileSize + 1);

        for (int x = startX; x < endX; x++)
        {
            for (int y = startY; y < endY; y++)
            {
                var tile = _tiles[x, y];
                if (tile.Type == TileType.Empty || tile.ProtoId == null) continue;

                var proto = prototypes.GetTile(tile.ProtoId);
                if (proto == null) continue;

                var destRect = new Rectangle(x * TileSize, y * TileSize, TileSize, TileSize);

                Texture2D? tex = null;
                Rectangle? srcRect = null;

                if (proto.Sprite?.FullPath != null)
                    tex = assets.LoadFromFile(proto.Sprite.FullPath);

                if (proto.Animations != null && _tileAnimPlayers.TryGetValue(proto.Id, out var player))
                {
                    srcRect = player.GetSourceRect();
                    if (tex == null && !string.IsNullOrEmpty(proto.Animations.TexturePath))
                        tex = assets.LoadFromFile(proto.Animations.TexturePath);
                }
                else if (proto.Sprite != null && tex != null)
                {
                    srcRect = new Rectangle(
                        proto.Sprite.SrcX, proto.Sprite.SrcY,
                        proto.Sprite.Width, proto.Sprite.Height
                    );
                }

                if (tex != null)
                {
                    spriteBatch.Draw(tex, destRect, srcRect, Color.White);
                    continue;
                }

                spriteBatch.Draw(assets.GetColorTexture(proto.Color), destRect, Color.White);
            }
        }
    }
}