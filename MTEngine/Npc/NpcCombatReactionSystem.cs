using Microsoft.Xna.Framework;
using MTEngine.Combat;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Systems;
using MTEngine.World;
using MTEngine.Wounds;

namespace MTEngine.Npc;

/// <summary>
/// Боевая реакция NPC: преследование, удары, разагривание.
///
/// Триггеры:
///   1) Игрок ударил NPC ⇒ NPC агрится в ответ. Если до этого уже стоял в Restrained
///      (т.е. это «повторное нападение») — эскалируется в Lethal.
///   2) HomeIntrusionSystem помечает агрессию: уже спит ⇒ Lethal, эскорт-таймаут ⇒ Restrained.
///
/// Поведение:
///   • Каждый кадр: видит ли NPC цель (дистанция + FOV + line-of-sight). Если видит — обновляет позицию.
///   • Если не видит уже &gt; LoseSightSeconds — стоит ещё DisengageLingerSeconds, потом снимает агрессию.
///   • Idle-фаза (после потери зрения): стоит на месте, не атакует.
///   • HomeIntrusion без удара игрока заканчивается сразу, когда игрок вышел из защищаемого дома.
///   • Restrained: не бьёт цель, у которой эффективный HP ≤ floor (20, или 5 при истощении).
///   • Lethal: бьёт без ограничений.
///   • Внутри своего дома против нарушителя агрессия всегда Lethal.
///   • PlayerOpinion снижается ограниченно за один конфликт, а не за каждый damage event.
/// </summary>
public class NpcCombatReactionSystem : GameSystem
{
    private GameClock? _clock;
    private MapManager? _mapManager;
    private WorldRegistry? _registry;
    private CombatSystem? _combatSystem;
    private EventBus? _bus;
    private bool _subscribed;

    private const float LoseSightSeconds = 5f;
    private const float DisengageLingerSeconds = 3f;
    private const float HpFloorNormal = 20f;
    private const float HpFloorExhausted = 5f;
    private const float ExhaustionThreshold = 0.5f;
    private const int InitialAssaultOpinionPenalty = 6;
    private const int OngoingAssaultOpinionPenalty = 2;
    private const int MaxOpinionPenaltyPerConflict = 12;
    private const double OpinionPenaltyCooldownSeconds = 20d;
    /// <summary>В режиме «предупреждения» NPC отвечает не больше этого числа раз и успокаивается.</summary>
    private const int WarningHitCap = 2;
    /// <summary>При каком уровне opinion первое же нападение игрока запускает Lethal.</summary>
    private const int HostileOpinionThreshold = -25;

    public override void OnInitialize()
    {
        EnsureServices();
    }

    public override void Update(float deltaTime)
    {
        if (!EnsureServices())
            return;

        var map = _mapManager!.CurrentMap;
        if (map == null)
            return;
        var tileMap = _mapManager.CurrentTileMap;

        var player = World.GetEntitiesWith<PlayerTagComponent, TransformComponent>().FirstOrDefault();
        var playerHealth = player?.GetComponent<HealthComponent>();
        var playerAlive = player != null && playerHealth?.IsDead != true;
        var now = _clock!.TotalSecondsAbsolute;

        foreach (var npc in World.GetEntitiesWith<NpcAggressionComponent>())
        {
            if (!npc.HasComponent<NpcTagComponent>() || !npc.HasComponent<TransformComponent>())
                continue;
            if (!NpcLod.IsActive(npc))
                continue;

            var aggression = npc.GetComponent<NpcAggressionComponent>()!;
            if (aggression.Mode == AggressionMode.None)
                continue;

            if (npc.GetComponent<HealthComponent>()?.IsDead == true)
            {
                Aggression.Clear(npc);
                continue;
            }

            if (npc.GetComponent<NpcFleeComponent>() != null)
                continue;

            // Цель — пока только игрок. Если игрок мёртв — закрываем агрессию.
            if (!playerAlive || player == null)
            {
                FinishAggression(npc, aggression);
                continue;
            }

            // После save/load рантайм-id игрока меняется — переразрешаем по SaveId / флагу TargetIsPlayer.
            if (aggression.TargetEntityId != player.Id)
            {
                var playerSaveId = player.GetComponent<SaveEntityIdComponent>()?.SaveId ?? "";
                var saveIdMatches = !string.IsNullOrEmpty(aggression.TargetSaveId)
                    && string.Equals(aggression.TargetSaveId, playerSaveId, StringComparison.OrdinalIgnoreCase);

                if (saveIdMatches || aggression.TargetIsPlayer)
                {
                    aggression.TargetEntityId = player.Id;
                    if (string.IsNullOrEmpty(aggression.TargetSaveId) && !string.IsNullOrEmpty(playerSaveId))
                        aggression.TargetSaveId = playerSaveId;
                }
                else
                {
                    FinishAggression(npc, aggression);
                    continue;
                }
            }

            if (ShouldForgiveHomeIntrusion(aggression, player, map))
            {
                FinishAggression(npc, aggression);
                continue;
            }

            var playerTransform = player.GetComponent<TransformComponent>()!;
            var sees = NpcPerception.CanSee(npc, player, tileMap);

            if (sees)
            {
                aggression.LastSightedAt = now;
                aggression.LastSightedPosition = playerTransform.Position;
                aggression.IsDisengaging = false;
                aggression.DisengageLingerSeconds = 0f;
            }
            else if (now - aggression.LastSightedAt > LoseSightSeconds)
            {
                if (!aggression.IsDisengaging)
                {
                    aggression.IsDisengaging = true;
                    aggression.DisengageLingerSeconds = 0f;
                    StandStill(npc);
                    SpeechBubbleSystem.Show(npc, "Хм...");
                }

                aggression.DisengageLingerSeconds += deltaTime;
                if (aggression.DisengageLingerSeconds >= DisengageLingerSeconds)
                {
                    FinishAggression(npc, aggression);
                    continue;
                }
                continue;
            }

            // Внутри своего дома против нарушителя всегда Lethal.
            if (IsNpcAndTargetInsideNpcHome(npc, player, map))
                aggression.Mode = AggressionMode.Lethal;

            // Преследование — двигаем интент к последней известной позиции цели.
            ChaseTarget(npc, map, aggression.LastSightedPosition);

            if (!sees)
                continue;

            // Решаем, можно ли бить (HP-floor для Restrained).
            if (aggression.Mode == AggressionMode.Restrained && IsBelowHpFloor(player!))
                continue;

            // Удар.
            var attack = _combatSystem!.GetCurrentAttackProfile(npc);
            if (_combatSystem.CanAttack(npc, player!, attack))
            {
                _combatSystem.TryAttack(npc, player!, attack);
                aggression.HasExchanged = true;
                aggression.HitsLandedOnTarget++;
                ApplyOpinionPenalty(npc);

                // Режим предупреждения: после WarningHitCap ответных — успокоиться.
                if (aggression.IsWarning && aggression.HitsLandedOnTarget >= WarningHitCap)
                {
                    SpeechBubbleSystem.Show(npc, "Хватит с тебя!");
                    FinishAggression(npc, aggression);
                    continue;
                }
            }
        }
    }

    private bool EnsureServices()
    {
        _clock ??= ServiceLocator.Has<GameClock>() ? ServiceLocator.Get<GameClock>() : null;
        _mapManager ??= ServiceLocator.Has<MapManager>() ? ServiceLocator.Get<MapManager>() : null;
        _registry ??= ServiceLocator.Has<WorldRegistry>() ? ServiceLocator.Get<WorldRegistry>() : null;
        _combatSystem ??= World.GetSystem<CombatSystem>();
        if (_bus == null && ServiceLocator.Has<EventBus>())
        {
            _bus = ServiceLocator.Get<EventBus>();
            if (!_subscribed)
            {
                _bus.Subscribe<EntityDamagedEvent>(OnEntityDamaged);
                _subscribed = true;
            }
        }

        return _clock != null && _mapManager != null && _registry != null && _combatSystem != null && _bus != null;
    }

    private void OnEntityDamaged(EntityDamagedEvent ev)
    {
        // Интересует только: игрок ударил NPC.
        if (!ev.Attacker.HasComponent<PlayerTagComponent>())
            return;
        if (!ev.Target.HasComponent<NpcTagComponent>())
            return;
        if (!NpcLod.IsActive(ev.Target))
            return;
        if (ev.Target.GetComponent<HealthComponent>()?.IsDead == true)
            return;

        var now = _clock?.TotalSecondsAbsolute ?? 0d;
        var existing = ev.Target.GetComponent<NpcAggressionComponent>();
        var rel = ev.Target.GetComponent<RelationshipsComponent>();
        // Опинион к игроку проверяем ДО штрафа за этот удар, чтобы решить режим.
        var hostileOpinion = rel != null && rel.PlayerOpinion <= HostileOpinionThreshold;
        var armedOrRangedAssault = ev.IsWeaponAttack || ev.IsRangedAttack;

        if (existing == null || existing.Mode == AggressionMode.None)
        {
            // Первое нападение — преследуем нелетально (или летально, если уже дома или плохие отношения).
            var insideHome = _mapManager?.CurrentMap is { } map && IsNpcAndTargetInsideNpcHome(ev.Target, ev.Attacker, map);
            var lethal = insideHome || hostileOpinion || armedOrRangedAssault;
            Aggression.MarkChasing(ev.Target, ev.Attacker, now, lethal);

            // При нелетальной реакции и нормальных отношениях — режим предупреждения (несколько ответных и успокоиться).
            var aggro = ev.Target.GetComponent<NpcAggressionComponent>();
            if (aggro != null && aggro.Mode == AggressionMode.Restrained && !hostileOpinion && !armedOrRangedAssault)
                aggro.IsWarning = true;
        }
        else
        {
            // Повторное (или продолжение) — эскалируем.
            Aggression.Escalate(ev.Target, ev.Attacker, now);
        }

        if (rel != null && ev.Target.GetComponent<NpcAggressionComponent>() is { } aggroAfterHit)
            ApplyPlayerAssaultOpinionPenalty(rel, aggroAfterHit, now);
    }

    private bool IsNpcAndTargetInsideNpcHome(Entity npc, Entity target, MapData map)
    {
        var residence = npc.GetComponent<ResidenceComponent>();
        if (residence == null || string.IsNullOrWhiteSpace(residence.HouseId))
            return false;

        return IsEntityInsideHouse(npc, map, residence.HouseId)
               && IsEntityInsideHouse(target, map, residence.HouseId);
    }

    private bool ShouldForgiveHomeIntrusion(NpcAggressionComponent aggression, Entity player, MapData map)
    {
        if (aggression.Reason != AggressionReason.HomeIntrusion || aggression.ProvokedByTarget)
            return false;

        if (string.IsNullOrWhiteSpace(aggression.ProtectedHouseId))
            return false;

        return !IsEntityInsideHouse(player, map, aggression.ProtectedHouseId);
    }

    private bool IsEntityInsideHouse(Entity entity, MapData map, string houseId)
    {
        if (!_registry!.Houses.TryGetValue(houseId, out var house))
            return false;

        var transform = entity.GetComponent<TransformComponent>();
        if (transform == null)
            return false;

        var tx = (int)MathF.Floor(transform.Position.X / map.TileSize);
        var ty = (int)MathF.Floor(transform.Position.Y / map.TileSize);
        return string.Equals(house.MapId, map.Id, StringComparison.OrdinalIgnoreCase)
               && house.Tiles.Any(t => t.X == tx && t.Y == ty);
    }

    private static bool IsBelowHpFloor(Entity target)
    {
        var health = target.GetComponent<HealthComponent>();
        if (health == null)
            return true;

        var wounds = target.GetComponent<WoundComponent>();
        var floor = wounds is { ExhaustionDamage: >= ExhaustionThreshold } ? HpFloorExhausted : HpFloorNormal;
        return health.Health <= floor;
    }

    private static void ChaseTarget(Entity npc, MapData map, Vector2 targetPos)
    {
        var intent = npc.GetComponent<NpcIntentComponent>() ?? npc.AddComponent(new NpcIntentComponent());
        intent.Action = ScheduleAction.Visit;
        intent.SetTarget(map.Id, targetPos, "", "aggression");
    }

    private static void StandStill(Entity npc)
    {
        var velocity = npc.GetComponent<VelocityComponent>();
        if (velocity != null)
            velocity.Velocity = Vector2.Zero;
        var intent = npc.GetComponent<NpcIntentComponent>();
        intent?.ClearTarget();
    }

    private void FinishAggression(Entity npc, NpcAggressionComponent aggression)
    {
        Aggression.Clear(npc);
        // Сбросим интент — ScheduleSystem на следующем тике поставит обычное расписание.
        npc.GetComponent<NpcIntentComponent>()?.ClearTarget();
        World.GetSystem<ScheduleSystem>()?.RefreshNow();
    }

    private static void ApplyPlayerAssaultOpinionPenalty(
        RelationshipsComponent rel,
        NpcAggressionComponent aggression,
        double now)
    {
        if (aggression.OpinionPenaltyInConflict >= MaxOpinionPenaltyPerConflict)
            return;

        var firstPenalty = aggression.OpinionPenaltyInConflict <= 0;
        if (!firstPenalty && now - aggression.LastOpinionPenaltyAt < OpinionPenaltyCooldownSeconds)
            return;

        var penalty = firstPenalty ? InitialAssaultOpinionPenalty : OngoingAssaultOpinionPenalty;
        penalty = Math.Min(penalty, MaxOpinionPenaltyPerConflict - aggression.OpinionPenaltyInConflict);
        if (penalty <= 0)
            return;

        rel.PlayerOpinion = Math.Clamp(rel.PlayerOpinion - penalty, -100, 100);
        aggression.OpinionPenaltyInConflict += penalty;
        aggression.LastOpinionPenaltyAt = now;
    }

    private static void ApplyOpinionPenalty(Entity npc)
    {
        // Опинион уже снижается в OnEntityDamaged с ограничением на один конфликт.
        // Удары NPC по игроку дополнительно опинион не двигают, чтобы избежать двойного штрафа.
        _ = npc;
    }
}
