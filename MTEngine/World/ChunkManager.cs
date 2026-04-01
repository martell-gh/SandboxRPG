using Microsoft.Xna.Framework;

namespace MTEngine.World;

public class Chunk
{
    public const int Size = 32;
    public Point ChunkPos { get; }
    public TileMap Tiles { get; }
    public bool IsLoaded { get; set; }

    public Chunk(Point chunkPos, int tileSize)
    {
        ChunkPos = chunkPos;
        Tiles = new TileMap(Size, Size, tileSize);
        IsLoaded = true;
    }

    public Vector2 WorldOrigin(int tileSize)
    {
        return new Vector2(
            ChunkPos.X * Size * tileSize,
            ChunkPos.Y * Size * tileSize
        );
    }
}

public class ChunkManager
{
    private readonly Dictionary<Point, Chunk> _chunks = new();
    private readonly int _tileSize;
    private readonly int _loadRadius;

    public ChunkManager(int tileSize = 32, int loadRadius = 3)
    {
        _tileSize = tileSize;
        _loadRadius = loadRadius;
    }

    public Chunk GetOrCreateChunk(Point chunkPos)
    {
        if (!_chunks.TryGetValue(chunkPos, out var chunk))
        {
            chunk = new Chunk(chunkPos, _tileSize);
            _chunks[chunkPos] = chunk;
            GenerateChunk(chunk);
        }
        return chunk;
    }

    public Chunk? GetChunk(Point chunkPos)
        => _chunks.TryGetValue(chunkPos, out var chunk) ? chunk : null;

    public Point WorldToChunk(Vector2 worldPos)
    {
        int chunkPixelSize = Chunk.Size * _tileSize;
        return new Point(
            (int)Math.Floor(worldPos.X / chunkPixelSize),
            (int)Math.Floor(worldPos.Y / chunkPixelSize)
        );
    }

    public void UpdateLoadedChunks(Vector2 playerWorldPos)
    {
        var centerChunk = WorldToChunk(playerWorldPos);
        for (int x = centerChunk.X - _loadRadius; x <= centerChunk.X + _loadRadius; x++)
            for (int y = centerChunk.Y - _loadRadius; y <= centerChunk.Y + _loadRadius; y++)
                GetOrCreateChunk(new Point(x, y));
    }

    public IEnumerable<Chunk> GetLoadedChunks() => _chunks.Values.Where(c => c.IsLoaded);

    private void GenerateChunk(Chunk chunk)
    {
        var rng = new Random(chunk.ChunkPos.X * 10000 + chunk.ChunkPos.Y);
        for (int x = 0; x < Chunk.Size; x++)
            for (int y = 0; y < Chunk.Size; y++)
            {
                var tile = rng.Next(100) < 80
                    ? new Tile { Type = TileType.Grass, Solid = false, SourceRect = new Microsoft.Xna.Framework.Rectangle(0, 0, 32, 32) }
                    : new Tile { Type = TileType.Stone, Solid = true, SourceRect = new Microsoft.Xna.Framework.Rectangle(32, 0, 32, 32) };
                chunk.Tiles.SetTile(x, y, tile);
            }
    }
}