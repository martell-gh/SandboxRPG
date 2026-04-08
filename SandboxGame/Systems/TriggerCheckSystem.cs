#nullable enable
using System;
using System.Linq;
using Microsoft.Xna.Framework;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.World;

namespace SandboxGame.Systems;

public class TriggerCheckSystem : GameSystem
{
    private MapManager _mapManager = null!;
    private TriggerSystem _triggerSystem = null!;
    private Action<string, string>? _onLoadMap;
    private Action<TriggerZoneData, Vector2, Action, Action>? _onConfirmTransition;

    // предотвращает повторное срабатывание пока игрок стоит в зоне
    private string? _lastTriggerId;
    private bool _transitionPromptOpen;
    private Vector2 _lastSafePosition;

    public void Initialize(MapManager mapManager, Action<string, string> onLoadMap, Action<TriggerZoneData, Vector2, Action, Action> onConfirmTransition)
    {
        _mapManager = mapManager;
        _triggerSystem = new TriggerSystem(mapManager);
        _onLoadMap = onLoadMap;
        _onConfirmTransition = onConfirmTransition;
        ServiceLocator.Get<EventBus>().Subscribe<MapLoadedEvent>(_ => OnMapLoaded());
    }

    public void OnMapLoaded()
    {
        _lastTriggerId = null;
        _transitionPromptOpen = false;
        if (_mapManager.CurrentMap != null)
            _triggerSystem.SetMap(_mapManager.CurrentMap);
    }

    public override void Update(float deltaTime)
    {
        if (_mapManager.CurrentMap == null) return;

        var player = World.GetEntitiesWith<TransformComponent, PlayerTagComponent>().FirstOrDefault();
        if (player == null) return;

        var transform = player.GetComponent<TransformComponent>()!;
        var collider = player.GetComponent<ColliderComponent>();
        var trigger = collider != null
            ? _triggerSystem.CheckTriggerAtBounds(collider.GetBounds(transform.Position))
            : _triggerSystem.CheckTriggerAt(transform.Position);

        if (trigger == null)
        {
            _lastTriggerId = null;
            _transitionPromptOpen = false;
            _lastSafePosition = transform.Position;
            return;
        }

        // не срабатываем повторно пока игрок в той же зоне
        if (_transitionPromptOpen || trigger.Id == _lastTriggerId)
            return;

        _lastTriggerId = trigger.Id;
        _transitionPromptOpen = true;
        ExecuteTrigger(trigger, _lastSafePosition);
    }

    private void ExecuteTrigger(TriggerZoneData trigger, Vector2 returnPosition)
    {
        switch (trigger.Action.Type)
        {
            case TriggerActionTypes.LocationTransition:
                var targetMap = trigger.Action.TargetMapId;
                var spawnId = trigger.Action.SpawnPointId ?? "default";
                if (string.IsNullOrWhiteSpace(targetMap))
                {
                    DevConsole.Log($"[Trigger] {trigger.Id}: no target map set");
                    return;
                }
                _onConfirmTransition?.Invoke(
                    trigger,
                    returnPosition,
                    () =>
                    {
                        _transitionPromptOpen = false;
                        DevConsole.Log($"[Trigger] {trigger.Id}: transitioning to {targetMap} @ {spawnId}");
                        _onLoadMap?.Invoke(targetMap!, spawnId);
                    },
                    () =>
                    {
                        var player = World.GetEntitiesWith<TransformComponent, PlayerTagComponent>().FirstOrDefault();
                        var transform = player?.GetComponent<TransformComponent>();
                        if (transform != null)
                            transform.Position = returnPosition;
                        _transitionPromptOpen = false;
                        _lastTriggerId = null;
                    });
                break;

            default:
                DevConsole.Log($"[Trigger] {trigger.Id}: unknown action type '{trigger.Action.Type}'");
                break;
        }
    }
}
