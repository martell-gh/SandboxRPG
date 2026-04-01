using Microsoft.Xna.Framework;
using MTEngine.Components;
using MTEngine.ECS;
using MTEngine.World;

namespace MTEngine.Systems;

public class CollisionSystem : GameSystem
{
    private TileMap? _tileMap;
    private readonly int _tileSize;

    public CollisionSystem(int tileSize = 16)
    {
        _tileSize = tileSize;
    }

    public void SetTileMap(TileMap tileMap)
    {
        _tileMap = tileMap;
    }

    public override void Update(float deltaTime)
    {
        if (_tileMap == null) return;

        foreach (var entity in World.GetEntitiesWith<TransformComponent, ColliderComponent>())
        {
            var transform = entity.GetComponent<TransformComponent>()!;
            var collider = entity.GetComponent<ColliderComponent>()!;

            var bounds = collider.GetBounds(transform.Position);
            var resolved = ResolveCollision(bounds, transform.Position);
            transform.Position = resolved;
        }
    }

    private Vector2 ResolveCollision(Rectangle bounds, Vector2 position)
    {
        if (_tileMap == null) return position;

        // тайлы вокруг персонажа
        int startX = bounds.Left / _tileSize - 1;
        int startY = bounds.Top / _tileSize - 1;
        int endX = bounds.Right / _tileSize + 1;
        int endY = bounds.Bottom / _tileSize + 1;

        var resolved = position;

        for (int tx = startX; tx <= endX; tx++)
        {
            for (int ty = startY; ty <= endY; ty++)
            {
                var tile = _tileMap.GetTile(tx, ty);
                if (!tile.Solid) continue;

                var tileRect = new Rectangle(
                    tx * _tileSize,
                    ty * _tileSize,
                    _tileSize,
                    _tileSize
                );

                var currentBounds = new Rectangle(
                    (int)(resolved.X + bounds.X - position.X),
                    (int)(resolved.Y + bounds.Y - position.Y),
                    bounds.Width,
                    bounds.Height
                );

                if (!currentBounds.Intersects(tileRect)) continue;

                // вычисляем глубину пересечения
                var intersect = Rectangle.Intersect(currentBounds, tileRect);

                // толкаем по наименьшей оси
                if (intersect.Width < intersect.Height)
                {
                    // толкаем по X
                    if (currentBounds.Center.X < tileRect.Center.X)
                        resolved.X -= intersect.Width;
                    else
                        resolved.X += intersect.Width;
                }
                else
                {
                    // толкаем по Y
                    if (currentBounds.Center.Y < tileRect.Center.Y)
                        resolved.Y -= intersect.Height;
                    else
                        resolved.Y += intersect.Height;
                }
            }
        }

        return resolved;
    }

    // проверка — можно ли встать на позицию
    public bool CanMoveTo(Rectangle bounds)
    {
        if (_tileMap == null) return true;

        int startX = bounds.Left / _tileSize;
        int startY = bounds.Top / _tileSize;
        int endX = bounds.Right / _tileSize;
        int endY = bounds.Bottom / _tileSize;

        for (int tx = startX; tx <= endX; tx++)
            for (int ty = startY; ty <= endY; ty++)
                if (_tileMap.GetTile(tx, ty).Solid) return false;

        return true;
    }
}