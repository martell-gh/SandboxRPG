using Microsoft.Xna.Framework;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Rendering;
using MTEngine.World;

namespace MTEngine.Npc;

/// <summary>
/// Двигает NPC по пути из GridPathfinder к target из NpcIntentComponent.
/// Перепланирует путь, если цель сменилась или текущая точка достигнута.
/// Не перепланирует пока NPC уже в окрестности цели (3px).
/// </summary>
public class NpcMovementSystem : GameSystem
{
    private MapManager? _mapManager;
    private float _replanAccumulator;
    private const float ReplanInterval = 1f;          // принудительный repath раз в секунду
    private const float WaypointReachedDistance = 3f; // px
    private const float WanderDwellDuration = 2f;     // короткая остановка на точке прогулки

    private readonly Dictionary<int, NpcPathState> _state = new();

    public override void OnInitialize()
    {
    }

    public override void Update(float deltaTime)
    {
        if (_mapManager == null)
        {
            if (!ServiceLocator.Has<MapManager>())
                return;
            _mapManager = ServiceLocator.Get<MapManager>();
        }

        var tileMap = _mapManager.CurrentTileMap;
        var map = _mapManager.CurrentMap;
        if (tileMap == null || map == null) return;
        var locationTransitionTiles = BuildLocationTransitionTiles(map);

        _replanAccumulator += deltaTime;
        var forceReplan = _replanAccumulator >= ReplanInterval;
        if (forceReplan) _replanAccumulator = 0f;

        foreach (var entity in World.GetEntitiesWith<NpcTagComponent, TransformComponent>())
        {
            if (!NpcLod.IsActive(entity))
                continue;

            var velocity = entity.GetComponent<VelocityComponent>();
            var intent = entity.GetComponent<NpcIntentComponent>();
            if (velocity == null || intent == null) continue;
            var transform = entity.GetComponent<TransformComponent>()!;
            var sprite = entity.GetComponent<SpriteComponent>();
            var health = entity.GetComponent<HealthComponent>();
            var st = GetState(entity.Id);
            TryClosePassedDoors(transform.Position, st, map.TileSize);

            if (health?.IsDead == true) { StopNpc(velocity, sprite); continue; }

            if (entity.GetComponent<NpcInteractionHoldComponent>() is { } hold)
            {
                FaceHeldInteractionActor(entity, hold, velocity, sprite);
                continue;
            }

            if (!intent.HasTarget || intent.TargetMapId != map.Id)
            {
                StopNpc(velocity, sprite);
                continue;
            }

            var targetKey = BuildTargetKey(intent);
            if (!string.Equals(st.TargetKey, targetKey, StringComparison.Ordinal))
            {
                st.TargetKey = targetKey;
                st.ArrivalPublished = false;
                st.DwellTargetKey = "";
                st.DwellRemaining = 0f;
            }

            if (Vector2.Distance(transform.Position, intent.TargetPosition) <= WaypointReachedDistance)
            {
                if (TryDwellBeforeArrival(intent, velocity, sprite, st, targetKey, deltaTime))
                    continue;

                CompleteArrival(entity, intent, velocity, st);
                continue;
            }

            var needsReplan = forceReplan
                || st.Path.Count == 0
                || st.LastTarget != intent.TargetPosition;

            if (needsReplan)
            {
                var fromTile = tileMap.WorldToTile(transform.Position);
                var toTile = tileMap.WorldToTile(intent.TargetPosition);
                var targetIsTransition = intent.TargetPointId.StartsWith("map_transition:", StringComparison.OrdinalIgnoreCase);
                st.Path = GridPathfinder.FindPath(
                    tileMap,
                    fromTile,
                    toTile,
                    isBlocked: tile => locationTransitionTiles.Contains(tile)
                                      && (!targetIsTransition || tile != toTile)
                        || IsTileBlockedByEntity(tile, map.TileSize, entity));
                st.LastTarget = intent.TargetPosition;
                st.PathIndex = st.Path.Count > 0 ? 1 : 0;   // skip current tile
            }

            if (st.Path.Count == 0 || st.PathIndex >= st.Path.Count)
            {
                if (Vector2.Distance(transform.Position, intent.TargetPosition) <= WaypointReachedDistance * 2f)
                {
                    if (TryDwellBeforeArrival(intent, velocity, sprite, st, targetKey, deltaTime))
                        continue;

                    CompleteArrival(entity, intent, velocity, st);
                }
                else
                {
                    StopNpc(velocity, sprite);
                }
                continue;
            }

            // двигаемся к следующей путевой точке
            var nextTile = st.Path[st.PathIndex];
            TryOpenDoorAhead(entity, transform.Position, nextTile, map.TileSize, st);

            var nextWorld = new Vector2(
                (nextTile.X + 0.5f) * map.TileSize,
                (nextTile.Y + 0.5f) * map.TileSize);
            var diff = nextWorld - transform.Position;
            var dist = diff.Length();
            if (dist <= WaypointReachedDistance)
            {
                st.PathIndex++;
                StopNpc(velocity, sprite);
                continue;
            }
            var dir = diff / dist;
            var step = dir * velocity.Speed * deltaTime;
            if (step.LengthSquared() > diff.LengthSquared())
                step = diff;
            transform.Position += step;
            velocity.Velocity = dir * velocity.Speed;
            sprite?.PlayDirectionalIdle(dir);
        }
    }

    private bool TryOpenDoorAhead(Entity actor, Vector2 actorPosition, Point nextTile, int tileSize, NpcPathState state)
    {
        var nextTileRect = new Rectangle(nextTile.X * tileSize, nextTile.Y * tileSize, tileSize, tileSize);
        var maxDistance = tileSize * 1.75f;

        foreach (var doorEntity in World.GetEntitiesWith<DoorComponent>())
        {
            if (doorEntity == actor)
                continue;

            var door = doorEntity.GetComponent<DoorComponent>()!;
            if (door.IsOpen)
                continue;

            var blocker = doorEntity.GetComponent<BlockerComponent>();
            if (blocker is not { Enabled: true, BlocksMovement: true })
                continue;

            var transform = doorEntity.GetComponent<TransformComponent>()!;
            var collider = doorEntity.GetComponent<ColliderComponent>()!;
            var bounds = collider.GetBounds(transform.Position);
            var inflated = new Rectangle(bounds.X - 2, bounds.Y - 2, bounds.Width + 4, bounds.Height + 4);
            if (!inflated.Intersects(nextTileRect))
                continue;

            var doorCenter = bounds.Center.ToVector2();
            if (Vector2.Distance(actorPosition, doorCenter) > maxDistance)
                continue;

            door.IsOpen = true;
            RememberOpenedDoor(state, doorEntity);
            MarkWorldDirty();
            return true;
        }

        return false;
    }

    private bool IsTileBlockedByEntity(Point tile, int tileSize, Entity mover)
    {
        var tileRect = new Rectangle(tile.X * tileSize, tile.Y * tileSize, tileSize, tileSize);

        foreach (var entity in World.GetEntities())
        {
            if (entity == mover)
                continue;

            // Doors are planned as passable: NPCs open/close them while following the path.
            if (entity.GetComponent<DoorComponent>() != null)
                continue;

            if (!EntityOcclusionHelper.IsMovementBlocker(entity))
                continue;

            if (EntityOcclusionHelper.TryGetBlockerBounds(entity, out var bounds)
                && bounds.Intersects(tileRect))
                return true;
        }

        return false;
    }

    private void TryClosePassedDoors(Vector2 actorPosition, NpcPathState state, int tileSize)
    {
        if (state.OpenedDoors.Count == 0)
            return;

        var closeDistance = tileSize * 1.2f;
        for (var i = state.OpenedDoors.Count - 1; i >= 0; i--)
        {
            var opened = state.OpenedDoors[i];
            var doorEntity = opened.Door;
            if (!doorEntity.Active)
            {
                state.OpenedDoors.RemoveAt(i);
                continue;
            }

            var door = doorEntity.GetComponent<DoorComponent>();
            if (door == null || !door.IsOpen)
            {
                state.OpenedDoors.RemoveAt(i);
                continue;
            }

            if (!TryGetDoorBounds(doorEntity, out var bounds))
            {
                state.OpenedDoors.RemoveAt(i);
                continue;
            }

            var doorCenter = bounds.Center.ToVector2();
            if (Vector2.Distance(actorPosition, doorCenter) <= closeDistance)
                continue;

            if (!IsDoorPassageClear(doorEntity, bounds))
                continue;

            door.IsOpen = false;
            MarkWorldDirty();
            state.OpenedDoors.RemoveAt(i);
        }
    }

    private static void RememberOpenedDoor(NpcPathState state, Entity doorEntity)
    {
        if (state.OpenedDoors.Any(opened => opened.Door == doorEntity))
            return;

        state.OpenedDoors.Add(new NpcOpenedDoorState(doorEntity));
    }

    private bool IsDoorPassageClear(Entity doorEntity, Rectangle doorBounds)
    {
        var keepOpenBounds = new Rectangle(
            doorBounds.X - 6,
            doorBounds.Y - 6,
            doorBounds.Width + 12,
            doorBounds.Height + 12);

        foreach (var entity in World.GetEntitiesWith<TransformComponent, ColliderComponent>())
        {
            if (entity == doorEntity)
                continue;

            if (!entity.HasComponent<NpcTagComponent>() && !entity.HasComponent<PlayerTagComponent>())
                continue;

            var transform = entity.GetComponent<TransformComponent>()!;
            var collider = entity.GetComponent<ColliderComponent>()!;
            if (collider.GetBounds(transform.Position).Intersects(keepOpenBounds))
                return false;
        }

        return true;
    }

    private static bool TryGetDoorBounds(Entity doorEntity, out Rectangle bounds)
    {
        var transform = doorEntity.GetComponent<TransformComponent>();
        var collider = doorEntity.GetComponent<ColliderComponent>();
        if (transform == null || collider == null)
        {
            bounds = Rectangle.Empty;
            return false;
        }

        bounds = collider.GetBounds(transform.Position);
        return true;
    }

    private static void MarkWorldDirty()
    {
        if (ServiceLocator.Has<IWorldStateTracker>())
            ServiceLocator.Get<IWorldStateTracker>().MarkDirty();
    }

    private static string BuildTargetKey(NpcIntentComponent intent)
        => $"{intent.Action}|{intent.TargetMapId}|{intent.TargetAreaId}|{intent.TargetPointId}|{intent.TargetX:0.###}|{intent.TargetY:0.###}";

    private static bool TryDwellBeforeArrival(
        NpcIntentComponent intent,
        VelocityComponent velocity,
        SpriteComponent? sprite,
        NpcPathState state,
        string targetKey,
        float deltaTime)
    {
        if (intent.Arrived || intent.Action != ScheduleAction.Wander)
            return false;

        if (!string.Equals(state.DwellTargetKey, targetKey, StringComparison.Ordinal))
        {
            state.DwellTargetKey = targetKey;
            state.DwellRemaining = WanderDwellDuration;
        }

        if (state.DwellRemaining <= 0f)
            return false;

        state.DwellRemaining = Math.Max(0f, state.DwellRemaining - deltaTime);
        StopNpc(velocity, sprite);
        return state.DwellRemaining > 0f;
    }

    private static void StopNpc(VelocityComponent velocity, SpriteComponent? sprite)
    {
        velocity.Velocity = Vector2.Zero;
        sprite?.PlayDirectionalIdle(Vector2.Zero);
    }

    private void FaceHeldInteractionActor(
        Entity entity,
        NpcInteractionHoldComponent hold,
        VelocityComponent velocity,
        SpriteComponent? sprite)
    {
        velocity.Velocity = Vector2.Zero;

        var ownPosition = entity.GetComponent<TransformComponent>()?.Position;
        var actor = World.GetEntities().FirstOrDefault(e => e.Active && e.Id == hold.ActorId);
        var actorPosition = actor?.GetComponent<TransformComponent>()?.Position;
        if (ownPosition.HasValue && actorPosition.HasValue)
            sprite?.PlayDirectionalIdle(actorPosition.Value - ownPosition.Value);
        else
            sprite?.PlayDirectionalIdle(Vector2.Zero);
    }

    private static HashSet<Point> BuildLocationTransitionTiles(MapData map)
    {
        var blocked = new HashSet<Point>();
        foreach (var trigger in map.Triggers)
        {
            if (!string.Equals(trigger.Action.Type, TriggerActionTypes.LocationTransition, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var tile in trigger.Tiles)
                blocked.Add(new Point(tile.X, tile.Y));
        }

        return blocked;
    }

    private static void CompleteArrival(Entity entity, NpcIntentComponent intent, VelocityComponent velocity, NpcPathState state)
    {
        velocity.Velocity = Vector2.Zero;
        intent.MarkArrived();

        if (state.ArrivalPublished || string.IsNullOrWhiteSpace(intent.TargetAreaId))
            return;

        state.ArrivalPublished = true;
        if (ServiceLocator.Has<EventBus>())
        {
            ServiceLocator.Get<EventBus>().Publish(new NpcArrivedAtArea(
                entity,
                intent.Action,
                intent.TargetMapId,
                intent.TargetAreaId,
                intent.TargetPointId,
                intent.TargetPosition));
        }
    }

    private NpcPathState GetState(int entityId)
    {
        if (!_state.TryGetValue(entityId, out var st))
        {
            st = new NpcPathState();
            _state[entityId] = st;
        }
        return st;
    }

    private sealed class NpcPathState
    {
        public List<Point> Path = new();
        public int PathIndex;
        public Vector2 LastTarget;
        public string TargetKey = "";
        public bool ArrivalPublished;
        public List<NpcOpenedDoorState> OpenedDoors = new();
        public string DwellTargetKey = "";
        public float DwellRemaining;
    }

    private sealed class NpcOpenedDoorState
    {
        public Entity Door { get; }

        public NpcOpenedDoorState(Entity door)
        {
            Door = door;
        }
    }
}
