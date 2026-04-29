using Microsoft.Xna.Framework;
using MTEngine.Components;
using MTEngine.ECS;
using MTEngine.Rendering;
using MTEngine.World;

namespace MTEngine.Npc;

public static class NpcPerception
{
    public const float DefaultSightDistancePx = 420f;
    public const float DefaultFieldOfViewDegrees = 150f;
    private const float CloseAwarenessDistancePx = 96f;

    public static bool CanSee(
        Entity observer,
        Entity target,
        TileMap? tileMap,
        float sightDistancePx = DefaultSightDistancePx,
        float fieldOfViewDegrees = DefaultFieldOfViewDegrees)
    {
        var observerTransform = observer.GetComponent<TransformComponent>();
        var targetTransform = target.GetComponent<TransformComponent>();
        if (observerTransform == null || targetTransform == null)
            return false;

        var toTarget = targetTransform.Position - observerTransform.Position;
        var distance = toTarget.Length();
        if (distance > sightDistancePx)
            return false;

        if (tileMap != null)
        {
            if (observer.World == null)
            {
                if (!tileMap.HasWorldLineOfSight(observerTransform.Position, targetTransform.Position))
                    return false;
            }
            else if (!EntityOcclusionHelper.HasWorldLineOfSight(
                         tileMap,
                         observer.World,
                         observerTransform.Position,
                         targetTransform.Position,
                         observer))
            {
                return false;
            }
        }

        if (distance <= CloseAwarenessDistancePx)
            return true;

        if (!TryGetFacing(observer, out var facing))
            return true;

        if (toTarget == Vector2.Zero)
            return true;

        toTarget.Normalize();
        facing.Normalize();
        var halfFovRadians = MathHelper.ToRadians(Math.Clamp(fieldOfViewDegrees, 1f, 360f) * 0.5f);
        var minDot = MathF.Cos(halfFovRadians);
        return Vector2.Dot(facing, toTarget) >= minDot;
    }

    private static bool TryGetFacing(Entity observer, out Vector2 facing)
    {
        var velocity = observer.GetComponent<VelocityComponent>()?.Velocity ?? Vector2.Zero;
        if (velocity.LengthSquared() > 0.001f)
        {
            facing = velocity;
            return true;
        }

        var clip = observer.GetComponent<SpriteComponent>()?.AnimationPlayer?.CurrentClipName ?? "";
        facing = clip switch
        {
            var name when name.Contains("_up", StringComparison.OrdinalIgnoreCase) => new Vector2(0f, -1f),
            var name when name.Contains("_down", StringComparison.OrdinalIgnoreCase) => new Vector2(0f, 1f),
            var name when name.Contains("_left", StringComparison.OrdinalIgnoreCase) => new Vector2(-1f, 0f),
            var name when name.Contains("_right", StringComparison.OrdinalIgnoreCase) => new Vector2(1f, 0f),
            _ => Vector2.Zero
        };

        return facing != Vector2.Zero;
    }
}
