using Microsoft.Xna.Framework;
using MTEngine.Components;
using MTEngine.Combat;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Items;
using MTEngine.World;

namespace MTEngine.Npc;

/// <summary>
/// MTLiving revenge trigger layer. It schedules revenge after a player-caused family death
/// and marks due triggers as ready. Actual avenger pursuit is P7.3.
/// </summary>
public class RevengeSystem : GameSystem
{
    private const int MinDelayDays = 5;
    private const int MaxDelayDays = 20;

    private readonly Random _rng = new();
    private EventBus _bus = null!;
    private Calendar? _calendar;
    private WorldRegistry? _registry;
    private PrototypeManager? _prototypes;
    private EntityFactory? _entityFactory;
    private GameClock? _clock;
    private MapManager? _mapManager;

    public override void OnInitialize()
    {
        _bus = ServiceLocator.Get<EventBus>();
        _bus.Subscribe<EntityDied>(OnEntityDied);
        _bus.Subscribe<DayChanged>(OnDayChanged);
    }

    public override void Update(float deltaTime)
    {
        _clock ??= ServiceLocator.Has<GameClock>() ? ServiceLocator.Get<GameClock>() : null;
        _mapManager ??= ServiceLocator.Has<MapManager>() ? ServiceLocator.Get<MapManager>() : null;

        var map = _mapManager?.CurrentMap;
        var tileMap = _mapManager?.CurrentTileMap;
        if (_clock == null || map == null || tileMap == null)
            return;

        var player = World.GetEntitiesWith<PlayerTagComponent, TransformComponent>().FirstOrDefault();
        if (player == null || player.GetComponent<HealthComponent>()?.IsDead == true)
            return;

        foreach (var npc in World.GetEntitiesWith<NpcTagComponent, RevengeTriggerComponent>())
        {
            if (!NpcLod.IsActive(npc))
                continue;
            if (npc.GetComponent<HealthComponent>()?.IsDead == true)
                continue;
            if (!HasReadyHostilityAgainstPlayer(npc.GetComponent<RevengeTriggerComponent>()!, player))
                continue;

            var aggression = npc.GetComponent<NpcAggressionComponent>();
            if (aggression is { Mode: not AggressionMode.None, TargetEntityId: var targetId } && targetId == player.Id)
                continue;

            if (!NpcPerception.CanSee(npc, player, tileMap))
                continue;

            Aggression.MarkChasing(npc, player, _clock.TotalSecondsAbsolute, lethal: true);
        }
    }

    public override void OnDestroy()
    {
        _bus.Unsubscribe<EntityDied>(OnEntityDied);
        _bus.Unsubscribe<DayChanged>(OnDayChanged);
    }

    private void OnEntityDied(EntityDied ev)
    {
        if (ev.Killer == null || !IsPlayerOrPlayerAllied(ev.Killer))
            return;

        var victimSaveId = GetSaveId(ev.Victim);
        var killerSaveId = GetSaveId(ev.Killer);
        if (string.IsNullOrWhiteSpace(victimSaveId))
            return;

        var victimKin = ev.Victim.GetComponent<KinComponent>();
        if (victimKin == null || victimKin.Links.Count == 0)
            return;

        var changed = false;
        foreach (var link in victimKin.Links.ToList())
        {
            var grieving = FindEntityBySaveId(link.NpcSaveId);
            if (grieving == null || grieving == ev.Victim || grieving == ev.Killer)
                continue;

            var personality = grieving.GetComponent<PersonalityComponent>();
            if (personality == null || personality.Pacifist || personality.Vengefulness <= 0)
                continue;

            var behavior = ResolveBehavior(personality.Vengefulness);
            if (behavior == RevengeBehavior.None)
                continue;

            var triggers = grieving.GetComponent<RevengeTriggerComponent>()
                           ?? grieving.AddComponent(new RevengeTriggerComponent());
            if (triggers.HasTriggerFor(victimSaveId, killerSaveId))
                continue;

            triggers.Triggers.Add(new RevengeTrigger
            {
                VictimSaveId = victimSaveId,
                KillerSaveId = killerSaveId,
                CauseKin = link.Kind,
                CreatedDayIndex = ev.DayIndex,
                TriggerAfterDayIndex = ResolveTriggerDay(grieving, ev.DayIndex),
                Behavior = behavior
            });
            changed = true;
        }

        MarkDirtyIfNeeded(changed);
    }

    private void OnDayChanged(DayChanged ev)
    {
        var changed = false;
        foreach (var entity in World.GetEntitiesWith<RevengeTriggerComponent>())
        {
            var triggers = entity.GetComponent<RevengeTriggerComponent>()!;
            foreach (var trigger in triggers.Triggers)
            {
                if (trigger.Ready || ev.NewDayIndex < trigger.TriggerAfterDayIndex)
                    continue;

                if (IsUnderage(entity))
                    continue;

                if (trigger.Behavior == RevengeBehavior.Avenger)
                    changed |= StartAvenger(entity, trigger, ev.NewDayIndex);
                else
                    changed |= MarkTriggerReady(trigger, ev.NewDayIndex);
            }
        }

        MarkDirtyIfNeeded(changed);
    }

    private bool StartAvenger(Entity entity, RevengeTrigger trigger, long today)
    {
        var changed = MarkTriggerReady(trigger, today);

        var avenger = entity.GetComponent<AvengerComponent>();
        if (avenger == null)
        {
            avenger = entity.AddComponent(new AvengerComponent());
            changed = true;
        }

        if (!string.Equals(avenger.TargetSaveId, trigger.KillerSaveId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(avenger.VictimSaveId, trigger.VictimSaveId, StringComparison.OrdinalIgnoreCase)
            || avenger.StartedDayIndex != today)
        {
            avenger.TargetSaveId = trigger.KillerSaveId;
            avenger.TargetIsPlayer = true;
            avenger.VictimSaveId = trigger.VictimSaveId;
            avenger.StartedDayIndex = today;
            changed = true;
        }

        changed |= RemoveProfession(entity);
        changed |= ApplyAvengerSchedule(entity);
        changed |= ApplySpeedBoost(entity, avenger);
        changed |= ApplyCombatPreparation(entity, avenger);
        return changed;
    }

    private static bool MarkTriggerReady(RevengeTrigger trigger, long today)
    {
        if (trigger.Ready && trigger.ReadyDayIndex == today)
            return false;

        trigger.Ready = true;
        trigger.ReadyDayIndex = today;
        return true;
    }

    private bool RemoveProfession(Entity entity)
    {
        var profession = entity.GetComponent<ProfessionComponent>();
        if (profession == null)
            return false;

        _registry ??= ServiceLocator.Has<WorldRegistry>() ? ServiceLocator.Get<WorldRegistry>() : null;
        var saveId = GetSaveId(entity);
        if (_registry != null
            && !string.IsNullOrWhiteSpace(profession.SlotId)
            && _registry.Professions.TryGetValue(profession.SlotId, out var slot)
            && string.Equals(slot.OccupiedNpcSaveId, saveId, StringComparison.OrdinalIgnoreCase))
        {
            slot.OccupiedNpcSaveId = null;
        }

        entity.RemoveComponent<ProfessionComponent>();
        return true;
    }

    private static bool ApplyAvengerSchedule(Entity entity)
    {
        var schedule = entity.GetComponent<ScheduleComponent>() ?? entity.AddComponent(new ScheduleComponent());
        var changed = schedule.TemplateId != "avenger" || schedule.Slots.Count != 1 || schedule.Freetime.Count != 0;

        schedule.TemplateId = "avenger";
        schedule.Slots.Clear();
        schedule.Slots.Add(new ScheduleSlot
        {
            StartHour = 0,
            EndHour = 24,
            Action = ScheduleAction.Visit,
            TargetAreaId = "$player",
            Priority = 100
        });
        schedule.Freetime.Clear();
        return changed;
    }

    private static bool ApplySpeedBoost(Entity entity, AvengerComponent avenger)
    {
        if (avenger.SpeedBoostApplied)
            return false;

        var velocity = entity.GetComponent<VelocityComponent>();
        if (velocity == null)
            return false;

        velocity.Speed *= 1.12f;
        avenger.SpeedBoostApplied = true;
        return true;
    }

    private bool ApplyCombatPreparation(Entity entity, AvengerComponent avenger)
    {
        if (string.IsNullOrWhiteSpace(avenger.CombatSkillId))
            avenger.CombatSkillId = PickCombatSkillId();

        BoostNpcSkill(entity, avenger.CombatSkillId);
        BoostCombatSkill(entity, avenger.CombatSkillId);

        if (!avenger.WeaponIssued)
        {
            avenger.WeaponIssued = TryIssueWeapon(entity, avenger.CombatSkillId);
            return true;
        }

        return false;
    }

    private string PickCombatSkillId()
    {
        var roll = _rng.Next(3);
        return roll switch
        {
            0 => "melee_unarmed",
            1 => "melee_one_handed",
            _ => "melee_two_handed"
        };
    }

    private static void BoostNpcSkill(Entity entity, string skillId)
    {
        var skills = entity.GetComponent<SkillsComponent>() ?? entity.AddComponent(new SkillsComponent());
        skills.Add(skillId, 5f);
    }

    private static void BoostCombatSkill(Entity entity, string skillId)
    {
        var skills = entity.GetComponent<SkillComponent>() ?? entity.AddComponent(new SkillComponent());
        switch (skillId)
        {
            case "melee_unarmed":
                skills.HandToHand = Math.Clamp(skills.HandToHand + 15f, 0f, 100f);
                break;
            case "melee_one_handed":
                skills.OneHandedWeapons = Math.Clamp(skills.OneHandedWeapons + 15f, 0f, 100f);
                break;
            case "melee_two_handed":
                skills.TwoHandedWeapons = Math.Clamp(skills.TwoHandedWeapons + 15f, 0f, 100f);
                break;
        }
    }

    private bool TryIssueWeapon(Entity entity, string skillId)
    {
        if (skillId == "melee_unarmed")
            return true;

        _prototypes ??= ServiceLocator.Has<PrototypeManager>() ? ServiceLocator.Get<PrototypeManager>() : null;
        _entityFactory ??= ServiceLocator.Has<EntityFactory>() ? ServiceLocator.Get<EntityFactory>() : null;
        if (_prototypes == null || _entityFactory == null)
            return false;

        var prototypeId = skillId == "melee_two_handed" ? "greatsword" : "longsword";
        var proto = _prototypes.GetEntity(prototypeId);
        if (proto == null)
            return false;

        var position = entity.GetComponent<TransformComponent>()?.Position ?? Vector2.Zero;
        var weapon = _entityFactory.CreateFromPrototype(proto, position);
        if (weapon == null)
            return false;

        var hands = entity.GetComponent<HandsComponent>() ?? entity.AddComponent(new HandsComponent());
        return hands.TryPickUp(weapon);
    }

    private long ResolveTriggerDay(Entity grieving, long today)
    {
        var triggerDay = today + _rng.Next(MinDelayDays, MaxDelayDays + 1);
        var age = grieving.GetComponent<AgeComponent>();
        if (age == null || age.Years >= 18)
            return triggerDay;

        _calendar ??= ServiceLocator.Has<Calendar>() ? ServiceLocator.Get<Calendar>() : null;
        var daysPerYear = Math.Max(1, _calendar?.DaysPerYear ?? 365);
        var adulthoodDay = today + (long)Math.Ceiling((double)((18 - age.Years) * daysPerYear));
        return Math.Max(triggerDay, adulthoodDay);
    }

    private static RevengeBehavior ResolveBehavior(int vengefulness)
        => vengefulness switch
        {
            <= 0 => RevengeBehavior.None,
            <= 2 => RevengeBehavior.MerchantPenalty,
            <= 5 => RevengeBehavior.HostileOnSight,
            <= 7 => RevengeBehavior.OpportunisticHunter,
            _ => RevengeBehavior.Avenger
        };

    private static bool IsPlayerOrPlayerAllied(Entity entity)
        => entity.HasComponent<PlayerTagComponent>();

    private static bool HasReadyHostilityAgainstPlayer(RevengeTriggerComponent triggers, Entity player)
        => triggers.Triggers.Any(trigger =>
            trigger.Ready
            && IsPlayerKillerTrigger(trigger, player)
            && trigger.Behavior is RevengeBehavior.HostileOnSight or RevengeBehavior.OpportunisticHunter);

    private static bool IsPlayerKillerTrigger(RevengeTrigger trigger, Entity player)
    {
        if (!player.HasComponent<PlayerTagComponent>())
            return false;

        var playerSaveId = player.GetComponent<SaveEntityIdComponent>()?.SaveId ?? "";
        return string.IsNullOrWhiteSpace(trigger.KillerSaveId)
               || string.Equals(trigger.KillerSaveId, playerSaveId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnderage(Entity entity)
        => entity.GetComponent<AgeComponent>() is { Years: < 18 };

    private Entity? FindEntityBySaveId(string saveId)
    {
        if (string.IsNullOrWhiteSpace(saveId))
            return null;

        foreach (var entity in World.GetEntitiesWith<SaveEntityIdComponent>())
        {
            if (string.Equals(GetSaveId(entity), saveId, StringComparison.OrdinalIgnoreCase))
                return entity;
        }

        return null;
    }

    private static string GetSaveId(Entity entity)
        => entity.GetComponent<SaveEntityIdComponent>()?.SaveId ?? "";

    private static void MarkDirtyIfNeeded(bool changed)
    {
        if (changed && ServiceLocator.Has<IWorldStateTracker>())
            ServiceLocator.Get<IWorldStateTracker>().MarkDirty();
    }
}
