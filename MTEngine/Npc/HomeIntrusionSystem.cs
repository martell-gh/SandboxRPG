using Microsoft.Xna.Framework;
using MTEngine.Combat;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Systems;
using MTEngine.World;

namespace MTEngine.Npc;

/// <summary>
/// Защита дома: если игрок без доверенных отношений находится в чужом доме во время сна жильца,
/// жилец не ложится спать (а если уже спит — просыпается), идёт выгонять и при контакте бьёт.
/// Боевая агрессия (преследование, ответный удар) живёт в <see cref="NpcCombatReactionSystem"/>.
/// </summary>
public class HomeIntrusionSystem : GameSystem
{
    private GameClock? _clock;
    private MapManager? _mapManager;
    private WorldRegistry? _registry;
    private SleepSystem? _sleepSystem;
    private CombatSystem? _combatSystem;
    private readonly Dictionary<int, float> _warningCooldowns = new();
    private readonly Dictionary<int, float> _firstSightedAt = new();

    private const float EscortGraceSeconds = 12f;

    public override void Update(float deltaTime)
    {
        if (!EnsureServices())
            return;

        TickWarningCooldowns(deltaTime);

        var map = _mapManager!.CurrentMap;
        if (map == null)
            return;

        var player = World.GetEntitiesWith<PlayerTagComponent, TransformComponent>().FirstOrDefault();
        if (player == null || player.GetComponent<HealthComponent>()?.IsDead == true)
        {
            _firstSightedAt.Clear();
            return;
        }

        var playerTransform = player.GetComponent<TransformComponent>()!;
        var playerTile = new Point(
            (int)MathF.Floor(playerTransform.Position.X / map.TileSize),
            (int)MathF.Floor(playerTransform.Position.Y / map.TileSize));

        var house = _registry!.FindHouseByMapAndTile(map.Id, playerTile.X, playerTile.Y);
        if (house == null)
        {
            _firstSightedAt.Clear();
            return;
        }
        var tileMap = _mapManager.CurrentTileMap;

        foreach (var owner in World.GetEntitiesWith<NpcTagComponent, ResidenceComponent>())
        {
            if (!NpcLod.IsActive(owner))
                continue;

            if (owner.GetComponent<HealthComponent>()?.IsDead == true)
                continue;

            var residence = owner.GetComponent<ResidenceComponent>()!;
            if (!string.Equals(residence.HouseId, house.Id, StringComparison.OrdinalIgnoreCase))
                continue;

            if (IsTrustedByPlayer(owner))
                continue;

            var sleepingNow = _sleepSystem != null && _sleepSystem.IsSleeping(owner);
            var scheduledSleep = IsScheduledSleep(owner);
            if (!sleepingNow && !scheduledSleep)
            {
                _firstSightedAt.Remove(owner.Id);
                continue;
            }

            var ownerSeesPlayer = NpcPerception.CanSee(owner, player, tileMap);
            if (!sleepingNow && !ownerSeesPlayer)
                continue;

            // Уже спит — будим и сразу делаем летальным (без grace-окна).
            if (sleepingNow)
            {
                _sleepSystem!.WakeActor(owner, "Кто-то в моём доме!");
                Aggression.MarkHomeIntrusion(owner, player, _clock!.TotalSecondsAbsolute, house.Id, lethal: true);
                Warn(owner);
                OverrideIntent(owner, playerTransform.Position, map.Id, house.Id);
                continue;
            }

            // Час сна, но ещё не спит — старая мягкая логика: предупреждение + эскорт,
            // через ~12 сек переходим в нелетальное преследование.
            OverrideIntent(owner, playerTransform.Position, map.Id, house.Id);
            Warn(owner);

            if (!_firstSightedAt.ContainsKey(owner.Id))
                _firstSightedAt[owner.Id] = 0f;
            _firstSightedAt[owner.Id] += deltaTime;

            if (_firstSightedAt[owner.Id] >= EscortGraceSeconds)
            {
                Aggression.MarkHomeIntrusion(owner, player, _clock!.TotalSecondsAbsolute, house.Id, lethal: false);
                TryAttackIntruder(owner, player);
            }
        }
    }

    private bool EnsureServices()
    {
        _clock ??= ServiceLocator.Has<GameClock>() ? ServiceLocator.Get<GameClock>() : null;
        _mapManager ??= ServiceLocator.Has<MapManager>() ? ServiceLocator.Get<MapManager>() : null;
        _registry ??= ServiceLocator.Has<WorldRegistry>() ? ServiceLocator.Get<WorldRegistry>() : null;
        _sleepSystem ??= World.GetSystem<SleepSystem>();
        _combatSystem ??= World.GetSystem<CombatSystem>();
        return _clock != null && _mapManager != null && _registry != null;
    }

    private bool IsScheduledSleep(Entity owner)
    {
        var schedule = owner.GetComponent<ScheduleComponent>();
        if (schedule?.FindSlot(_clock!.HourInt)?.Action == ScheduleAction.Sleep)
            return true;
        return owner.GetComponent<NpcIntentComponent>()?.Action == ScheduleAction.Sleep;
    }

    private static bool IsTrustedByPlayer(Entity owner)
    {
        var relationships = owner.GetComponent<RelationshipsComponent>();
        return relationships is
        {
            PartnerIsPlayer: true,
            Status: RelationshipStatus.Dating or RelationshipStatus.Engaged or RelationshipStatus.Married
        };
    }

    private static void OverrideIntent(Entity owner, Vector2 playerPosition, string mapId, string houseId)
    {
        var intent = owner.GetComponent<NpcIntentComponent>() ?? owner.AddComponent(new NpcIntentComponent());
        intent.Action = ScheduleAction.Visit;
        intent.SetTarget(mapId, playerPosition, houseId, "intruder");
    }

    private void Warn(Entity owner)
    {
        if (_warningCooldowns.TryGetValue(owner.Id, out var cooldown) && cooldown > 0f)
            return;

        PopupTextSystem.Show(owner, "Вон из моего дома!", Color.IndianRed, lifetime: 1.2f);
        _warningCooldowns[owner.Id] = 3f;
    }

    private void TryAttackIntruder(Entity owner, Entity player)
    {
        if (_combatSystem == null)
            return;

        var attack = _combatSystem.GetCurrentAttackProfile(owner);
        if (_combatSystem.CanAttack(owner, player, attack))
            _combatSystem.TryAttack(owner, player, attack);
    }

    private void TickWarningCooldowns(float deltaTime)
    {
        if (_warningCooldowns.Count == 0)
            return;

        foreach (var key in _warningCooldowns.Keys.ToArray())
        {
            _warningCooldowns[key] -= deltaTime;
            if (_warningCooldowns[key] <= 0f)
                _warningCooldowns.Remove(key);
        }
    }
}
