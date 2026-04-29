using Microsoft.Xna.Framework;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.World;

namespace MTEngine.Npc;

public class NpcLocationTravelSystem : GameSystem
{
    private const float TransitionFadeSeconds = 0.85f;
    private MapManager? _mapManager;
    private LocationGraph? _locationGraph;
    private WorldPopulationStore? _population;
    private PrototypeManager? _prototypes;
    private EntityFactory? _entityFactory;
    private EventBus? _bus;
    private bool _subscribed;

    public override void OnInitialize()
    {
        if (ServiceLocator.Has<EventBus>())
        {
            _bus = ServiceLocator.Get<EventBus>();
            _bus.Subscribe<MapLoadedEvent>(OnMapLoaded);
            _subscribed = true;
        }
    }

    public override void OnDestroy()
    {
        if (_subscribed && _bus != null)
        {
            _bus.Unsubscribe<MapLoadedEvent>(OnMapLoaded);
            _subscribed = false;
        }
    }

    public override void Update(float deltaTime)
    {
        ResolveServices();

        var map = _mapManager?.CurrentMap;
        if (map == null || _locationGraph == null || _population == null)
            return;

        foreach (var npc in World.GetEntitiesWith<NpcTagComponent, TransformComponent>().ToList())
        {
            if (!NpcLod.IsActive(npc))
                continue;
            if (npc.GetComponent<HealthComponent>()?.IsDead == true)
                continue;

            var travel = npc.GetComponent<NpcLocationTravelComponent>();
            if (travel != null)
            {
                UpdateTravel(npc, travel, map, deltaTime);
                continue;
            }

            var intent = npc.GetComponent<NpcIntentComponent>();
            if (intent == null
                || !intent.HasTarget
                || string.IsNullOrWhiteSpace(intent.TargetMapId)
                || string.Equals(intent.TargetMapId, map.Id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Cross-map travel is only legitimate for explicit Visit-style actions.
            // Anything else (Wander/Sleep/etc.) with a foreign TargetMapId is stale —
            // clear it so ScheduleSystem repopulates with current-map target instead of
            // letting the NPC walk through a transition trigger and leave the location.
            if (intent.Action != ScheduleAction.Visit)
            {
                intent.ClearTarget();
                continue;
            }

            BeginTravel(npc, intent, map);
        }
    }

    private void BeginTravel(Entity npc, NpcIntentComponent intent, MapData map)
    {
        var travel = npc.GetComponent<NpcLocationTravelComponent>() ?? npc.AddComponent(new NpcLocationTravelComponent());
        travel.FinalMapId = intent.TargetMapId;
        travel.FinalX = intent.TargetX;
        travel.FinalY = intent.TargetY;
        travel.FinalAreaId = intent.TargetAreaId;
        travel.FinalPointId = intent.TargetPointId;

        RouteNextHop(npc, travel, map);
    }

    private void UpdateTravel(Entity npc, NpcLocationTravelComponent travel, MapData map, float deltaTime)
    {
        if (travel.FadingOut)
        {
            UpdateFadeAndTransfer(npc, travel, map, deltaTime);
            return;
        }

        if (string.Equals(map.Id, travel.FinalMapId, StringComparison.OrdinalIgnoreCase))
        {
            var finalIntent = npc.GetComponent<NpcIntentComponent>() ?? npc.AddComponent(new NpcIntentComponent());
            finalIntent.Action = ScheduleAction.Visit;
            finalIntent.SetTarget(map.Id, travel.FinalPosition, travel.FinalAreaId, travel.FinalPointId);
            npc.RemoveComponent<NpcLocationTravelComponent>();
            RestoreAlpha(npc, travel);
            return;
        }

        if (string.IsNullOrWhiteSpace(travel.NextMapId)
            || string.IsNullOrWhiteSpace(travel.TriggerId)
            || !map.Triggers.Any(t => string.Equals(t.Id, travel.TriggerId, StringComparison.OrdinalIgnoreCase)))
        {
            RouteNextHop(npc, travel, map);
        }

        var intent = npc.GetComponent<NpcIntentComponent>() ?? npc.AddComponent(new NpcIntentComponent());
        intent.Action = ScheduleAction.Visit;
        intent.SetTarget(map.Id, travel.TransitionPosition, "", $"map_transition:{travel.TriggerId}");

        var atTrigger = IsTouchingTravelTrigger(npc, map, travel.TriggerId);
        if (!atTrigger)
            return;

        travel.FadingOut = true;
        travel.FadeSeconds = 0f;
        travel.OriginalColor = npc.GetComponent<SpriteComponent>()?.Color ?? Color.White;
        intent.ClearTarget();
        Stop(npc);
    }

    private void RouteNextHop(Entity npc, NpcLocationTravelComponent travel, MapData map)
    {
        if (!_locationGraph!.TryGetNextHop(map.Id, travel.FinalMapId, out var nextHop))
        {
            npc.RemoveComponent<NpcLocationTravelComponent>();
            npc.GetComponent<NpcIntentComponent>()?.ClearTarget();
            RestoreAlpha(npc, travel);
            return;
        }

        var trigger = PickTransitionTrigger(npc, map, nextHop);
        if (trigger == null)
        {
            npc.RemoveComponent<NpcLocationTravelComponent>();
            npc.GetComponent<NpcIntentComponent>()?.ClearTarget();
            RestoreAlpha(npc, travel);
            return;
        }

        var tile = PickTriggerTile(npc, trigger, map.TileSize);
        travel.NextMapId = nextHop;
        travel.SpawnPointId = string.IsNullOrWhiteSpace(trigger.Action.SpawnPointId) ? "default" : trigger.Action.SpawnPointId!;
        travel.TriggerId = trigger.Id;
        travel.TransitionX = (tile.X + 0.5f) * map.TileSize;
        travel.TransitionY = (tile.Y + 0.5f) * map.TileSize;

        var intent = npc.GetComponent<NpcIntentComponent>() ?? npc.AddComponent(new NpcIntentComponent());
        intent.Action = ScheduleAction.Visit;
        intent.SetTarget(map.Id, travel.TransitionPosition, "", $"map_transition:{travel.TriggerId}");
    }

    private void UpdateFadeAndTransfer(Entity npc, NpcLocationTravelComponent travel, MapData map, float deltaTime)
    {
        Stop(npc);
        travel.FadeSeconds += deltaTime;
        var fadeT = Math.Clamp(travel.FadeSeconds / TransitionFadeSeconds, 0f, 1f);

        if (npc.GetComponent<SpriteComponent>() is { } sprite)
        {
            var c = travel.OriginalColor;
            sprite.Color = new Color(c.R, c.G, c.B, (byte)MathHelper.Clamp((int)(c.A * (1f - fadeT)), 0, 255));
        }

        if (fadeT < 1f)
            return;

        var nextMapId = travel.NextMapId;
        var spawnPointId = travel.SpawnPointId;
        var spawn = ResolveSpawnPosition(nextMapId, spawnPointId);
        if (npc.GetComponent<TransformComponent>() is { } transform)
            transform.Position = spawn;
        RestoreAlpha(npc, travel);

        var snapshot = _population!.SnapshotAndDespawn(npc, nextMapId, flushWorld: false);
        snapshot.X = spawn.X;
        snapshot.Y = spawn.Y;
        _population.Put(snapshot);

        if (ServiceLocator.Has<IWorldStateTracker>())
            ServiceLocator.Get<IWorldStateTracker>().MarkDirty();
    }

    private TriggerZoneData? PickTransitionTrigger(Entity npc, MapData map, string nextMapId)
    {
        var position = npc.GetComponent<TransformComponent>()!.Position;
        return map.Triggers
            .Where(trigger =>
                string.Equals(trigger.Action.Type, TriggerActionTypes.LocationTransition, StringComparison.OrdinalIgnoreCase)
                && string.Equals(trigger.Action.TargetMapId, nextMapId, StringComparison.OrdinalIgnoreCase)
                && trigger.Tiles.Count > 0)
            .OrderBy(trigger =>
            {
                var tile = PickTriggerTile(npc, trigger, map.TileSize);
                var world = new Vector2((tile.X + 0.5f) * map.TileSize, (tile.Y + 0.5f) * map.TileSize);
                return Vector2.DistanceSquared(position, world);
            })
            .FirstOrDefault();
    }

    private static TriggerTile PickTriggerTile(Entity npc, TriggerZoneData trigger, int tileSize)
    {
        var position = npc.GetComponent<TransformComponent>()?.Position ?? Vector2.Zero;
        return trigger.Tiles
            .OrderBy(tile =>
            {
                var world = new Vector2((tile.X + 0.5f) * tileSize, (tile.Y + 0.5f) * tileSize);
                return Vector2.DistanceSquared(position, world);
            })
            .First();
    }

    private bool IsTouchingTravelTrigger(Entity npc, MapData map, string triggerId)
    {
        var trigger = map.Triggers.FirstOrDefault(t => string.Equals(t.Id, triggerId, StringComparison.OrdinalIgnoreCase));
        if (trigger == null)
            return false;

        var transform = npc.GetComponent<TransformComponent>();
        if (transform == null)
            return false;

        if (npc.GetComponent<ColliderComponent>() is { } collider)
        {
            var bounds = collider.GetBounds(transform.Position);
            var startX = bounds.Left / map.TileSize;
            var startY = bounds.Top / map.TileSize;
            var endX = Math.Max(startX, (bounds.Right - 1) / map.TileSize);
            var endY = Math.Max(startY, (bounds.Bottom - 1) / map.TileSize);

            for (var y = startY; y <= endY; y++)
            {
                for (var x = startX; x <= endX; x++)
                {
                    if (trigger.ContainsTile(x, y))
                        return true;
                }
            }

            return false;
        }

        var tile = new Point((int)MathF.Floor(transform.Position.X / map.TileSize), (int)MathF.Floor(transform.Position.Y / map.TileSize));
        return trigger.ContainsTile(tile.X, tile.Y);
    }

    private Vector2 ResolveSpawnPosition(string mapId, string spawnPointId)
    {
        var targetMap = _mapManager?.LoadBaseMapData(mapId);
        var spawn = targetMap?.SpawnPoints.FirstOrDefault(s => string.Equals(s.Id, spawnPointId, StringComparison.OrdinalIgnoreCase))
                    ?? targetMap?.SpawnPoints.FirstOrDefault();

        return spawn != null ? new Vector2(spawn.X, spawn.Y) : Vector2.Zero;
    }

    private static void Stop(Entity npc)
    {
        if (npc.GetComponent<VelocityComponent>() is { } velocity)
            velocity.Velocity = Vector2.Zero;
        npc.GetComponent<SpriteComponent>()?.PlayDirectionalIdle(Vector2.Zero);
    }

    private static void RestoreAlpha(Entity npc, NpcLocationTravelComponent travel)
    {
        if (npc.GetComponent<SpriteComponent>() is { } sprite)
            sprite.Color = travel.OriginalColor;
    }

    private void ResolveServices()
    {
        _mapManager ??= ServiceLocator.Has<MapManager>() ? ServiceLocator.Get<MapManager>() : null;
        _locationGraph ??= ServiceLocator.Has<LocationGraph>() ? ServiceLocator.Get<LocationGraph>() : null;
        _population ??= ServiceLocator.Has<WorldPopulationStore>() ? ServiceLocator.Get<WorldPopulationStore>() : null;
        _prototypes ??= ServiceLocator.Has<PrototypeManager>() ? ServiceLocator.Get<PrototypeManager>() : null;
        _entityFactory ??= ServiceLocator.Has<EntityFactory>() ? ServiceLocator.Get<EntityFactory>() : null;
    }

    private void OnMapLoaded(MapLoadedEvent ev)
    {
        ResolveServices();
        if (_population == null || _prototypes == null || _entityFactory == null || ev.Map == null)
            return;

        foreach (var snapshot in _population.InMap(ev.Map.Id).ToList())
            _population.Live(snapshot, _prototypes, _entityFactory);
    }
}
