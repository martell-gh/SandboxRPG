using Microsoft.Xna.Framework;

namespace MTEngine.World;

// Чанк — кусок мира 32x32 тайла
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

    // мировые координаты левого верхнего угла чанка
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
    private readonly int _loadRadius;  // сколько чанков грузим вокруг игрока

    public ChunkManager(int tileSize = 16, int loadRadius = 3)
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
    {
        return _chunks.TryGetValue(chunkPos, out var chunk) ? chunk : null;
    }

    // мировая позиция → позиция чанка
    public Point WorldToChunk(Vector2 worldPos)
    {
        int chunkPixelSize = Chunk.Size * _tileSize;
        return new Point(
            (int)Math.Floor(worldPos.X / chunkPixelSize),
            (int)Math.Floor(worldPos.Y / chunkPixelSize)
        );
    }

    // обновляем загруженные чанки вокруг игрока
    public void UpdateLoadedChunks(Vector2 playerWorldPos)
    {
        var centerChunk = WorldToChunk(playerWorldPos);

        for (int x = centerChunk.X - _loadRadius; x <= centerChunk.X + _loadRadius; x++)
            for (int y = centerChunk.Y - _loadRadius; y <= centerChunk.Y + _loadRadius; y++)
                GetOrCreateChunk(new Point(x, y));
    }

    public IEnumerable<Chunk> GetLoadedChunks() => _chunks.Values.Where(c => c.IsLoaded);

    // базовая генерация — потом заменим на нормальную процедурную
    private void GenerateChunk(Chunk chunk)
    {
        var rng = new Random(chunk.ChunkPos.X * 10000 + chunk.ChunkPos.Y);

        for (int x = 0; x < Chunk.Size; x++)
        {
            for (int y = 0; y < Chunk.Size; y++)
            {
                var tile = rng.Next(100) < 80
                    ? new Tile { Type = TileType.Grass, Solid = false, SourceRect = new Rectangle(0, 0, 16, 16) }
                    : new Tile { Type = TileType.Stone, Solid = true, SourceRect = new Rectangle(16, 0, 16, 16) };

                chunk.Tiles.SetTile(x, y, tile);
            }
        }
    }
}