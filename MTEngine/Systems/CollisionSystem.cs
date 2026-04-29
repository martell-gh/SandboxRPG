using Microsoft.Xna.Framework;
using MTEngine.Components;
using MTEngine.ECS;
using MTEngine.Rendering;
using MTEngine.World;

namespace MTEngine.Systems;

public class CollisionSystem : GameSystem
{
    private TileMap? _tileMap;
    private readonly int _tileSize;

    public CollisionSystem(int tileSize = 32)
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
            resolved = ResolveEntityCollision(entity, bounds, transform.Position, resolved);
            transform.Position = resolved;
        }
    }

    private Vector2 ResolveCollision(Rectangle bounds, Vector2 position)
    {
        if (_tileMap == null) return position;

        int startX = bounds.Left / _tileSize - 1;
        int startY = bounds.Top / _tileSize - 1;
        int endX = bounds.Right / _tileSize + 1;
        int endY = bounds.Bottom / _tileSize + 1;

        var resolved = position;

        for (int tx = startX; tx <= endX; tx++)
        {
            for (int ty = startY; ty <= endY; ty++)
            {
                if (!_tileMap.IsSolid(tx, ty)) continue;

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

                var intersect = Rectangle.Intersect(currentBounds, tileRect);

                if (intersect.Width < intersect.Height)
                {
                    if (currentBounds.Center.X < tileRect.Center.X)
                        resolved.X -= intersect.Width;
                    else
                        resolved.X += intersect.Width;
                }
                else
                {
                    if (currentBounds.Center.Y < tileRect.Center.Y)
                        resolved.Y -= intersect.Height;
                    else
                        resolved.Y += intersect.Height;
                }
            }
        }

        return resolved;
    }

    private Vector2 ResolveEntityCollision(Entity mover, Rectangle originalBounds, Vector2 originalPosition, Vector2 resolvedPosition)
    {
        var currentBounds = new Rectangle(
            (int)(resolvedPosition.X + originalBounds.X - originalPosition.X),
            (int)(resolvedPosition.Y + originalBounds.Y - originalPosition.Y),
            originalBounds.Width,
            originalBounds.Height);

        foreach (var entity in World.GetEntities())
        {
            if (entity == mover || !EntityOcclusionHelper.IsMovementBlocker(entity))
                continue;

            if (!EntityOcclusionHelper.TryGetBlockerBounds(entity, out var blockerBounds))
                continue;

            if (!currentBounds.Intersects(blockerBounds))
                continue;

            var intersect = Rectangle.Intersect(currentBounds, blockerBounds);

            if (intersect.Width < intersect.Height)
            {
                if (currentBounds.Center.X < blockerBounds.Center.X)
                    resolvedPosition.X -= intersect.Width;
                else
                    resolvedPosition.X += intersect.Width;
            }
            else
            {
                if (currentBounds.Center.Y < blockerBounds.Center.Y)
                    resolvedPosition.Y -= intersect.Height;
                else
                    resolvedPosition.Y += intersect.Height;
            }

            currentBounds = new Rectangle(
                (int)(resolvedPosition.X + originalBounds.X - originalPosition.X),
                (int)(resolvedPosition.Y + originalBounds.Y - originalPosition.Y),
                originalBounds.Width,
                originalBounds.Height);
        }

        return resolvedPosition;
    }

    public bool CanMoveTo(Rectangle bounds, Entity? ignoreEntity = null)
    {
        if (_tileMap == null) return true;

        int startX = bounds.Left / _tileSize;
        int startY = bounds.Top / _tileSize;
        int endX = bounds.Right / _tileSize;
        int endY = bounds.Bottom / _tileSize;

        for (int tx = startX; tx <= endX; tx++)
            for (int ty = startY; ty <= endY; ty++)
                if (_tileMap.IsSolid(tx, ty)) return false;

        foreach (var entity in World.GetEntities())
        {
            if (entity == ignoreEntity || !EntityOcclusionHelper.IsMovementBlocker(entity))
                continue;

            if (EntityOcclusionHelper.TryGetBlockerBounds(entity, out var blockerBounds)
                && bounds.Intersects(blockerBounds))
                return false;
        }

        return true;
    }
}
