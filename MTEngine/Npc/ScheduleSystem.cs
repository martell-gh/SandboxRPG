using Microsoft.Xna.Framework;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Rendering;
using MTEngine.Systems;
using MTEngine.World;

namespace MTEngine.Npc;

/// <summary>
/// Resolves the current schedule slot into an NPC intent once per second.
/// Active-zone only for now; LOD filtering comes later.
/// </summary>
public class ScheduleSystem : GameSystem
{
    private GameClock _clock = null!;
    private WorldRegistry? _registry;
    private MapManager? _mapManager;
    private ScheduleTemplates? _templates;
    private PrototypeManager? _prototypes;

    private float _accumulator;
    private const float TickInterval = 1f;
    private const float SocialApproachTimeoutSeconds = 24f;
    private const float SocialTalkMinSeconds = 5f;
    private const float SocialTalkMaxSeconds = 9f;
    private const float SocialCooldownSeconds = 18f;
    private const float SocialPopupIntervalSeconds = 2.4f;
    private readonly Random _rng = new();
    private readonly Dictionary<int, WanderRouteState> _wanderRoutes = new();
    private readonly Dictionary<int, SocialConversationState> _socialByEntity = new();
    private readonly Dictionary<int, float> _socialCooldowns = new();
    private int _lastResolvedHour = -1;
    private string _conversationMapId = "";

    public override void OnInitialize()
    {
        _clock = ServiceLocator.Get<GameClock>();
    }

    public override void Update(float deltaTime)
    {
        if (!EnsureRuntimeServices())
            return;

        var map = _mapManager!.CurrentMap;
        if (map == null)
            return;

        UpdateSocialConversations(deltaTime, map);

        var currentHour = _clock.HourInt;
        var hourChanged = currentHour != _lastResolvedHour;

        _accumulator += deltaTime;
        if (_accumulator < TickInterval && !hourChanged)
            return;

        _accumulator = 0f;
        _lastResolvedHour = currentHour;

        UpdateSchedules(map);
    }

    public void RefreshNow()
    {
        if (!EnsureRuntimeServices())
            return;

        var map = _mapManager!.CurrentMap;
        if (map == null)
            return;

        _accumulator = 0f;
        _lastResolvedHour = _clock.HourInt;
        UpdateSchedules(map);
    }

    /// <summary>
    /// Used when a map has just been materialized from an unloaded/background state.
    /// NPCs should appear at their current schedule destination, not at stale spawn/home
    /// positions and then start walking only after the player opens the location.
    /// </summary>
    public void SettleCurrentMapNpcsFromBackground()
    {
        if (!EnsureRuntimeServices())
            return;

        var map = _mapManager!.CurrentMap;
        if (map == null)
            return;

        _accumulator = 0f;
        _lastResolvedHour = _clock.HourInt;
        UpdateSchedules(map);

        foreach (var entity in World.GetEntitiesWith<NpcTagComponent, TransformComponent>())
        {
            if (!NpcLod.IsActive(entity))
                continue;

            if (entity.GetComponent<HealthComponent>()?.IsDead == true)
                continue;

            var intent = entity.GetComponent<NpcIntentComponent>();
            if (intent == null
                || !intent.HasTarget
                || !string.Equals(intent.TargetMapId, map.Id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var transform = entity.GetComponent<TransformComponent>()!;
            transform.Position = intent.TargetPosition;
            intent.MarkArrived();

            if (entity.GetComponent<VelocityComponent>() is { } velocity)
                velocity.Velocity = Vector2.Zero;
            entity.GetComponent<SpriteComponent>()?.PlayDirectionalIdle(Vector2.Zero);
        }
    }

    private void UpdateSchedules(MapData map)
    {
        foreach (var entity in World.GetEntitiesWith<NpcTagComponent, ScheduleComponent>())
        {
            if (!NpcLod.IsActive(entity))
                continue;

            if (entity.GetComponent<NpcHealingComponent>() != null)
                continue;

            if (entity.GetComponent<NpcLocationTravelComponent>() != null)
                continue;

            if (_socialByEntity.ContainsKey(entity.Id))
                continue;

            var schedule = entity.GetComponent<ScheduleComponent>()!;
            if (schedule.Slots.Count == 0 && !string.IsNullOrEmpty(schedule.TemplateId))
                _templates?.Apply(schedule, schedule.TemplateId);

            var slot = schedule.FindSlot(_clock.HourInt);
            if (slot == null)
            {
                ClearIntent(entity);
                continue;
            }

            var intent = entity.GetComponent<NpcIntentComponent>() ?? entity.AddComponent(new NpcIntentComponent());
            var (action, targetAreaId) = ResolveAction(slot, schedule, entity);
            var concreteAreaId = ResolveAreaId(targetAreaId, entity, map);

            if (IsWanderLike(action)
                && intent.HasTarget
                && !intent.Arrived
                && intent.Action == action
                && (string.IsNullOrWhiteSpace(concreteAreaId)
                    || string.Equals(intent.TargetAreaId, concreteAreaId, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var advanceWander = IsWanderLike(action) && intent.Action == action && intent.Arrived;

            var target = ResolveTargetPosition(ref action, concreteAreaId, entity, map, advanceWander);
            intent.Action = action;
            if (target.HasValue)
                intent.SetTarget(map.Id, target.Value.Position, target.Value.AreaId, target.Value.PointId);
            else
                intent.ClearTarget();
        }
    }

    private bool EnsureRuntimeServices()
    {
        if (_registry == null && ServiceLocator.Has<WorldRegistry>())
            _registry = ServiceLocator.Get<WorldRegistry>();
        if (_mapManager == null && ServiceLocator.Has<MapManager>())
            _mapManager = ServiceLocator.Get<MapManager>();
        if (_templates == null && ServiceLocator.Has<ScheduleTemplates>())
            _templates = ServiceLocator.Get<ScheduleTemplates>();

        return _registry != null && _mapManager != null;
    }

    private (ScheduleAction action, string areaId) ResolveAction(ScheduleSlot slot, ScheduleComponent schedule, Entity entity)
    {
        if (slot.Action != ScheduleAction.Free)
        {
            if (MerchantWorkRules.IsInnkeeper(entity) && slot.Action == ScheduleAction.Sleep)
                return (ScheduleAction.StayInTavern, "$tavern");

            return (slot.Action, slot.TargetAreaId);
        }

        var isDay = _clock.IsDay;
        var options = schedule.Freetime
            .Where(o => (!o.DayOnly || isDay) && (!o.NightOnly || !isDay) && o.Conditions.Count == 0)
            .ToList();
        if (options.Count == 0)
            return (ScheduleAction.Wander, "");

        var maxPriority = options.Max(o => o.Priority);
        var top = options.Where(o => o.Priority == maxPriority).ToList();
        var pick = top[_rng.Next(top.Count)];
        return (pick.Action, pick.TargetAreaId);
    }

    private TargetResolution? ResolveTargetPosition(
        ref ScheduleAction action,
        string concreteAreaId,
        Entity entity,
        MapData map,
        bool advanceWander)
    {
        var residence = entity.GetComponent<ResidenceComponent>();
        var tileSize = map.TileSize;
        if (residence != null)
            NpcBedAssignment.EnsureAssigned(entity, _registry!, World);

        switch (action)
        {
            case ScheduleAction.Sleep:
                var overnight = TryResolveOvernightSleepTarget(entity, map, tileSize);
                if (overnight.HasValue)
                    return overnight;

                if (residence != null && _registry!.Houses.TryGetValue(residence.HouseId, out var sleepHouse))
                {
                    var slot = sleepHouse.BedSlots.FirstOrDefault(p =>
                        string.Equals(p.Id, residence.BedSlotId, StringComparison.OrdinalIgnoreCase))
                        ?? sleepHouse.BedSlots.FirstOrDefault();
                    if (slot != null)
                        return new TargetResolution(TileCenter(slot.X, slot.Y, tileSize), sleepHouse.Id, slot.Id);
                }

                // Бездомный/безработный: §7.4 — стабильно держим за NPC слот inn_bed_*.
                var innTarget = ResolveInnBedTarget(entity, map, concreteAreaId);
                if (innTarget.HasValue)
                    return innTarget;

                // Кровати нет (всё inn_bed_* занято или ничего не нашлось).
                // Не входим в визуальный сон без кровати: переключаем action на
                // StayInTavern/Wander и возвращаем точку шатания. Renderer.IsSleepingVisual
                // активирует pose только при Action == Sleep + Arrived, поэтому смена action
                // снимает «сон где стоит».
                var loiter = PickWanderPoint(entity, map, AreaZoneKinds.Tavern, "", advanceWander)
                             ?? PickWanderPoint(entity, map, AreaZoneKinds.Wander, "", advanceWander);
                if (loiter.HasValue)
                {
                    action = ScheduleAction.StayInTavern;
                    return loiter;
                }

                // Последний шанс: запасная кровать в Inn по точкам bed_slot_* — если есть, всё ещё Sleep.
                return ResolveAreaPointTarget(map, concreteAreaId, AreaZoneKinds.Inn, "bed_slot_");

            case ScheduleAction.EatAtHome:
                return ResolveMealTarget(entity, map, concreteAreaId, residence);

            case ScheduleAction.StayAtHome:
                if (residence != null && _registry!.Houses.TryGetValue(residence.HouseId, out var homeHouse)
                    && homeHouse.Tiles.Count > 0)
                {
                    var pick = homeHouse.Tiles[_rng.Next(homeHouse.Tiles.Count)];
                    return new TargetResolution(TileCenter(pick.X, pick.Y, tileSize), homeHouse.Id, "");
                }
                break;

            case ScheduleAction.Work:
                if (!string.IsNullOrEmpty(concreteAreaId)
                    && _registry!.Professions.TryGetValue(concreteAreaId, out var profession)
                    && profession.WorkAnchor != null)
                {
                    return new TargetResolution(
                        TileCenter(profession.WorkAnchor.X, profession.WorkAnchor.Y, tileSize),
                        profession.Id,
                        profession.WorkAnchor.Id);
                }
                break;

            case ScheduleAction.SchoolDay:
                return PickWanderPoint(entity, map, AreaZoneKinds.School, concreteAreaId, advanceWander)
                    ?? PickWanderPoint(entity, map, AreaZoneKinds.Wander, "", advanceWander);

            case ScheduleAction.StayInTavern:
                return PickWanderPoint(entity, map, AreaZoneKinds.Tavern, concreteAreaId, advanceWander)
                    ?? PickWanderPoint(entity, map, AreaZoneKinds.Wander, "", advanceWander);

            case ScheduleAction.Wander:
                if (string.IsNullOrWhiteSpace(concreteAreaId))
                    return PickLocationWanderPoint(entity, map);

                return PickWanderPoint(entity, map, AreaZoneKinds.Wander, concreteAreaId, advanceWander);

            case ScheduleAction.Socialize:
                var social = TryStartSocialConversation(entity, map);
                if (social.HasValue)
                    return social;

                action = ScheduleAction.Wander;
                return string.IsNullOrWhiteSpace(concreteAreaId)
                    ? PickLocationWanderPoint(entity, map)
                    : PickWanderPoint(entity, map, AreaZoneKinds.Wander, concreteAreaId, advanceWander);
        }

        return null;
    }

    private void UpdateSocialConversations(float deltaTime, MapData map)
    {
        if (!string.Equals(_conversationMapId, map.Id, StringComparison.OrdinalIgnoreCase))
        {
            _conversationMapId = map.Id;
            _socialByEntity.Clear();
        }

        DecaySocialCooldowns(deltaTime);

        if (_socialByEntity.Count == 0)
            return;

        foreach (var conversation in _socialByEntity.Values.Distinct().ToList())
        {
            var a = FindActiveNpc(conversation.NpcAId);
            var b = FindActiveNpc(conversation.NpcBId);
            if (a == null || b == null
                || !NpcLod.IsActive(a)
                || !NpcLod.IsActive(b)
                || a.GetComponent<HealthComponent>()?.IsDead == true
                || b.GetComponent<HealthComponent>()?.IsDead == true
                || !IsWithinSocialTime(a)
                || !IsWithinSocialTime(b))
            {
                EndSocialConversation(conversation, clearIntent: true);
                continue;
            }

            var intentA = a.GetComponent<NpcIntentComponent>();
            var intentB = b.GetComponent<NpcIntentComponent>();
            if (intentA?.Action != ScheduleAction.Socialize || intentB?.Action != ScheduleAction.Socialize)
            {
                EndSocialConversation(conversation, clearIntent: false);
                continue;
            }

            var arrivedAtAssignedTargets = IsAtConversationTarget(a, conversation.TargetA, intentA)
                                           && IsAtConversationTarget(b, conversation.TargetB, intentB);
            var alreadyAdjacentAndVisible = CanContinueSocialConversation(a, b, map);
            if (!arrivedAtAssignedTargets && !alreadyAdjacentAndVisible)
            {
                conversation.ApproachRemaining -= deltaTime;
                if (conversation.ApproachRemaining <= 0f)
                    EndSocialConversation(conversation, clearIntent: true);
                continue;
            }

            if (!alreadyAdjacentAndVisible)
            {
                EndSocialConversation(conversation, clearIntent: true);
                continue;
            }

            if (!arrivedAtAssignedTargets)
                PinConversationAtCurrentPositions(conversation, a, b, map, intentA, intentB);

            FaceEachOther(a, b);
            StopNpc(a);
            StopNpc(b);

            if (!conversation.Started)
            {
                conversation.Started = true;
                conversation.Remaining = SocialTalkMinSeconds
                                         + (float)_rng.NextDouble() * (SocialTalkMaxSeconds - SocialTalkMinSeconds);
                conversation.PopupCooldown = 0f;
            }

            conversation.Remaining -= deltaTime;
            conversation.PopupCooldown -= deltaTime;
            if (conversation.PopupCooldown <= 0f)
            {
                ShowConversationPopup(conversation, a, b);
                conversation.PopupCooldown = SocialPopupIntervalSeconds;
            }

            if (conversation.Remaining <= 0f)
                EndSocialConversation(conversation, clearIntent: true);
        }
    }

    private void DecaySocialCooldowns(float deltaTime)
    {
        if (_socialCooldowns.Count == 0)
            return;

        foreach (var entityId in _socialCooldowns.Keys.ToList())
        {
            var next = _socialCooldowns[entityId] - deltaTime;
            if (next <= 0f)
                _socialCooldowns.Remove(entityId);
            else
                _socialCooldowns[entityId] = next;
        }
    }

    private TargetResolution? TryStartSocialConversation(Entity initiator, MapData map)
    {
        if (_socialByEntity.ContainsKey(initiator.Id) || _socialCooldowns.ContainsKey(initiator.Id))
            return null;

        var partner = PickSocialPartner(initiator);
        if (partner == null)
            return null;

        if (!TryResolveSocialTargets(initiator, partner, map, out var initiatorTarget, out var partnerTarget))
            return null;

        var state = new SocialConversationState
        {
            NpcAId = initiator.Id,
            NpcBId = partner.Id,
            TargetA = initiatorTarget,
            TargetB = partnerTarget,
            ApproachRemaining = SocialApproachTimeoutSeconds
        };

        _socialByEntity[initiator.Id] = state;
        _socialByEntity[partner.Id] = state;

        var partnerIntent = partner.GetComponent<NpcIntentComponent>() ?? partner.AddComponent(new NpcIntentComponent());
        partnerIntent.Action = ScheduleAction.Socialize;
        partnerIntent.SetTarget(map.Id, partnerTarget, "socialize", $"talk:{initiator.Id}");

        return new TargetResolution(initiatorTarget, "socialize", $"talk:{partner.Id}");
    }

    private Entity? PickSocialPartner(Entity initiator)
    {
        var candidates = World.GetEntitiesWith<NpcTagComponent, TransformComponent>()
            .Where(other => other != initiator
                            && NpcLod.IsActive(other)
                            && other.HasComponent<ScheduleComponent>()
                            && !_socialByEntity.ContainsKey(other.Id)
                            && !_socialCooldowns.ContainsKey(other.Id)
                            && other.GetComponent<HealthComponent>()?.IsDead != true
                            && IsWithinSocialTime(other))
            .ToList();

        if (candidates.Count == 0)
            return null;

        var initiatorPosition = initiator.GetComponent<TransformComponent>()!.Position;
        var near = candidates
            .OrderBy(other => Vector2.DistanceSquared(initiatorPosition, other.GetComponent<TransformComponent>()!.Position))
            .Take(Math.Min(6, candidates.Count))
            .ToList();

        return near[_rng.Next(near.Count)];
    }

    private bool IsWithinSocialTime(Entity entity)
    {
        var schedule = entity.GetComponent<ScheduleComponent>();
        var slot = schedule?.FindSlot(_clock.HourInt);
        if (slot == null)
            return false;

        return slot.Action is ScheduleAction.Free or ScheduleAction.Wander or ScheduleAction.StayInTavern;
    }

    private bool TryResolveSocialTargets(Entity a, Entity b, MapData map, out Vector2 targetA, out Vector2 targetB)
    {
        targetA = targetB = Vector2.Zero;
        var tileMap = _mapManager?.CurrentTileMap;
        if (tileMap == null)
            return false;

        var posA = a.GetComponent<TransformComponent>()!.Position;
        var posB = b.GetComponent<TransformComponent>()!.Position;
        var center = tileMap.WorldToTile(Vector2.Lerp(posA, posB, 0.5f));
        var forbidden = BuildLocationWanderForbiddenTiles(a, map);
        foreach (var point in BuildLocationWanderForbiddenTiles(b, map))
            forbidden.Add(point);

        foreach (var tile in EnumerateTilesAround(center, radius: 8))
        {
            var pairs = new (Point A, Point B)[]
            {
                (tile, new Point(tile.X + 1, tile.Y)),
                (tile, new Point(tile.X - 1, tile.Y)),
                (tile, new Point(tile.X, tile.Y + 1)),
                (tile, new Point(tile.X, tile.Y - 1))
            };

            foreach (var pair in pairs)
            {
                if (!IsSocialTileWalkable(pair.A, tileMap, forbidden, map.TileSize, a, b)
                    || !IsSocialTileWalkable(pair.B, tileMap, forbidden, map.TileSize, a, b))
                {
                    continue;
                }

                targetA = TileCenter(pair.A.X, pair.A.Y, map.TileSize);
                targetB = TileCenter(pair.B.X, pair.B.Y, map.TileSize);
                if (!EntityOcclusionHelper.HasWorldLineOfSight(tileMap, World, targetA, targetB))
                    continue;

                return true;
            }
        }

        return false;
    }

    private bool CanContinueSocialConversation(Entity a, Entity b, MapData map)
    {
        var tileMap = _mapManager?.CurrentTileMap;
        if (tileMap == null)
            return false;

        var posA = a.GetComponent<TransformComponent>()?.Position;
        var posB = b.GetComponent<TransformComponent>()?.Position;
        if (!posA.HasValue || !posB.HasValue)
            return false;

        var tileA = tileMap.WorldToTile(posA.Value);
        var tileB = tileMap.WorldToTile(posB.Value);
        if (Math.Abs(tileA.X - tileB.X) + Math.Abs(tileA.Y - tileB.Y) != 1)
            return false;

        return EntityOcclusionHelper.HasWorldLineOfSight(tileMap, World, posA.Value, posB.Value);
    }

    private static IEnumerable<Point> EnumerateTilesAround(Point center, int radius)
    {
        yield return center;
        for (var r = 1; r <= radius; r++)
        {
            for (var y = center.Y - r; y <= center.Y + r; y++)
            {
                for (var x = center.X - r; x <= center.X + r; x++)
                {
                    if (Math.Abs(x - center.X) != r && Math.Abs(y - center.Y) != r)
                        continue;
                    yield return new Point(x, y);
                }
            }
        }
    }

    private bool IsSocialTileWalkable(Point tile, TileMap tileMap, HashSet<Point> forbidden, int tileSize, Entity npcA, Entity npcB)
        => tileMap.IsInBounds(tile.X, tile.Y)
           && !tileMap.IsSolid(tile.X, tile.Y)
           && !forbidden.Contains(tile)
           && !IsTileBlockedByEntity(tile, tileSize, npcA, npcB);

    private static bool IsAtConversationTarget(Entity entity, Vector2 target, NpcIntentComponent intent)
    {
        if (intent.Arrived)
            return true;

        var transform = entity.GetComponent<TransformComponent>();
        return transform != null && Vector2.DistanceSquared(transform.Position, target) <= 36f;
    }

    private static void PinConversationAtCurrentPositions(
        SocialConversationState conversation,
        Entity a,
        Entity b,
        MapData map,
        NpcIntentComponent intentA,
        NpcIntentComponent intentB)
    {
        var posA = a.GetComponent<TransformComponent>()?.Position;
        var posB = b.GetComponent<TransformComponent>()?.Position;
        if (!posA.HasValue || !posB.HasValue)
            return;

        conversation.TargetA = posA.Value;
        conversation.TargetB = posB.Value;
        intentA.SetTarget(map.Id, posA.Value, "socialize", $"talk:{b.Id}");
        intentB.SetTarget(map.Id, posB.Value, "socialize", $"talk:{a.Id}");
        intentA.MarkArrived();
        intentB.MarkArrived();
    }

    private void EndSocialConversation(SocialConversationState conversation, bool clearIntent)
    {
        _socialByEntity.Remove(conversation.NpcAId);
        _socialByEntity.Remove(conversation.NpcBId);
        _socialCooldowns[conversation.NpcAId] = SocialCooldownSeconds;
        _socialCooldowns[conversation.NpcBId] = SocialCooldownSeconds;

        if (!clearIntent)
            return;

        foreach (var entityId in new[] { conversation.NpcAId, conversation.NpcBId })
        {
            var npc = FindActiveNpc(entityId);
            var intent = npc?.GetComponent<NpcIntentComponent>();
            if (intent?.Action == ScheduleAction.Socialize)
                intent.ClearTarget();
        }
    }

    private Entity? FindActiveNpc(int entityId)
        => World.GetEntities()
            .FirstOrDefault(entity => entity.Active && entity.Id == entityId && entity.HasComponent<NpcTagComponent>());

    private static void FaceEachOther(Entity a, Entity b)
    {
        var posA = a.GetComponent<TransformComponent>()?.Position;
        var posB = b.GetComponent<TransformComponent>()?.Position;
        if (!posA.HasValue || !posB.HasValue)
            return;

        a.GetComponent<SpriteComponent>()?.PlayDirectionalIdle(posB.Value - posA.Value);
        b.GetComponent<SpriteComponent>()?.PlayDirectionalIdle(posA.Value - posB.Value);
    }

    private static void StopNpc(Entity entity)
    {
        if (entity.GetComponent<VelocityComponent>() is { } velocity)
            velocity.Velocity = Vector2.Zero;
    }

    private void ShowConversationPopup(SocialConversationState conversation, Entity a, Entity b)
    {
        var speaker = conversation.NextSpeakerA ? a : b;
        conversation.NextSpeakerA = !conversation.NextSpeakerA;

        var lines = new[] { "Привет.", "Как дела?", "Слышал новости?", "Хороший день.", "Увидимся." };
        SpeechBubbleSystem.Show(speaker, lines[_rng.Next(lines.Length)]);
    }

    private string ResolveAreaId(string placeholder, Entity entity, MapData map)
    {
        if (string.IsNullOrEmpty(placeholder) || !placeholder.StartsWith("$"))
            return placeholder;

        var residence = entity.GetComponent<ResidenceComponent>();
        return placeholder switch
        {
            "$house" => residence?.HouseId ?? "",
            "$profession" => entity.GetComponent<ProfessionComponent>()?.SlotId ?? "",
            "$inn" => FindFirstAreaId(map, AreaZoneKinds.Inn),
            "$school" => FindFirstAreaId(map, AreaZoneKinds.School),
            "$tavern" => FindFirstAreaId(map, AreaZoneKinds.Tavern),
            "$wander" => FindFirstAreaId(map, AreaZoneKinds.Wander),
            _ => ""
        };
    }

    private TargetResolution? TryResolveOvernightSleepTarget(Entity entity, MapData map, int tileSize)
    {
        var relationships = entity.GetComponent<RelationshipsComponent>();
        if (relationships == null
            || relationships.PartnerIsPlayer
            || string.IsNullOrWhiteSpace(relationships.PartnerNpcSaveId)
            || relationships.Status is not (RelationshipStatus.Dating or RelationshipStatus.Engaged or RelationshipStatus.Married))
        {
            return null;
        }

        var selfMarker = entity.GetComponent<SaveEntityIdComponent>();
        if (selfMarker == null || string.IsNullOrWhiteSpace(selfMarker.SaveId))
            return null;

        var partner = FindNpcBySaveId(relationships.PartnerNpcSaveId);
        if (partner == null)
            return null;

        var partnerRelationships = partner.GetComponent<RelationshipsComponent>();
        if (partnerRelationships == null
            || partnerRelationships.PartnerIsPlayer
            || !string.Equals(partnerRelationships.PartnerNpcSaveId, selfMarker.SaveId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var partnerMarker = partner.GetComponent<SaveEntityIdComponent>();
        if (partnerMarker == null || string.IsNullOrWhiteSpace(partnerMarker.SaveId))
            return null;

        var nightIndex = GetSleepNightIndex();
        if (StableUnitRandom($"overnight-roll|{PairKey(selfMarker.SaveId, partnerMarker.SaveId)}|{nightIndex}") > GetOvernightChance(relationships, nightIndex))
            return null;

        var selfResidence = entity.GetComponent<ResidenceComponent>();
        var partnerResidence = partner.GetComponent<ResidenceComponent>();
        var selfHouse = ResolveResidentHouse(selfResidence, map);
        var partnerHouse = ResolveResidentHouse(partnerResidence, map);
        if (selfHouse == null && partnerHouse == null)
            return null;

        var hostSaveId = PickOvernightHost(selfMarker.SaveId, partnerMarker.SaveId, nightIndex);
        var selfIsHost = string.Equals(hostSaveId, selfMarker.SaveId, StringComparison.OrdinalIgnoreCase);
        var hostHouse = selfIsHost ? selfHouse : partnerHouse;
        var hostResidence = selfIsHost ? selfResidence : partnerResidence;

        if (hostHouse == null)
        {
            selfIsHost = !selfIsHost;
            hostHouse = selfIsHost ? selfHouse : partnerHouse;
            hostResidence = selfIsHost ? selfResidence : partnerResidence;
        }

        if (hostHouse == null)
            return null;

        if (selfIsHost)
            return ResolveHouseSleepSlot(hostHouse, hostResidence?.BedSlotId ?? "", "", tileSize);

        return ResolveHouseSleepSlot(hostHouse, "", hostResidence?.BedSlotId ?? "", tileSize);
    }

    private Entity? FindNpcBySaveId(string saveId)
    {
        foreach (var npc in World.GetEntitiesWith<NpcTagComponent, RelationshipsComponent>())
        {
            var marker = npc.GetComponent<SaveEntityIdComponent>();
            if (marker != null && string.Equals(marker.SaveId, saveId, StringComparison.OrdinalIgnoreCase))
                return npc;
        }

        return null;
    }

    private HouseDef? ResolveResidentHouse(ResidenceComponent? residence, MapData map)
    {
        if (residence == null || string.IsNullOrWhiteSpace(residence.HouseId))
            return null;

        if (!_registry!.Houses.TryGetValue(residence.HouseId, out var house))
            return null;

        return string.Equals(house.MapId, map.Id, StringComparison.OrdinalIgnoreCase)
            ? house
            : null;
    }

    private static TargetResolution? ResolveHouseSleepSlot(
        HouseDef house,
        string preferredPointId,
        string avoidPointId,
        int tileSize)
    {
        AreaPointData? slot = null;
        if (!string.IsNullOrWhiteSpace(preferredPointId))
        {
            slot = house.BedSlots.FirstOrDefault(p =>
                string.Equals(p.Id, preferredPointId, StringComparison.OrdinalIgnoreCase));
        }

        if (slot == null && !string.IsNullOrWhiteSpace(avoidPointId))
        {
            slot = house.BedSlots.FirstOrDefault(p =>
                !string.Equals(p.Id, avoidPointId, StringComparison.OrdinalIgnoreCase));
        }

        slot ??= house.BedSlots.FirstOrDefault();
        if (slot != null)
            return new TargetResolution(TileCenter(slot.X, slot.Y, tileSize), house.Id, slot.Id);

        var tile = house.Tiles.FirstOrDefault();
        return tile == null
            ? null
            : new TargetResolution(TileCenter(tile.X, tile.Y, tileSize), house.Id, "");
    }

    private long GetSleepNightIndex()
        => _clock.HourInt < 12 ? Math.Max(0L, _clock.DayIndex - 1L) : _clock.DayIndex;

    private static double GetOvernightChance(RelationshipsComponent relationships, long nightIndex)
    {
        if (relationships.Status == RelationshipStatus.Married)
            return 1.0;

        var started = relationships.DatingStartedDayIndex >= 0L
            ? relationships.DatingStartedDayIndex
            : nightIndex;
        var wedding = relationships.ScheduledWeddingDayIndex > started
            ? relationships.ScheduledWeddingDayIndex
            : started + 90L;

        var progress = (double)(nightIndex - started) / Math.Max(1L, wedding - started);
        return Math.Clamp(progress, 0.10, 0.95);
    }

    private static string PickOvernightHost(string saveIdA, string saveIdB, long nightIndex)
    {
        var first = string.Compare(saveIdA, saveIdB, StringComparison.OrdinalIgnoreCase) <= 0 ? saveIdA : saveIdB;
        var second = string.Equals(first, saveIdA, StringComparison.OrdinalIgnoreCase) ? saveIdB : saveIdA;
        return StableUnitRandom($"overnight-host|{PairKey(saveIdA, saveIdB)}|{nightIndex}") < 0.5
            ? first
            : second;
    }

    private static string PairKey(string a, string b)
        => string.Compare(a, b, StringComparison.OrdinalIgnoreCase) <= 0
            ? $"{a}|{b}"
            : $"{b}|{a}";

    private static double StableUnitRandom(string key)
    {
        var hash = StableHash(key);
        return (hash & 0x00FFFFFF) / (double)0x01000000;
    }

    private static uint StableHash(string text)
    {
        const uint offset = 2166136261u;
        const uint prime = 16777619u;

        var hash = offset;
        foreach (var ch in text)
        {
            hash ^= char.ToLowerInvariant(ch);
            hash *= prime;
        }

        return hash;
    }

    /// <summary>
    /// §7.4: подобрать свободный <c>inn_bed_*</c> или авто-кровать внутри Inn для бездомного NPC и закрепить его в
    /// <see cref="ResidenceComponent"/>. Если все слоты заняты — возвращает null
    /// (вызывающий код уведёт NPC в Tavern/Wander).
    /// </summary>
    private TargetResolution? ResolveInnBedTarget(Entity entity, MapData map, string innAreaId)
    {
        var area = ResolveAreas(map, AreaZoneKinds.Inn, innAreaId).FirstOrDefault();
        if (area == null)
            return null;

        var slots = GetInnBedPoints(area, map).ToList();
        if (slots.Count == 0)
            return null;

        var residence = entity.GetComponent<ResidenceComponent>();
        var marker = entity.GetComponent<SaveEntityIdComponent>();
        var selfSaveId = marker?.SaveId ?? "";

        // Если уже закреплён за этим Inn — возвращаем тот же слот.
        if (residence != null
            && string.Equals(residence.HouseId, area.Id, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(residence.BedSlotId))
        {
            var existing = slots.FirstOrDefault(s => string.Equals(s.Id, residence.BedSlotId, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
                return new TargetResolution(TileCenter(existing.X, existing.Y, map.TileSize), area.Id, existing.Id);
        }

        var occupied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var npc in World.GetEntitiesWith<NpcTagComponent, ResidenceComponent>())
        {
            if (npc == entity) continue;
            var otherMarker = npc.GetComponent<SaveEntityIdComponent>();
            if (otherMarker != null && string.Equals(otherMarker.SaveId, selfSaveId, StringComparison.OrdinalIgnoreCase))
                continue;
            var r = npc.GetComponent<ResidenceComponent>()!;
            if (string.Equals(r.HouseId, area.Id, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(r.BedSlotId))
            {
                occupied.Add(r.BedSlotId);
            }
        }

        var free = slots.FirstOrDefault(s => !occupied.Contains(s.Id));
        if (free == null)
            return null;

        residence ??= entity.AddComponent(new ResidenceComponent());
        residence.HouseId = area.Id;
        residence.BedSlotId = free.Id;

        return new TargetResolution(TileCenter(free.X, free.Y, map.TileSize), area.Id, free.Id);
    }

    private IEnumerable<AreaPointData> GetInnBedPoints(AreaZoneData area, MapData map)
    {
        var points = area.GetPointsByPrefix("inn_bed_").ToList();

        _prototypes ??= ServiceLocator.Has<PrototypeManager>() ? ServiceLocator.Get<PrototypeManager>() : null;
        if (_prototypes != null)
            points.AddRange(HouseBedScanner.EnumerateAutoBedPoints(area, map, _prototypes));

        foreach (var point in points
                     .GroupBy(point => point.Id, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First())
                     .OrderBy(point => point.Id, StringComparer.OrdinalIgnoreCase))
        {
            yield return point;
        }
    }

    private TargetResolution? ResolveAreaPointTarget(MapData map, string areaId, string kind, string pointPrefix)
    {
        var area = ResolveAreas(map, kind, areaId).FirstOrDefault();
        if (area == null)
            return null;

        var points = area.GetPointsByPrefix(pointPrefix)
            .OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (points.Count > 0)
        {
            var point = points[0];
            return new TargetResolution(TileCenter(point.X, point.Y, map.TileSize), area.Id, point.Id);
        }

        var tile = area.Tiles.FirstOrDefault();
        return tile == null
            ? null
            : new TargetResolution(TileCenter(tile.X, tile.Y, map.TileSize), area.Id, "");
    }

    private TargetResolution? ResolveMealTarget(Entity entity, MapData map, string concreteAreaId, ResidenceComponent? residence)
    {
        // Eating is social by default: if the location has a tavern/inn, people go there.
        // The old action name stays EatAtHome for save/template compatibility.
        var tavernAreaId = IsAreaOfKind(map, concreteAreaId, AreaZoneKinds.Tavern) ? concreteAreaId : "";
        var innAreaId = IsAreaOfKind(map, concreteAreaId, AreaZoneKinds.Inn) ? concreteAreaId : "";
        return ResolveAreaActivityTarget(entity, map, tavernAreaId, AreaZoneKinds.Tavern)
            ?? ResolveAreaActivityTarget(entity, map, innAreaId, AreaZoneKinds.Inn)
            ?? ResolveHomeActivityTarget(entity, map, residence);
    }

    private TargetResolution? ResolveHomeActivityTarget(Entity entity, MapData map, ResidenceComponent? residence)
    {
        if (residence == null || !_registry!.Houses.TryGetValue(residence.HouseId, out var house))
            return null;

        if (!string.Equals(house.MapId, map.Id, StringComparison.OrdinalIgnoreCase))
            return null;

        var tileMap = _mapManager?.CurrentTileMap;
        var preferredPoints = house.BedSlots
            .Where(p => IsTargetTileUsable(new Point(p.X, p.Y), map.TileSize, tileMap, entity))
            .OrderBy(p => string.Equals(p.Id, residence.BedSlotId, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var point = preferredPoints.FirstOrDefault();
        if (point != null)
            return new TargetResolution(TileCenter(point.X, point.Y, map.TileSize), house.Id, point.Id);

        return PickUsableAreaTile(entity, map, house.Id, house.Tiles);
    }

    private TargetResolution? ResolveAreaActivityTarget(Entity entity, MapData map, string areaId, string kind)
    {
        var area = ResolveAreas(map, kind, areaId).FirstOrDefault();
        if (area == null)
            return null;

        var tileMap = _mapManager?.CurrentTileMap;
        foreach (var prefix in new[] { "eat_", "table_", "seat_", "wander_", "work_anchor" })
        {
            var point = string.Equals(prefix, "work_anchor", StringComparison.OrdinalIgnoreCase)
                ? area.GetPoint("work_anchor")
                : area.GetPointsByPrefix(prefix).OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault();

            if (point == null)
                continue;

            var tile = new Point(point.X, point.Y);
            if (IsTargetTileUsable(tile, map.TileSize, tileMap, entity))
                return new TargetResolution(TileCenter(point.X, point.Y, map.TileSize), area.Id, point.Id);
        }

        return PickUsableAreaTile(entity, map, area.Id, area.Tiles);
    }

    private TargetResolution? PickWanderPoint(Entity entity, MapData map, string kind, string areaId, bool advance)
    {
        var areas = ResolveAreas(map, kind, areaId).ToList();
        if (areas.Count == 0)
            return null;

        var routeKey = $"{kind}|{areaId}";
        if (!_wanderRoutes.TryGetValue(entity.Id, out var state)
            || !string.Equals(state.RouteKey, routeKey, StringComparison.OrdinalIgnoreCase)
            || areas.All(a => !string.Equals(a.Id, state.AreaId, StringComparison.OrdinalIgnoreCase)))
        {
            var startArea = areas[_rng.Next(areas.Count)];
            state = new WanderRouteState { RouteKey = routeKey, AreaId = startArea.Id, PointIndex = 0 };
        }
        else if (advance)
        {
            state.PointIndex++;
        }

        var area = areas.First(a => string.Equals(a.Id, state.AreaId, StringComparison.OrdinalIgnoreCase));
        var transitionTiles = BuildTransitionTileSet(map);
        var points = area.GetPointsByPrefix("wander_")
            .Where(p => !transitionTiles.Contains(new Point(p.X, p.Y)))
            .OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _wanderRoutes[entity.Id] = state;

        if (points.Count > 0)
        {
            var point = points[Math.Abs(state.PointIndex) % points.Count];
            return new TargetResolution(TileCenter(point.X, point.Y, map.TileSize), area.Id, point.Id);
        }

        if (area.Tiles.Count == 0)
            return null;

        var tiles = area.Tiles
            .Where(t => !transitionTiles.Contains(new Point(t.X, t.Y)))
            .OrderBy(t => t.X).ThenBy(t => t.Y)
            .ToList();
        if (tiles.Count == 0)
            return null;
        var tile = tiles[Math.Abs(state.PointIndex) % tiles.Count];
        return new TargetResolution(TileCenter(tile.X, tile.Y, map.TileSize), area.Id, "");
    }

    /// <summary>Trigger tiles of LocationTransition kind — must never be a wander/idle target.</summary>
    private static HashSet<Point> BuildTransitionTileSet(MapData map)
    {
        var set = new HashSet<Point>();
        foreach (var trigger in map.Triggers)
        {
            if (!string.Equals(trigger.Action.Type, TriggerActionTypes.LocationTransition, StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (var tile in trigger.Tiles)
                set.Add(new Point(tile.X, tile.Y));
        }
        return set;
    }

    private TargetResolution? PickUsableAreaTile(Entity entity, MapData map, string areaId, IReadOnlyList<TriggerTile> tiles)
    {
        var tileMap = _mapManager?.CurrentTileMap;
        var transitionTiles = BuildTransitionTileSet(map);
        var candidates = tiles
            .Select(t => new Point(t.X, t.Y))
            .Where(tile => !transitionTiles.Contains(tile))
            .Where(tile => IsTargetTileUsable(tile, map.TileSize, tileMap, entity))
            .OrderBy(tile => StableHash($"{areaId}|{tile.X}|{tile.Y}"))
            .ToList();

        if (candidates.Count == 0)
            return null;

        var stableKey = GetStableNpcKey(entity);
        var index = (int)(StableHash($"area-target|{stableKey}|{areaId}|{_clock.DayIndex}|{_clock.HourInt}") % candidates.Count);
        var pick = candidates[index];
        return new TargetResolution(TileCenter(pick.X, pick.Y, map.TileSize), areaId, "");
    }

    private bool IsTargetTileUsable(Point tile, int tileSize, TileMap? tileMap, Entity entity)
    {
        if (tileMap?.IsSolid(tile.X, tile.Y) == true)
            return false;

        return !IsTileBlockedByEntity(tile, tileSize, entity);
    }

    private TargetResolution? PickLocationWanderPoint(Entity entity, MapData map)
    {
        var tileMap = _mapManager?.CurrentTileMap;
        var forbidden = BuildLocationWanderForbiddenTiles(entity, map);
        var candidates = new List<Point>();

        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var point = new Point(x, y);
                if (forbidden.Contains(point))
                    continue;

                if (tileMap?.IsSolid(x, y) == true)
                    continue;

                if (IsTileBlockedByEntity(point, map.TileSize, entity))
                    continue;

                candidates.Add(point);
            }
        }

        if (candidates.Count == 0)
            return null;

        var pick = candidates[_rng.Next(candidates.Count)];
        return new TargetResolution(TileCenter(pick.X, pick.Y, map.TileSize), "", "location_wander");
    }

    private static HashSet<Point> BuildLocationWanderForbiddenTiles(Entity entity, MapData map)
    {
        var forbidden = new HashSet<Point>();
        var ownHouseId = entity.GetComponent<ResidenceComponent>()?.HouseId ?? "";

        foreach (var house in map.Areas.Where(area =>
                     string.Equals(area.Kind, AreaZoneKinds.House, StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(area.Id, ownHouseId, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var tile in house.Tiles)
                forbidden.Add(new Point(tile.X, tile.Y));
        }

        foreach (var trigger in map.Triggers.Where(trigger =>
                     string.Equals(trigger.Action.Type, TriggerActionTypes.LocationTransition, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var tile in trigger.Tiles)
                forbidden.Add(new Point(tile.X, tile.Y));
        }

        return forbidden;
    }

    private bool IsTileBlockedByEntity(Point tile, int tileSize, params Entity[] ignored)
    {
        var tileRect = new Rectangle(tile.X * tileSize, tile.Y * tileSize, tileSize, tileSize);

        foreach (var entity in World.GetEntities())
        {
            if (ignored.Contains(entity))
                continue;

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

    private static IEnumerable<AreaZoneData> ResolveAreas(MapData map, string kind, string areaId)
    {
        var areas = map.Areas
            .Where(a => string.Equals(a.Kind, kind, StringComparison.OrdinalIgnoreCase) && a.Tiles.Count > 0);

        if (!string.IsNullOrWhiteSpace(areaId))
            areas = areas.Where(a => string.Equals(a.Id, areaId, StringComparison.OrdinalIgnoreCase));

        return areas.OrderBy(a => a.Id, StringComparer.OrdinalIgnoreCase);
    }

    private static Vector2 TileCenter(int x, int y, int tileSize)
        => new((x + 0.5f) * tileSize, (y + 0.5f) * tileSize);

    private static string FindFirstAreaId(MapData map, string kind)
        => map.Areas
            .Where(a => string.Equals(a.Kind, kind, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Id, StringComparer.OrdinalIgnoreCase)
            .Select(a => a.Id)
            .FirstOrDefault() ?? "";

    private static bool IsAreaOfKind(MapData map, string areaId, string kind)
        => !string.IsNullOrWhiteSpace(areaId)
           && map.Areas.Any(a =>
               string.Equals(a.Id, areaId, StringComparison.OrdinalIgnoreCase)
               && string.Equals(a.Kind, kind, StringComparison.OrdinalIgnoreCase));

    private static string GetStableNpcKey(Entity entity)
        => entity.GetComponent<SaveEntityIdComponent>()?.SaveId is { Length: > 0 } saveId
            ? saveId
            : entity.Id.ToString();

    private static bool IsWanderLike(ScheduleAction action)
        => action is ScheduleAction.Wander or ScheduleAction.SchoolDay or ScheduleAction.StayInTavern;

    private static void ClearIntent(Entity entity)
    {
        var intent = entity.GetComponent<NpcIntentComponent>();
        intent?.ClearTarget();
    }

    private readonly struct TargetResolution
    {
        public Vector2 Position { get; }
        public string AreaId { get; }
        public string PointId { get; }

        public TargetResolution(Vector2 position, string areaId, string pointId)
        {
            Position = position;
            AreaId = areaId;
            PointId = pointId;
        }
    }

    private sealed class WanderRouteState
    {
        public string RouteKey { get; set; } = "";
        public string AreaId { get; set; } = "";
        public int PointIndex { get; set; }
    }

    private sealed class SocialConversationState
    {
        public int NpcAId { get; set; }
        public int NpcBId { get; set; }
        public Vector2 TargetA { get; set; }
        public Vector2 TargetB { get; set; }
        public float ApproachRemaining { get; set; }
        public bool Started { get; set; }
        public float Remaining { get; set; }
        public float PopupCooldown { get; set; }
        public bool NextSpeakerA { get; set; } = true;
    }
}
