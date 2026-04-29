using Microsoft.Xna.Framework;
using MTEngine.Combat;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Items;
using MTEngine.Systems;
using MTEngine.World;

namespace MTEngine.Npc;

public class CombatThreatSystem : GameSystem
{
    private const float SafeForSecondsTarget = 5f;
    private const float SafeDistance = 500f;
    private const float RepathDistance = 96f;
    private const float UnarmedWeaponThreshold = 9f;
    private const int HostileFactionThreshold = -50;
    private const float FleeRepathCooldownSeconds = 1.25f;
    private const int EscapeSearchRadiusMin = 4;
    private const int EscapeSearchRadiusMax = 18;
    private const int EscapeSearchMaxNodes = 1200;
    /// <summary>Жёсткий таймаут на flee — иначе NPC, загнанный в угол, висит в нём вечно.</summary>
    private const float MaxFleeSeconds = 90f;

    private MapManager? _mapManager;
    private GameClock? _clock;
    private readonly Random _rng = new();

    public override void Update(float deltaTime)
    {
        _mapManager ??= ServiceLocator.Has<MapManager>() ? ServiceLocator.Get<MapManager>() : null;
        _clock ??= ServiceLocator.Has<GameClock>() ? ServiceLocator.Get<GameClock>() : null;

        var map = _mapManager?.CurrentMap;
        var tileMap = _mapManager?.CurrentTileMap;
        if (map == null || tileMap == null || _clock == null)
            return;

        var player = World.GetEntitiesWith<PlayerTagComponent, TransformComponent>().FirstOrDefault();
        if (player == null || player.GetComponent<HealthComponent>()?.IsDead == true)
            return;

        foreach (var npc in World.GetEntitiesWith<NpcTagComponent, TransformComponent>())
        {
            if (!NpcLod.IsActive(npc))
                continue;
            if (npc.GetComponent<HealthComponent>()?.IsDead == true)
                continue;

            var flee = npc.GetComponent<NpcFleeComponent>();
            if (flee != null)
            {
                UpdateFlee(npc, flee, ResolveFleeThreat(flee, player), map, tileMap, deltaTime);
                continue;
            }

            var threat = IsInCombatContext(npc)
                ? player
                : FindHostileFactionThreat(npc, tileMap, player);
            if (threat == null)
                continue;

            if (ShouldFlee(npc, threat, out var reason))
            {
                if (!TryFindEscapeTarget(npc, threat, map, tileMap, out var target))
                    continue;

                StartFlee(npc, threat, target, reason);
                continue;
            }

            if (threat.HasComponent<PlayerTagComponent>() && NpcPerception.CanSee(npc, threat, tileMap))
                StartHostileAggression(npc, threat);
        }
    }

    private Entity? ResolveFleeThreat(NpcFleeComponent flee, Entity player)
    {
        if (flee.ThreatEntityId == player.Id)
            return player;

        foreach (var entity in World.GetEntitiesWith<TransformComponent>())
        {
            if (entity.Id == flee.ThreatEntityId)
                return entity;
        }

        return string.IsNullOrWhiteSpace(flee.ThreatSaveId)
            ? null
            : World.GetEntitiesWith<SaveEntityIdComponent, TransformComponent>()
                .FirstOrDefault(entity => string.Equals(
                    entity.GetComponent<SaveEntityIdComponent>()!.SaveId,
                    flee.ThreatSaveId,
                    StringComparison.OrdinalIgnoreCase));
    }

    private void UpdateFlee(Entity npc, NpcFleeComponent flee, Entity? threat, MapData map, TileMap tileMap, float deltaTime)
    {
        var npcTransform = npc.GetComponent<TransformComponent>()!;
        var now = _clock!.TotalSecondsAbsolute;
        var threatTransform = threat?.GetComponent<TransformComponent>();
        var threatHealth = threat?.GetComponent<HealthComponent>();
        var threatGone = threat == null || threatTransform == null || threatHealth?.IsDead == true;
        var threatPosition = threatTransform?.Position ?? flee.LastThreatPosition;
        var distanceToThreat = Vector2.Distance(npcTransform.Position, threatPosition);
        var seesThreat = !threatGone && NpcPerception.CanSee(npc, threat!, tileMap);
        var currentlySafe = threatGone || distanceToThreat >= SafeDistance && (!seesThreat || distanceToThreat >= SafeDistance * 1.15f);
        flee.RepathCooldownSeconds = Math.Max(0f, flee.RepathCooldownSeconds - deltaTime);

        if (!currentlySafe)
        {
            flee.LastSawThreatAt = now;
            flee.LastThreatPosition = threatPosition;
            flee.SafeForSeconds = 0f;
        }
        else
        {
            flee.SafeForSeconds += deltaTime;
        }

        var tooClose = distanceToThreat < SafeDistance * 0.7f;
        var targetTooNearThreat = Vector2.Distance(flee.EscapeTarget, threatPosition) < SafeDistance * 0.75f;
        var closeToTarget = Vector2.Distance(npcTransform.Position, flee.EscapeTarget) < RepathDistance;
        if (!threatGone
            && flee.RepathCooldownSeconds <= 0f
            && (tooClose || targetTooNearThreat || closeToTarget && !currentlySafe)
            && TryFindEscapeTarget(npc, threat!, map, tileMap, out var newTarget))
        {
            flee.EscapeTarget = newTarget;
            flee.RepathCooldownSeconds = FleeRepathCooldownSeconds;
        }

        var intent = npc.GetComponent<NpcIntentComponent>() ?? npc.AddComponent(new NpcIntentComponent());
        intent.Action = ScheduleAction.Visit;
        intent.SetTarget(map.Id, flee.EscapeTarget, "", "flee");

        var safeReached = flee.SafeForSeconds >= SafeForSecondsTarget;
        var fleeTimedOut = now - flee.StartedAt >= MaxFleeSeconds;

        if (safeReached || fleeTimedOut)
        {
            npc.RemoveComponent<NpcFleeComponent>();
            intent.ClearTarget();
            if (fleeTimedOut)
                Aggression.Clear(npc);
            World.GetSystem<ScheduleSystem>()?.RefreshNow();
            MarkDirty();
        }
    }

    private static bool IsInCombatContext(Entity npc)
    {
        if (npc.GetComponent<NpcAggressionComponent>() is { Mode: not AggressionMode.None })
            return true;

        return npc.GetComponent<AvengerComponent>() != null;
    }

    private Entity? FindHostileFactionThreat(Entity npc, TileMap tileMap, Entity player)
    {
        var npcFaction = npc.GetComponent<IdentityComponent>()?.FactionId ?? "";
        if (string.IsNullOrWhiteSpace(npcFaction))
            return null;

        var npcTransform = npc.GetComponent<TransformComponent>()!;
        Entity? bestThreat = null;
        var bestScore = float.MinValue;

        void ConsiderThreat(Entity other)
        {
            if (other == npc || other.GetComponent<HealthComponent>()?.IsDead == true)
                return;

            var otherFaction = other.GetComponent<IdentityComponent>()?.FactionId ?? "";
            if (!AreFactionsHostile(npcFaction, otherFaction))
                return;

            var otherTransform = other.GetComponent<TransformComponent>()!;
            var distance = Vector2.Distance(npcTransform.Position, otherTransform.Position);
            if (distance > SafeDistance * 1.6f && !NpcPerception.CanSee(npc, other, tileMap))
                return;

            var score = CalculatePower(other) - CalculatePower(npc) - distance * 0.03f;
            if (score > bestScore)
            {
                bestScore = score;
                bestThreat = other;
            }
        }

        ConsiderThreat(player);

        foreach (var other in World.GetEntitiesWith<NpcTagComponent, TransformComponent>())
        {
            if (!NpcLod.IsActive(other))
                continue;
            ConsiderThreat(other);
        }

        return bestThreat;
    }

    private bool AreFactionsHostile(string leftFactionId, string rightFactionId)
    {
        if (string.IsNullOrWhiteSpace(leftFactionId)
            || string.IsNullOrWhiteSpace(rightFactionId)
            || string.Equals(leftFactionId, rightFactionId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var left = _mapManager?.GetFaction(leftFactionId);
        return left?.GetRelationTo(rightFactionId) <= HostileFactionThreshold;
    }

    private static bool ShouldFlee(Entity npc, Entity threat, out string reason)
    {
        reason = "";
        var role = ResolveRole(npc);
        var hpRatio = GetHealthRatio(npc);
        var npcPower = CalculatePower(npc);
        var threatPower = CalculatePower(threat);
        var relativePower = npcPower / Math.Max(1f, threatPower);
        var threatWeaponPower = GetHeldWeaponPower(threat);
        var npcWeaponPower = GetHeldWeaponPower(npc);
        var threatArmed = threatWeaponPower > UnarmedWeaponThreshold;
        var npcUnarmed = npcWeaponPower <= UnarmedWeaponThreshold;

        if (role == CombatRole.Pacifist)
        {
            reason = "pacifist";
            return true;
        }

        if (role == CombatRole.Avenger)
        {
            if (hpRatio < 0.18f && relativePower < 1.75f)
            {
                reason = "critical_hp";
                return true;
            }

            return false;
        }

        if (role == CombatRole.Guard)
        {
            if (hpRatio < 0.25f)
            {
                reason = "low_hp";
                return true;
            }

            if (relativePower < 0.45f)
            {
                reason = "outmatched";
                return true;
            }

            return false;
        }

        if (hpRatio < 0.45f)
        {
            reason = "low_hp";
            return true;
        }

        // Раньше срабатывало почти всегда (relativePower < 1.25 при оружии у игрока).
        // Теперь: бежим только если у NPC нет HP-преимущества — иначе кулаками задавит.
        var npcHasHpEdge = HasHealthEdge(npc, threat);
        if (threatArmed && npcUnarmed && relativePower < 0.85f && !npcHasHpEdge)
        {
            reason = "unarmed_vs_weapon";
            return true;
        }

        if (relativePower < 0.75f && !npcHasHpEdge)
        {
            reason = "outmatched";
            return true;
        }

        return false;
    }

    private static bool HasHealthEdge(Entity npc, Entity player)
    {
        var npcHp = npc.GetComponent<HealthComponent>();
        var playerHp = player.GetComponent<HealthComponent>();
        if (npcHp == null || playerHp == null)
            return false;

        // NPC ощутимо здоровее: либо больше абсолютного HP минимум на 20%, либо явный перевес ratio.
        return npcHp.Health > playerHp.Health * 1.2f
            && GetHealthRatio(npc) > 0.6f;
    }

    private bool TryFindEscapeTarget(Entity npc, Entity threat, MapData map, TileMap tileMap, out Vector2 target)
    {
        target = Vector2.Zero;
        var npcPosition = npc.GetComponent<TransformComponent>()!.Position;
        var threatPosition = threat.GetComponent<TransformComponent>()!.Position;
        var from = tileMap.WorldToTile(npcPosition);
        var forbidden = BuildForbiddenTiles(npc, map);
        var transitionTiles = BuildLocationTransitionTiles(map);
        foreach (var point in transitionTiles)
            forbidden.Add(point);

        var distances = BuildEscapeReachability(tileMap, from, forbidden, maxNodes: EscapeSearchMaxNodes);
        var bestScore = float.MinValue;
        Point? best = null;
        foreach (var (point, pathDistance) in distances)
        {
            var radius = Math.Abs(point.X - from.X) + Math.Abs(point.Y - from.Y);
            if (radius < EscapeSearchRadiusMin || radius > EscapeSearchRadiusMax)
                continue;

            var world = new Vector2((point.X + 0.5f) * map.TileSize, (point.Y + 0.5f) * map.TileSize);
            var distanceFromThreat = Vector2.Distance(world, threatPosition);
            if (distanceFromThreat < SafeDistance * 0.4f)
                continue;

            var breaksSight = !tileMap.HasWorldLineOfSight(threatPosition, world);
            var score = distanceFromThreat + (breaksSight ? 240f : 0f) - pathDistance * 8f;
            if (score > bestScore)
            {
                bestScore = score;
                best = point;
            }
        }

        if (best == null)
            return false;

        target = new Vector2((best.Value.X + 0.5f) * map.TileSize, (best.Value.Y + 0.5f) * map.TileSize);
        return true;
    }

    private void StartFlee(Entity npc, Entity threat, Vector2 target, string reason)
    {
        var now = _clock!.TotalSecondsAbsolute;
        var threatPosition = threat.GetComponent<TransformComponent>()?.Position ?? target;
        var flee = npc.AddComponent(new NpcFleeComponent());
        flee.ThreatEntityId = threat.Id;
        flee.ThreatSaveId = threat.GetComponent<SaveEntityIdComponent>()?.SaveId ?? "";
        flee.Reason = reason;
        flee.LastThreatPosition = threatPosition;
        flee.EscapeTarget = target;
        flee.StartedAt = now;
        flee.LastSawThreatAt = now;
        flee.SafeForSeconds = 0f;
        flee.RepathCooldownSeconds = FleeRepathCooldownSeconds;

        Aggression.Clear(npc);

        var intent = npc.GetComponent<NpcIntentComponent>() ?? npc.AddComponent(new NpcIntentComponent());
        intent.Action = ScheduleAction.Visit;
        intent.SetTarget(_mapManager!.CurrentMap!.Id, target, "", "flee");

        var lines = new[] { "Сдаюсь!", "Надо сваливать!", "Не хочу умирать!", "Я ухожу!" };
        SpeechBubbleSystem.Show(npc, lines[_rng.Next(lines.Length)]);
        MarkDirty();
    }

    private void StartHostileAggression(Entity npc, Entity threat)
    {
        Aggression.MarkLethal(npc, threat, _clock!.TotalSecondsAbsolute);
        SpeechBubbleSystem.Show(npc, "Враг!");
        MarkDirty();
    }

    private static Dictionary<Point, int> BuildEscapeReachability(
        TileMap tileMap,
        Point from,
        HashSet<Point> blocked,
        int maxNodes)
    {
        var result = new Dictionary<Point, int>();
        if (!tileMap.IsInBounds(from.X, from.Y))
            return result;

        var queue = new Queue<Point>();
        result[from] = 0;
        queue.Enqueue(from);

        var dirs = new[]
        {
            new Point(1, 0),
            new Point(-1, 0),
            new Point(0, 1),
            new Point(0, -1)
        };

        while (queue.Count > 0 && result.Count < maxNodes)
        {
            var current = queue.Dequeue();
            var nextDistance = result[current] + 1;
            foreach (var dir in dirs)
            {
                var next = new Point(current.X + dir.X, current.Y + dir.Y);
                if (result.ContainsKey(next)
                    || !tileMap.IsInBounds(next.X, next.Y)
                    || tileMap.IsSolid(next.X, next.Y)
                    || blocked.Contains(next))
                {
                    continue;
                }

                result[next] = nextDistance;
                queue.Enqueue(next);
            }
        }

        return result;
    }

    private static CombatRole ResolveRole(Entity npc)
    {
        var personality = npc.GetComponent<PersonalityComponent>();
        if (personality?.Pacifist == true)
            return CombatRole.Pacifist;
        if (npc.GetComponent<AvengerComponent>() != null)
            return CombatRole.Avenger;

        var professionId = npc.GetComponent<ProfessionComponent>()?.ProfessionId ?? "";
        if (professionId.Contains("guard", StringComparison.OrdinalIgnoreCase)
            || professionId.Contains("watch", StringComparison.OrdinalIgnoreCase))
        {
            return CombatRole.Guard;
        }

        return CombatRole.Civilian;
    }

    private static float CalculatePower(Entity entity)
    {
        var health = entity.GetComponent<HealthComponent>();
        var hpRatio = GetHealthRatio(entity);
        var weaponPower = Math.Max(6f, GetHeldWeaponPower(entity));
        var skill = GetRelevantCombatSkill(entity, weaponPower);
        var fortitude = entity.GetComponent<SkillComponent>()?.Fortitude ?? 0f;
        return 12f + weaponPower * 1.8f + skill * 0.55f + fortitude * 0.25f + hpRatio * 36f
               + Math.Max(0f, (health?.Health ?? 0f) - 25f) * 0.1f;
    }

    private static float GetHealthRatio(Entity entity)
    {
        var health = entity.GetComponent<HealthComponent>();
        if (health == null || health.MaxHealth <= 0f)
            return 1f;

        return Math.Clamp(health.Health / health.MaxHealth, 0f, 1f);
    }

    private static float GetHeldWeaponPower(Entity entity)
    {
        var hands = entity.GetComponent<HandsComponent>();
        if (hands == null)
            return 0f;

        var best = 0f;
        foreach (var hand in hands.Hands)
        {
            var weapon = hand.HeldItem?.GetComponent<WeaponComponent>();
            if (weapon == null)
                continue;

            var damage = (weapon.EffectiveMinDamage + weapon.EffectiveMaxDamage) * 0.5f;
            best = Math.Max(best, damage + weapon.Range * 0.08f);
        }

        return best;
    }

    private static float GetRelevantCombatSkill(Entity entity, float weaponPower)
    {
        var skills = entity.GetComponent<SkillComponent>();
        if (skills == null)
            return 0f;

        if (weaponPower <= UnarmedWeaponThreshold)
            return skills.HandToHand;

        var hands = entity.GetComponent<HandsComponent>();
        var activeItem = hands?.ActiveItem;
        var item = activeItem?.GetComponent<ItemComponent>();
        return item?.TwoHanded == true ? skills.TwoHandedWeapons : skills.OneHandedWeapons;
    }

    private static HashSet<Point> BuildForbiddenTiles(Entity npc, MapData map)
    {
        var forbidden = new HashSet<Point>();
        var ownHouseId = npc.GetComponent<ResidenceComponent>()?.HouseId ?? "";
        foreach (var house in map.Areas.Where(area =>
                     string.Equals(area.Kind, AreaZoneKinds.House, StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(area.Id, ownHouseId, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var tile in house.Tiles)
                forbidden.Add(new Point(tile.X, tile.Y));
        }

        return forbidden;
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

    private static void MarkDirty()
    {
        if (ServiceLocator.Has<IWorldStateTracker>())
            ServiceLocator.Get<IWorldStateTracker>().MarkDirty();
    }

    private enum CombatRole
    {
        Civilian,
        Guard,
        Avenger,
        Pacifist
    }
}
