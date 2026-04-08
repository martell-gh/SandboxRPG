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
    public int LayerCount { get; }

    private readonly Tile[,,] _tiles;
    private readonly Dictionary<string, AnimationPlayer> _tileAnimPlayers = new();

    public TileMap(int width, int height, int tileSize = 32, int layerCount = 3)
    {
        Width = width;
        Height = height;
        TileSize = tileSize;
        LayerCount = Math.Max(1, layerCount);
        _tiles = new Tile[width, height, LayerCount];

        for (int layer = 0; layer < LayerCount; layer++)
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    _tiles[x, y, layer] = Tile.Empty;
    }

    public Tile GetTile(int x, int y, int layer = 0)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height || layer < 0 || layer >= LayerCount)
            return Tile.Empty;
        return _tiles[x, y, layer];
    }

    public void SetTile(int x, int y, Tile tile, int layer = 0)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height || layer < 0 || layer >= LayerCount) return;
        _tiles[x, y, layer] = tile;
    }

    public Point WorldToTile(Vector2 worldPos)
        => new((int)(worldPos.X / TileSize), (int)(worldPos.Y / TileSize));

    public Vector2 TileToWorld(int tx, int ty)
        => new(tx * TileSize, ty * TileSize);

    public bool IsSolid(int x, int y)
    {
        for (int layer = 0; layer < LayerCount; layer++)
        {
            if (GetTile(x, y, layer).Solid)
                return true;
        }

        return false;
    }

    public bool IsOpaque(int x, int y)
    {
        for (int layer = 0; layer < LayerCount; layer++)
        {
            if (GetTile(x, y, layer).Opaque)
                return true;
        }

        return false;
    }

    public bool HasLineOfSight(Point from, Point to)
    {
        if (from == to)
            return true;

        var x0 = from.X;
        var y0 = from.Y;
        var x1 = to.X;
        var y1 = to.Y;

        var dx = Math.Abs(x1 - x0);
        var dy = Math.Abs(y1 - y0);
        var sx = x0 < x1 ? 1 : -1;
        var sy = y0 < y1 ? 1 : -1;
        var err = dx - dy;

        while (true)
        {
            if (x0 == x1 && y0 == y1)
                return true;

            var e2 = err * 2;
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 < dx) { err += dx; y0 += sy; }

            if (!IsInBounds(x0, y0))
                return false;

            if (x0 == x1 && y0 == y1)
                return true;

            if (IsOpaque(x0, y0))
                return false;
        }
    }

    public bool HasWorldLineOfSight(Vector2 fromWorld, Vector2 toWorld)
    {
        var distance = Vector2.Distance(fromWorld, toWorld);
        if (distance <= 0.001f)
            return true;

        var steps = Math.Max(1, (int)MathF.Ceiling(distance / Math.Max(1f, TileSize * 0.2f)));
        var targetTile = WorldToTile(toWorld);

        for (var i = 1; i <= steps; i++)
        {
            var t = i / (float)steps;
            var sample = Vector2.Lerp(fromWorld, toWorld, t);
            var tile = WorldToTile(sample);

            if (!IsInBounds(tile.X, tile.Y))
                return false;

            if (tile == targetTile)
                return true;

            if (IsOpaque(tile.X, tile.Y))
                return false;
        }

        return true;
    }

    public bool IsInBounds(int x, int y)
        => x >= 0 && x < Width && y >= 0 && y < Height;

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
        DrawFilteredWithPrototypes(spriteBatch, visibleArea, prototypes, assets, (_, _, _) => true);
    }

    public void DrawFilteredWithPrototypes(
        SpriteBatch spriteBatch,
        Rectangle visibleArea,
        PrototypeManager prototypes,
        AssetManager assets,
        Func<int, int, Tile, bool> predicate)
    {
        int startX = Math.Max(0, visibleArea.Left / TileSize);
        int startY = Math.Max(0, visibleArea.Top / TileSize);
        int endX = Math.Min(Width, visibleArea.Right / TileSize + 1);
        int endY = Math.Min(Height, visibleArea.Bottom / TileSize + 1);

        for (int x = startX; x < endX; x++)
        {
            for (int y = startY; y < endY; y++)
            {
                for (int layer = 0; layer < LayerCount; layer++)
                {
                    var tile = _tiles[x, y, layer];
                    if (tile.Type == TileType.Empty || tile.ProtoId == null) continue;
                    if (!predicate(x, y, tile)) continue;

                    var proto = prototypes.GetTile(tile.ProtoId);
                    if (proto == null) continue;

                    var destRect = new Rectangle(x * TileSize, y * TileSize, TileSize, TileSize);

                    // Smoothing: pick sprite based on neighbor bitmask
                    if (proto.Smoothing != null)
                    {
                        if (DrawBlobCornerTile(spriteBatch, assets, x, y, layer, proto, prototypes, destRect))
                            continue;

                        var mask = GetConnectionMask(x, y, layer, proto, prototypes);
                        var state = proto.Smoothing.States[mask];
                        if (!string.IsNullOrWhiteSpace(state.FilePath))
                        {
                            var smoothTex = assets.LoadFromFile(state.FilePath);
                            if (smoothTex != null)
                            {
                                var smoothSrc = new Rectangle(state.SrcX, state.SrcY, state.Width, state.Height);
                                DrawSmoothedLayer(spriteBatch, smoothTex, smoothSrc, destRect, state.Rotation);
                                continue;
                            }
                        }
                    }

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

    private bool DrawBlobCornerTile(
        SpriteBatch spriteBatch,
        AssetManager assets,
        int x,
        int y,
        int layer,
        TilePrototype proto,
        PrototypeManager prototypes,
        Rectangle destRect)
    {
        var smoothing = proto.Smoothing;
        if (smoothing == null || !string.Equals(smoothing.Mode, "blobCorners", StringComparison.OrdinalIgnoreCase))
            return false;
        if (smoothing.FillCorner?.FilePath == null
            || smoothing.InnerCorner?.FilePath == null
            || smoothing.HorizontalEdge?.FilePath == null
            || smoothing.VerticalEdge?.FilePath == null
            || smoothing.OuterCorner?.FilePath == null)
            return false;

        var fillTex = assets.LoadFromFile(smoothing.FillCorner.FilePath);
        var innerTex = assets.LoadFromFile(smoothing.InnerCorner.FilePath);
        var horizTex = assets.LoadFromFile(smoothing.HorizontalEdge.FilePath);
        var vertTex = assets.LoadFromFile(smoothing.VerticalEdge.FilePath);
        var outerTex = assets.LoadFromFile(smoothing.OuterCorner.FilePath);
        if (fillTex == null || innerTex == null || horizTex == null || vertTex == null || outerTex == null)
            return false;

        DrawCornerPiece(spriteBatch, fillTex, innerTex, horizTex, vertTex, outerTex,
            smoothing, x, y, layer, proto, prototypes, destRect, 0, x, y - 1, x - 1, y, x - 1, y - 1);
        DrawCornerPiece(spriteBatch, fillTex, innerTex, horizTex, vertTex, outerTex,
            smoothing, x, y, layer, proto, prototypes, destRect, 1, x, y - 1, x + 1, y, x + 1, y - 1);
        DrawCornerPiece(spriteBatch, fillTex, innerTex, horizTex, vertTex, outerTex,
            smoothing, x, y, layer, proto, prototypes, destRect, 2, x, y + 1, x - 1, y, x - 1, y + 1);
        DrawCornerPiece(spriteBatch, fillTex, innerTex, horizTex, vertTex, outerTex,
            smoothing, x, y, layer, proto, prototypes, destRect, 3, x, y + 1, x + 1, y, x + 1, y + 1);

        return true;
    }

    private void DrawCornerPiece(
        SpriteBatch spriteBatch,
        Texture2D fillTex,
        Texture2D innerTex,
        Texture2D horizTex,
        Texture2D vertTex,
        Texture2D outerTex,
        SmoothingConfig smoothing,
        int x,
        int y,
        int layer,
        TilePrototype proto,
        PrototypeManager prototypes,
        Rectangle destRect,
        int quadrant,
        int verticalNeighborX,
        int verticalNeighborY,
        int horizontalNeighborX,
        int horizontalNeighborY,
        int diagonalNeighborX,
        int diagonalNeighborY)
    {
        var vertical = ConnectsToNeighbor(verticalNeighborX, verticalNeighborY, layer, proto, prototypes);
        var horizontal = ConnectsToNeighbor(horizontalNeighborX, horizontalNeighborY, layer, proto, prototypes);
        var diagonal = ConnectsToNeighbor(diagonalNeighborX, diagonalNeighborY, layer, proto, prototypes);

        Texture2D texture;
        SmoothingState state;

        if (vertical && horizontal && diagonal)
        {
            texture = fillTex;
            state = smoothing.FillCorner!;
        }
        else if (vertical && horizontal)
        {
            texture = innerTex;
            state = smoothing.InnerCorner!;
        }
        else if (horizontal)
        {
            texture = horizTex;
            state = smoothing.HorizontalEdge!;
        }
        else if (vertical)
        {
            texture = vertTex;
            state = smoothing.VerticalEdge!;
        }
        else
        {
            texture = outerTex;
            state = smoothing.OuterCorner!;
        }

        var src = GetBlobQuadrantSourceRect(texture, state, quadrant);
        var dest = GetBlobQuadrantDestinationRect(destRect, quadrant);
        spriteBatch.Draw(texture, dest, src, Color.White);
    }

    private static void DrawSmoothedLayer(
        SpriteBatch spriteBatch,
        Texture2D texture,
        Rectangle sourceRect,
        Rectangle destRect,
        float rotationDegrees)
    {
        var origin = new Vector2(sourceRect.Width * 0.5f, sourceRect.Height * 0.5f);
        var position = new Vector2(destRect.X + destRect.Width * 0.5f, destRect.Y + destRect.Height * 0.5f);
        var scale = new Vector2(
            destRect.Width / (float)sourceRect.Width,
            destRect.Height / (float)sourceRect.Height);

        spriteBatch.Draw(
            texture,
            position,
            sourceRect,
            Color.White,
            MathHelper.ToRadians(rotationDegrees),
            origin,
            scale,
            SpriteEffects.None,
            0f);
    }

    private static Rectangle GetBlobQuadrantSourceRect(Texture2D texture, SmoothingState state, int quadrant)
    {
        var isAtlas = texture.Width >= state.SrcX + 64 && texture.Height >= state.SrcY + 64;
        if (isAtlas)
        {
            return quadrant switch
            {
                0 => new Rectangle(state.SrcX + 32, state.SrcY + 0, 16, 16),
                1 => new Rectangle(state.SrcX + 16, state.SrcY + 32, 16, 16),
                2 => new Rectangle(state.SrcX + 32, state.SrcY + 48, 16, 16),
                _ => new Rectangle(state.SrcX + 16, state.SrcY + 16, 16, 16),
            };
        }

        return quadrant switch
        {
            0 => new Rectangle(state.SrcX + 0, state.SrcY + 0, 16, 16),
            1 => new Rectangle(state.SrcX + 16, state.SrcY + 0, 16, 16),
            2 => new Rectangle(state.SrcX + 0, state.SrcY + 16, 16, 16),
            _ => new Rectangle(state.SrcX + 16, state.SrcY + 16, 16, 16),
        };
    }

    private static Rectangle GetBlobQuadrantDestinationRect(Rectangle destRect, int quadrant)
    {
        var halfW = destRect.Width / 2;
        var halfH = destRect.Height / 2;

        return quadrant switch
        {
            0 => new Rectangle(destRect.X, destRect.Y, halfW, halfH),
            1 => new Rectangle(destRect.X + halfW, destRect.Y, halfW, halfH),
            2 => new Rectangle(destRect.X, destRect.Y + halfH, halfW, halfH),
            _ => new Rectangle(destRect.X + halfW, destRect.Y + halfH, halfW, halfH),
        };
    }

    private int GetConnectionMask(int x, int y, int layer, TilePrototype proto, PrototypeManager prototypes)
    {
        var mask = 0;

        if (ConnectsToNeighbor(x, y - 1, layer, proto, prototypes))
            mask |= 1; // N
        if (ConnectsToNeighbor(x, y + 1, layer, proto, prototypes))
            mask |= 2; // S
        if (ConnectsToNeighbor(x - 1, y, layer, proto, prototypes))
            mask |= 4; // W
        if (ConnectsToNeighbor(x + 1, y, layer, proto, prototypes))
            mask |= 8; // E

        return mask;
    }

    private bool ConnectsToNeighbor(int x, int y, int layer, TilePrototype proto, PrototypeManager prototypes)
    {
        if (!IsInBounds(x, y))
            return false;

        var neighborTile = GetTile(x, y, layer);
        if (neighborTile.Type == TileType.Empty || string.IsNullOrWhiteSpace(neighborTile.ProtoId))
            return false;

        if (string.Equals(neighborTile.ProtoId, proto.Id, StringComparison.OrdinalIgnoreCase))
            return true;

        if (proto.Smoothing == null)
            return false;

        if (proto.Smoothing.SmoothWith.Any(id => string.Equals(id, neighborTile.ProtoId, StringComparison.OrdinalIgnoreCase)))
            return true;

        var neighborProto = prototypes.GetTile(neighborTile.ProtoId);
        if (neighborProto?.Smoothing == null)
            return false;

        return neighborProto.Smoothing.SmoothWith.Any(id => string.Equals(id, proto.Id, StringComparison.OrdinalIgnoreCase));
    }
}
