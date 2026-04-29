using Microsoft.Xna.Framework;
using MTEngine.Combat;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Systems;
using MTEngine.World;

namespace MTEngine.Npc;

/// <summary>
/// Active-zone pursuit and attack layer for Avenger NPCs.
/// </summary>
public class AvengerSystem : GameSystem
{
    private MapManager? _mapManager;
    private CombatSystem? _combatSystem;
    private SleepSystem? _sleepSystem;

    public override void Update(float deltaTime)
    {
        _mapManager ??= ServiceLocator.Has<MapManager>() ? ServiceLocator.Get<MapManager>() : null;
        _combatSystem ??= World.GetSystem<CombatSystem>();
        _sleepSystem ??= ServiceLocator.Has<SleepSystem>() ? ServiceLocator.Get<SleepSystem>() : null;

        var map = _mapManager?.CurrentMap;
        if (map == null || _combatSystem == null)
            return;

        var player = World.GetEntitiesWith<PlayerTagComponent, TransformComponent>().FirstOrDefault();
        if (player == null)
            return;

        var playerTransform = player.GetComponent<TransformComponent>()!;
        foreach (var npc in World.GetEntitiesWith<NpcTagComponent, AvengerComponent>())
        {
            if (!NpcLod.IsActive(npc))
                continue;
            if (npc.GetComponent<HealthComponent>()?.IsDead == true)
                continue;
            if (npc.GetComponent<NpcFleeComponent>() != null)
                continue;

            var avenger = npc.GetComponent<AvengerComponent>()!;
            if (!avenger.TargetIsPlayer)
                continue;

            var intent = npc.GetComponent<NpcIntentComponent>() ?? npc.AddComponent(new NpcIntentComponent());

            avenger.LastKnownMapId = map.Id;
            avenger.LastKnownX = playerTransform.Position.X;
            avenger.LastKnownY = playerTransform.Position.Y;

            intent.Action = ScheduleAction.Visit;
            intent.SetTarget(map.Id, playerTransform.Position);

            var attack = _combatSystem.GetCurrentAttackProfile(npc);
            if (!_combatSystem.CanAttack(npc, player, attack))
                continue;

            if (_sleepSystem?.IsSleeping(player) == true)
            {
                StandStill(npc, intent);
                continue;
            }

            if (!avenger.AccusationSaid)
            {
                avenger.AccusationSaid = true;
                SpeechBubbleSystem.Show(npc, "Ты убил моего близкого!");
                MarkWorldDirty();
            }

            _combatSystem.TryAttack(npc, player, attack);
        }
    }

    private static void StandStill(Entity npc, NpcIntentComponent intent)
    {
        var velocity = npc.GetComponent<VelocityComponent>();
        if (velocity != null)
            velocity.Velocity = Vector2.Zero;

        npc.GetComponent<SpriteComponent>()?.PlayDirectionalIdle(Vector2.Zero);
        intent.MarkArrived();
    }

    private static void MarkWorldDirty()
    {
        if (ServiceLocator.Has<IWorldStateTracker>())
            ServiceLocator.Get<IWorldStateTracker>().MarkDirty();
    }
}
