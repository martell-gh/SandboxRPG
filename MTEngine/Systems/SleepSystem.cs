using Microsoft.Xna.Framework;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Metabolism;
using MTEngine.Npc;
using MTEngine.World;
using MTEngine.Wounds;

namespace MTEngine.Systems;

[SaveObject("sleepSystem")]
public class SleepSystem : GameSystem
{
    [SaveField("isSleeping")]
    public bool IsSleepAccelerationActive { get; private set; }

    [SaveField("sleepRemainingSeconds")]
    public float RemainingSleepSeconds { get; private set; }

    [SaveField("sleepWakeHour")]
    public float WakeHour { get; private set; } = 6f;

    [SaveField("sleepWakeAbsoluteSeconds")]
    public double WakeAbsoluteSeconds { get; private set; }

    [SaveField("sleepTimeScale")]
    public float ActiveSleepTimeScale { get; private set; }

    [SaveField("sleepHealingPerSecond")]
    public float ActiveHealingPerSecond { get; private set; }

    private GameClock _clock = null!;
    private Entity? _sleepingActor;
    private BedSlot? _reservedBedSlot;
    private float _previousTimeScale;

    private const float SleepPointOccupancyRadius = 18f;

    public override void OnInitialize()
    {
        _clock = ServiceLocator.Get<GameClock>();
    }

    public override void Update(float deltaTime)
    {
        if (!IsSleepAccelerationActive)
            return;

        if (_sleepingActor == null || _sleepingActor.GetComponent<HealthComponent>()?.IsDead == true)
        {
            StopSleep(setWakeTime: false);
            return;
        }

        _clock.TimeScale = ActiveSleepTimeScale;

        if (WakeAbsoluteSeconds > 0d)
            RemainingSleepSeconds = (float)Math.Max(0d, WakeAbsoluteSeconds - _clock.TotalSecondsAbsolute);
        else
            RemainingSleepSeconds -= deltaTime * ActiveSleepTimeScale;

        ApplySleepHealing(_sleepingActor, deltaTime);

        if (RemainingSleepSeconds <= 0f)
            StopSleep(setWakeTime: true);
    }

    public bool TryStartSleep(Entity actor, Entity bed, BedComponent bedComponent)
    {
        if (!CanActorSleepInBed(actor, bed, bedComponent, out var reason))
        {
            if (!string.IsNullOrWhiteSpace(reason))
                PopupTextSystem.Show(actor, reason, Color.LightGoldenrodYellow, lifetime: 1.5f);
            return false;
        }

        var slot = FindAvailableSleepSlot(actor, bed, bedComponent);
        if (slot == null)
        {
            PopupTextSystem.Show(actor, "Кровать занята.", Color.LightGoldenrodYellow, lifetime: 1.5f);
            return false;
        }

        _sleepingActor = actor;
        _reservedBedSlot = bedComponent.SleepSlots.Contains(slot) ? slot : null;
        if (_reservedBedSlot != null)
            _reservedBedSlot.OccupantSaveId = GetActorSleepId(actor);

        WakeHour = Math.Clamp(bedComponent.WakeHour, 0f, 23.99f);
        ActiveSleepTimeScale = Math.Max(_clock.TimeScale, bedComponent.SleepTimeScale);
        ActiveHealingPerSecond = Math.Max(0f, bedComponent.HealingPerSecond);
        _previousTimeScale = _clock.TimeScale;
        WakeAbsoluteSeconds = CalculateWakeAbsoluteSeconds(_clock.TotalSecondsAbsolute, WakeHour);
        RemainingSleepSeconds = (float)Math.Max(0d, WakeAbsoluteSeconds - _clock.TotalSecondsAbsolute);
        IsSleepAccelerationActive = RemainingSleepSeconds > 0.01f;

        if (!IsSleepAccelerationActive)
        {
            ReleaseReservedBedSlot();
            return false;
        }

        MoveActorToSleepPoint(actor, bed, bedComponent, slot);
        MarkWorldDirty();
        PopupTextSystem.Show(actor, $"Сон до {WakeHour:00}:00...", Color.LightSkyBlue, lifetime: 1.5f);
        return true;
    }

    public bool CanActorSleepInBed(Entity actor, Entity bed, BedComponent bedComponent, out string reason)
    {
        reason = "";

        if (IsSleepAccelerationActive)
            return false;

        if (_clock.IsDay)
            return false;

        var health = actor.GetComponent<HealthComponent>();
        if (health?.IsDead == true)
            return false;

        if (TryFindForeignBedOwner(actor, bed, bedComponent, out var owner)
            && !IsTrustedVisitor(actor, owner))
        {
            reason = "Чужая кровать.";
            return false;
        }

        var rental = World.GetSystem<InnRentalSystem>();
        if (rental != null && !rental.CanActorUseBed(actor, bed, bedComponent, out reason))
            return false;

        if (FindAvailableSleepSlot(actor, bed, bedComponent) == null)
        {
            reason = "Кровать занята.";
            return false;
        }

        return true;
    }

    public bool IsSleeping(Entity entity)
        => IsSleepAccelerationActive && _sleepingActor == entity;

    public bool WakeActor(Entity actor, string message = "")
    {
        if (!IsSleeping(actor))
            return false;

        StopSleep(setWakeTime: false);
        if (!string.IsNullOrWhiteSpace(message))
            PopupTextSystem.Show(actor, message, Color.LightGoldenrodYellow, lifetime: 1.5f);

        return true;
    }

    public bool ShouldSuspendNeedDecay()
        => IsSleepAccelerationActive;

    private void StopSleep(bool setWakeTime)
    {
        if (setWakeTime)
        {
            if (WakeAbsoluteSeconds > 0d)
                _clock.SetAbsoluteSeconds(WakeAbsoluteSeconds);
            else
                _clock.AdvanceToHour(WakeHour);
        }

        _clock.TimeScale = _previousTimeScale > 0f ? _previousTimeScale : 72f;
        ReleaseReservedBedSlot();

        if (setWakeTime)
            World.GetSystem<ScheduleSystem>()?.RefreshNow();

        if (_sleepingActor != null && setWakeTime)
            PopupTextSystem.Show(_sleepingActor, "Проснулся.", Color.LightGoldenrodYellow, lifetime: 1.5f);

        _sleepingActor = null;
        IsSleepAccelerationActive = false;
        RemainingSleepSeconds = 0f;
        WakeHour = 6f;
        WakeAbsoluteSeconds = 0d;
        ActiveSleepTimeScale = 0f;
        ActiveHealingPerSecond = 0f;
        _previousTimeScale = 0f;
    }

    private void ApplySleepHealing(Entity actor, float deltaTime)
    {
        if (ActiveHealingPerSecond <= 0f)
            return;

        var health = actor.GetComponent<HealthComponent>();
        if (health == null || health.IsDead)
            return;

        var healAmount = ActiveHealingPerSecond * deltaTime;
        if (healAmount <= 0f)
            return;

        var wounds = actor.GetComponent<WoundComponent>();
        if (wounds != null)
        {
            healAmount -= WoundComponent.HealDamage(actor, DamageType.Exhaustion, healAmount);
            if (healAmount > 0f)
                healAmount -= WoundComponent.HealDamage(actor, DamageType.Blunt, healAmount);
            if (healAmount > 0f)
                healAmount -= WoundComponent.HealDamage(actor, DamageType.Slash, healAmount);
            if (healAmount > 0f)
                WoundComponent.HealDamage(actor, DamageType.Burn, healAmount);

            health.Health = Math.Max(0f, health.MaxHealth - wounds.TotalDamage);
            return;
        }

        health.Health = Math.Min(health.MaxHealth, health.Health + healAmount);
    }

    private BedSlot? FindAvailableSleepSlot(Entity actor, Entity bed, BedComponent bedComponent)
    {
        var actorSaveId = GetActorSleepId(actor);
        foreach (var slot in bedComponent.GetEffectiveSlots())
        {
            if (!IsSlotAllowedForActor(slot, actorSaveId))
                continue;

            if (!string.IsNullOrWhiteSpace(slot.OccupantSaveId)
                && !string.Equals(slot.OccupantSaveId, actorSaveId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (IsSleepPointReservedByNpc(actor, bed, slot))
                continue;

            return slot;
        }

        return null;
    }

    private bool IsSleepPointReservedByNpc(Entity actor, Entity bed, BedSlot slot)
    {
        var bedTransform = bed.GetComponent<TransformComponent>();
        if (bedTransform == null)
            return false;

        var sleepPoint = bedTransform.Position + new Vector2(slot.LieOffsetX, slot.LieOffsetY);

        foreach (var npc in World.GetEntitiesWith<NpcTagComponent, TransformComponent>())
        {
            if (npc == actor)
                continue;

            if (npc.GetComponent<HealthComponent>()?.IsDead == true)
                continue;

            var intent = npc.GetComponent<NpcIntentComponent>();
            if (intent is { Action: ScheduleAction.Sleep, HasTarget: true }
                && Vector2.Distance(intent.TargetPosition, sleepPoint) <= SleepPointOccupancyRadius)
            {
                return true;
            }

            var schedule = npc.GetComponent<ScheduleComponent>();
            if (schedule?.FindSlot(_clock.HourInt)?.Action == ScheduleAction.Sleep)
            {
                var transform = npc.GetComponent<TransformComponent>()!;
                if (Vector2.Distance(transform.Position, sleepPoint) <= SleepPointOccupancyRadius)
                    return true;
            }
        }

        return false;
    }

    private bool TryFindForeignBedOwner(Entity actor, Entity bed, BedComponent bedComponent, out Entity owner)
    {
        owner = null!;
        if (!TryResolveHouseBedSlot(bed, bedComponent, out var house, out var pointId)
            || string.IsNullOrWhiteSpace(pointId))
        {
            return false;
        }

        var actorSaveId = GetActorSleepId(actor);
        foreach (var npc in World.GetEntitiesWith<NpcTagComponent, ResidenceComponent>())
        {
            if (npc == actor)
                continue;

            if (ServiceLocator.Has<WorldRegistry>())
                NpcBedAssignment.EnsureAssigned(npc, ServiceLocator.Get<WorldRegistry>(), World);

            var residence = npc.GetComponent<ResidenceComponent>()!;
            if (!string.Equals(residence.HouseId, house.Id, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(residence.BedSlotId, pointId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var ownerSaveId = npc.GetComponent<SaveEntityIdComponent>()?.SaveId ?? "";
            if (!string.IsNullOrWhiteSpace(ownerSaveId)
                && string.Equals(ownerSaveId, actorSaveId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            owner = npc;
            return true;
        }

        return false;
    }

    private static bool TryResolveHouseBedSlot(Entity bed, BedComponent bedComponent, out HouseDef house, out string pointId)
    {
        house = null!;
        pointId = "";

        if (!ServiceLocator.Has<MapManager>() || !ServiceLocator.Has<WorldRegistry>())
            return false;

        var mapManager = ServiceLocator.Get<MapManager>();
        var map = mapManager.CurrentMap;
        if (map == null)
            return false;

        var bedTransform = bed.GetComponent<TransformComponent>();
        if (bedTransform == null)
            return false;

        var tileSize = map.TileSize;
        var bedTileX = (int)MathF.Floor(bedTransform.Position.X / tileSize);
        var bedTileY = (int)MathF.Floor(bedTransform.Position.Y / tileSize);
        var registry = ServiceLocator.Get<WorldRegistry>();
        var foundHouse = registry.FindHouseByMapAndTile(map.Id, bedTileX, bedTileY);
        if (foundHouse == null)
            return false;

        house = foundHouse;
        var sleepPoint = bedTransform.Position + new Vector2(bedComponent.LieOffsetX, bedComponent.LieOffsetY);
        var maxDistanceSq = tileSize * tileSize * 2.25f;
        var slot = foundHouse.BedSlots
            .Concat(foundHouse.ChildBedSlots)
            .Select(point => new
            {
                Point = point,
                DistanceSq = Vector2.DistanceSquared(
                    sleepPoint,
                    new Vector2((point.X + 0.5f) * tileSize, (point.Y + 0.5f) * tileSize))
            })
            .Where(entry => entry.DistanceSq <= maxDistanceSq)
            .OrderBy(entry => entry.DistanceSq)
            .ThenBy(entry => entry.Point.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (slot != null)
            pointId = slot.Point.Id;

        return true;
    }

    private static bool IsTrustedVisitor(Entity actor, Entity owner)
    {
        if (!actor.HasComponent<PlayerTagComponent>())
            return false;

        var relationships = owner.GetComponent<RelationshipsComponent>();
        return relationships is
        {
            PartnerIsPlayer: true,
            Status: RelationshipStatus.Dating or RelationshipStatus.Engaged or RelationshipStatus.Married
        };
    }

    private static bool IsSlotAllowedForActor(BedSlot slot, string actorSaveId)
        => string.IsNullOrWhiteSpace(slot.OwnerSaveId)
           || string.Equals(slot.OwnerSaveId, actorSaveId, StringComparison.OrdinalIgnoreCase);

    private static void MoveActorToSleepPoint(Entity actor, Entity bed, BedComponent bedComponent, BedSlot? slot)
    {
        var actorTransform = actor.GetComponent<TransformComponent>();
        var bedTransform = bed.GetComponent<TransformComponent>();
        if (actorTransform == null || bedTransform == null)
            return;

        var sleepOffset = slot != null
            ? new Vector2(slot.LieOffsetX, slot.LieOffsetY)
            : new Vector2(bedComponent.LieOffsetX, bedComponent.LieOffsetY);

        actorTransform.Position = bedTransform.Position + sleepOffset;

        var velocity = actor.GetComponent<VelocityComponent>();
        if (velocity != null)
            velocity.Velocity = Vector2.Zero;
    }

    private static double CalculateWakeAbsoluteSeconds(double currentAbsoluteSeconds, float wakeHour)
    {
        var wakeSecondsOfDay = Math.Clamp(wakeHour, 0f, 23.99f) * 3600d;
        var dayIndex = Math.Floor(currentAbsoluteSeconds / GameClock.SecondsPerDay);
        var wakeAbsolute = dayIndex * GameClock.SecondsPerDay + wakeSecondsOfDay;
        if (wakeAbsolute <= currentAbsoluteSeconds)
            wakeAbsolute += GameClock.SecondsPerDay;

        return wakeAbsolute;
    }

    private void ReleaseReservedBedSlot()
    {
        if (_reservedBedSlot != null)
        {
            _reservedBedSlot.OccupantSaveId = "";
            _reservedBedSlot = null;
            MarkWorldDirty();
        }
    }

    private static string GetActorSleepId(Entity actor)
    {
        var marker = actor.GetComponent<SaveEntityIdComponent>();
        return !string.IsNullOrWhiteSpace(marker?.SaveId)
            ? marker!.SaveId
            : $"entity:{actor.Id}";
    }

    private static void MarkWorldDirty()
    {
        if (ServiceLocator.Has<IWorldStateTracker>())
            ServiceLocator.Get<IWorldStateTracker>().MarkDirty();
    }
}
