using Microsoft.Xna.Framework;
using MTEngine.Components;
using MTEngine.ECS;
using MTEngine.World;

namespace MTEngine.Rendering;

public static class EntityOcclusionHelper
{
    public static bool IsVisionBlocker(Entity entity)
    {
        var blocker = entity.GetComponent<BlockerComponent>();
        return entity.Active
            && blocker is { Enabled: true, BlocksVision: true }
            && entity.GetComponent<TransformComponent>() != null
            && entity.GetComponent<ColliderComponent>() != null;
    }

    public static bool IsMovementBlocker(Entity entity)
    {
        var blocker = entity.GetComponent<BlockerComponent>();
        return entity.Active
            && blocker is { Enabled: true, BlocksMovement: true }
            && entity.GetComponent<TransformComponent>() != null
            && entity.GetComponent<ColliderComponent>() != null;
    }

    public static bool TryGetBlockerBounds(Entity entity, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;

        var transform = entity.GetComponent<TransformComponent>();
        var collider = entity.GetComponent<ColliderComponent>();
        if (transform == null || collider == null)
            return false;

        bounds = collider.GetBounds(transform.Position);
        return true;
    }

    public static void AppendVisionBlockerEdges(
        ECS.World world,
        Vector2 origin,
        Rectangle visibleWorldBounds,
        List<OccluderEdgeCollector.Edge> edges)
    {
        foreach (var entity in world.GetEntities())
        {
            if (!IsVisionBlocker(entity) || !TryGetBlockerBounds(entity, out var bounds))
                continue;

            if (!bounds.Intersects(visibleWorldBounds))
                continue;

            AppendRectangleEdges(origin, bounds, edges);
        }
    }

    public static bool HasWorldLineOfSight(
        TileMap map,
        ECS.World world,
        Vector2 fromWorld,
        Vector2 toWorld,
        Entity? ignoreBlocker = null)
    {
        if (!map.HasWorldLineOfSight(fromWorld, toWorld))
            return false;

        foreach (var entity in world.GetEntities())
        {
            if (entity == ignoreBlocker || !IsVisionBlocker(entity) || !TryGetBlockerBounds(entity, out var bounds))
                continue;

            if (SegmentIntersectsRectangle(fromWorld, toWorld, bounds))
                return false;
        }

        return true;
    }

    private static void AppendRectangleEdges(Vector2 origin, Rectangle bounds, List<OccluderEdgeCollector.Edge> edges)
    {
        var left = bounds.Left;
        var top = bounds.Top;
        var right = bounds.Right;
        var bottom = bounds.Bottom;

        TryAddEdge(edges, origin, new Vector2(right, top), new Vector2(left, top));
        TryAddEdge(edges, origin, new Vector2(left, bottom), new Vector2(right, bottom));
        TryAddEdge(edges, origin, new Vector2(left, top), new Vector2(left, bottom));
        TryAddEdge(edges, origin, new Vector2(right, bottom), new Vector2(right, top));
    }

    private static void TryAddEdge(List<OccluderEdgeCollector.Edge> edges, Vector2 origin, Vector2 a, Vector2 b)
    {
        var edgeMid = (a + b) * 0.5f;
        var edgeDir = b - a;
        var normal = new Vector2(-edgeDir.Y, edgeDir.X);
        var toOrigin = origin - edgeMid;

        if (Vector2.Dot(normal, toOrigin) > 0f)
            edges.Add(new OccluderEdgeCollector.Edge { A = a, B = b });
    }

    private static bool SegmentIntersectsRectangle(Vector2 start, Vector2 end, Rectangle rectangle)
    {
        if (rectangle.Contains(start) || rectangle.Contains(end))
            return true;

        var min = rectangle.Location.ToVector2();
        var max = new Vector2(rectangle.Right, rectangle.Bottom);
        var delta = end - start;

        const float epsilon = 0.00001f;
        float tMin = 0f;
        float tMax = 1f;

        if (!ClipAxis(start.X, delta.X, min.X, max.X, ref tMin, ref tMax, epsilon))
            return false;
        if (!ClipAxis(start.Y, delta.Y, min.Y, max.Y, ref tMin, ref tMax, epsilon))
            return false;

        return tMax >= tMin && tMax >= 0f && tMin <= 1f;
    }

    private static bool ClipAxis(
        float origin,
        float delta,
        float min,
        float max,
        ref float tMin,
        ref float tMax,
        float epsilon)
    {
        if (MathF.Abs(delta) < epsilon)
            return origin >= min && origin <= max;

        var inv = 1f / delta;
        var t1 = (min - origin) * inv;
        var t2 = (max - origin) * inv;

        if (t1 > t2)
            (t1, t2) = (t2, t1);

        tMin = MathF.Max(tMin, t1);
        tMax = MathF.Min(tMax, t2);
        return tMax >= tMin;
    }
}
