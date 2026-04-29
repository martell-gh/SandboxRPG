using Microsoft.Xna.Framework;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Systems;
using MTEngine.World;
using MTEngine.Wounds;

namespace MTEngine.Npc;

public class NpcHealingSystem : GameSystem
{
    private const float StartHealingHpRatio = 0.55f;
    private const float HealerSearchDistance = 1100f;
    private const float HealerArrivalDistance = 42f;
    private const float SelfTreatmentSeconds = 3.6f;
    private const float HealerTreatmentSeconds = 3.2f;
    private const float SelfHealAmount = 28f;
    private const float HealerHealAmount = 55f;

    private MapManager? _mapManager;

    public override void Update(float deltaTime)
    {
        _mapManager ??= ServiceLocator.Has<MapManager>() ? ServiceLocator.Get<MapManager>() : null;
        var map = _mapManager?.CurrentMap;
        if (map == null)
            return;

        foreach (var npc in World.GetEntitiesWith<NpcTagComponent, TransformComponent>().ToList())
        {
            if (!NpcLod.IsActive(npc))
                continue;

            if (npc.GetComponent<HealthComponent>()?.IsDead == true)
            {
                npc.RemoveComponent<NpcHealingComponent>();
                continue;
            }

            var healing = npc.GetComponent<NpcHealingComponent>();
            if (healing != null)
            {
                UpdateHealing(npc, healing, map, deltaTime);
                continue;
            }

            if (!ShouldStartHealing(npc))
                continue;

            var healer = FindAvailableHealer(npc);
            if (healer != null)
                StartSeekingHealer(npc, healer, map);
            else if (IsSafeForTreatment(npc))
                StartSelfTreatment(npc, map);
        }
    }

    private void UpdateHealing(Entity npc, NpcHealingComponent healing, MapData map, float deltaTime)
    {
        if (npc.GetComponent<NpcFleeComponent>() != null
            || npc.GetComponent<NpcAggressionComponent>() is { Mode: not AggressionMode.None })
        {
            CancelHealing(npc, healing);
            return;
        }

        switch (healing.Mode)
        {
            case NpcHealingMode.SeekingHealer:
                UpdateSeekingHealer(npc, healing, map);
                break;
            case NpcHealingMode.SelfTreatment:
                UpdateSelfTreatment(npc, healing, deltaTime);
                break;
            case NpcHealingMode.ReceivingHealer:
                UpdateReceivingHealer(npc, healing, deltaTime);
                break;
            case NpcHealingMode.ProvidingHealer:
                UpdateProvidingHealer(npc, healing);
                break;
        }
    }

    private void UpdateSeekingHealer(Entity patient, NpcHealingComponent healing, MapData map)
    {
        var healer = FindEntity(healing.HealerEntityId);
        if (healer == null || !IsAvailableHealer(healer, patient))
        {
            StartSelfTreatment(patient, map);
            return;
        }

        var patientPos = patient.GetComponent<TransformComponent>()!.Position;
        var healerPos = healer.GetComponent<TransformComponent>()!.Position;
        if (Vector2.Distance(patientPos, healerPos) <= HealerArrivalDistance)
        {
            StartHealerTreatment(patient, healer, map);
            return;
        }

        var intent = patient.GetComponent<NpcIntentComponent>() ?? patient.AddComponent(new NpcIntentComponent());
        intent.Action = ScheduleAction.Visit;
        intent.SetTarget(map.Id, healerPos, "", "seek_healer");
    }

    private void UpdateSelfTreatment(Entity npc, NpcHealingComponent healing, float deltaTime)
    {
        if (!IsSafeForTreatment(npc))
        {
            CancelHealing(npc, healing);
            return;
        }

        Stop(npc);
        if (!healing.StartedSpeech)
        {
            healing.StartedSpeech = true;
            PopupTextSystem.Show(npc, "Самолечение", Color.LightGreen, lifetime: 1.25f);
            SpeechBubbleSystem.Show(npc, "Надо бы подлататься.");
        }

        healing.ProgressSeconds += deltaTime;
        if (healing.ProgressSeconds < SelfTreatmentSeconds)
            return;

        Heal(npc, SelfHealAmount, stopBleeding: 5f);
        FinishHealing(npc);
    }

    private void UpdateReceivingHealer(Entity patient, NpcHealingComponent healing, float deltaTime)
    {
        var healer = FindEntity(healing.HealerEntityId);
        if (healer == null || !IsAvailableHealer(healer, patient))
        {
            StartSelfTreatment(patient, _mapManager!.CurrentMap!);
            return;
        }

        Stop(patient);
        Stop(healer);
        FaceEachOther(patient, healer);

        if (!healing.StartedSpeech)
        {
            healing.StartedSpeech = true;
            PopupTextSystem.Show(patient, "Лечение", Color.LightGreen, lifetime: 1.25f);
            SpeechBubbleSystem.Show(healer, "Сейчас подлатаем.");
        }

        healing.ProgressSeconds += deltaTime;
        if (healing.ProgressSeconds < HealerTreatmentSeconds)
            return;

        Heal(patient, HealerHealAmount, stopBleeding: 999f);
        FinishHealing(patient);
        healer.RemoveComponent<NpcHealingComponent>();
        World.GetSystem<ScheduleSystem>()?.RefreshNow();
    }

    private void UpdateProvidingHealer(Entity healer, NpcHealingComponent healing)
    {
        var patient = FindEntity(healing.PatientEntityId);
        if (patient == null || patient.GetComponent<NpcHealingComponent>()?.Mode != NpcHealingMode.ReceivingHealer)
        {
            healer.RemoveComponent<NpcHealingComponent>();
            World.GetSystem<ScheduleSystem>()?.RefreshNow();
            return;
        }

        Stop(healer);
        FaceEachOther(healer, patient);
    }

    private void StartSeekingHealer(Entity patient, Entity healer, MapData map)
    {
        var healing = patient.GetComponent<NpcHealingComponent>() ?? patient.AddComponent(new NpcHealingComponent());
        healing.Mode = NpcHealingMode.SeekingHealer;
        healing.HealerEntityId = healer.Id;
        healing.ProgressSeconds = 0f;
        healing.StartedSpeech = false;

        var healerPos = healer.GetComponent<TransformComponent>()!.Position;
        var intent = patient.GetComponent<NpcIntentComponent>() ?? patient.AddComponent(new NpcIntentComponent());
        intent.Action = ScheduleAction.Visit;
        intent.SetTarget(map.Id, healerPos, "", "seek_healer");
        SpeechBubbleSystem.Show(patient, "Мне нужен лекарь.");
        MarkDirty();
    }

    private void StartSelfTreatment(Entity npc, MapData map)
    {
        var healing = npc.GetComponent<NpcHealingComponent>() ?? npc.AddComponent(new NpcHealingComponent());
        healing.Mode = NpcHealingMode.SelfTreatment;
        healing.HealerEntityId = 0;
        healing.PatientEntityId = 0;
        healing.ProgressSeconds = 0f;
        healing.StartedSpeech = false;

        var pos = npc.GetComponent<TransformComponent>()!.Position;
        var intent = npc.GetComponent<NpcIntentComponent>() ?? npc.AddComponent(new NpcIntentComponent());
        intent.Action = ScheduleAction.Visit;
        intent.SetTarget(map.Id, pos, "", "self_heal");
        intent.MarkArrived();
        MarkDirty();
    }

    private void StartHealerTreatment(Entity patient, Entity healer, MapData map)
    {
        var patientHealing = patient.GetComponent<NpcHealingComponent>() ?? patient.AddComponent(new NpcHealingComponent());
        patientHealing.Mode = NpcHealingMode.ReceivingHealer;
        patientHealing.HealerEntityId = healer.Id;
        patientHealing.ProgressSeconds = 0f;
        patientHealing.StartedSpeech = false;

        var healerHealing = healer.GetComponent<NpcHealingComponent>() ?? healer.AddComponent(new NpcHealingComponent());
        healerHealing.Mode = NpcHealingMode.ProvidingHealer;
        healerHealing.PatientEntityId = patient.Id;
        healerHealing.ProgressSeconds = 0f;
        healerHealing.StartedSpeech = true;

        PinCurrentPosition(patient, map);
        PinCurrentPosition(healer, map);
        MarkDirty();
    }

    private static void PinCurrentPosition(Entity entity, MapData map)
    {
        var pos = entity.GetComponent<TransformComponent>()!.Position;
        var intent = entity.GetComponent<NpcIntentComponent>() ?? entity.AddComponent(new NpcIntentComponent());
        intent.Action = ScheduleAction.Visit;
        intent.SetTarget(map.Id, pos, "", "healing");
        intent.MarkArrived();
    }

    private bool ShouldStartHealing(Entity npc)
        => GetHealthRatio(npc) <= StartHealingHpRatio
           && npc.GetComponent<NpcFleeComponent>() == null
           && npc.GetComponent<NpcAggressionComponent>() is not { Mode: not AggressionMode.None };

    private bool IsSafeForTreatment(Entity npc)
        => npc.GetComponent<NpcFleeComponent>() == null
           && npc.GetComponent<NpcAggressionComponent>() is not { Mode: not AggressionMode.None };

    private Entity? FindAvailableHealer(Entity patient)
    {
        var patientPos = patient.GetComponent<TransformComponent>()!.Position;
        return World.GetEntitiesWith<NpcTagComponent, TransformComponent>()
            .Where(entity => entity != patient && IsAvailableHealer(entity, patient))
            .OrderBy(entity => Vector2.DistanceSquared(patientPos, entity.GetComponent<TransformComponent>()!.Position))
            .FirstOrDefault(entity => Vector2.Distance(patientPos, entity.GetComponent<TransformComponent>()!.Position) <= HealerSearchDistance);
    }

    private static bool IsAvailableHealer(Entity healer, Entity patient)
    {
        if (healer.GetComponent<HealthComponent>() is not { IsDead: false } health || health.Health <= health.MaxHealth * 0.35f)
            return false;
        if (healer.GetComponent<NpcFleeComponent>() != null)
            return false;
        if (healer.GetComponent<NpcAggressionComponent>() is { Mode: not AggressionMode.None })
            return false;
        var healerHealing = healer.GetComponent<NpcHealingComponent>();
        if (healerHealing != null
            && (healerHealing.Mode != NpcHealingMode.ProvidingHealer
                || healerHealing.PatientEntityId != patient.Id))
        {
            return false;
        }

        var professionId = healer.GetComponent<ProfessionComponent>()?.ProfessionId ?? "";
        return string.Equals(professionId, "doctor", StringComparison.OrdinalIgnoreCase)
               || string.Equals(professionId, "healer", StringComparison.OrdinalIgnoreCase)
               || professionId.Contains("medic", StringComparison.OrdinalIgnoreCase)
               || professionId.Contains("healer", StringComparison.OrdinalIgnoreCase);
    }

    private Entity? FindEntity(int entityId)
        => World.GetEntities().FirstOrDefault(entity => entity.Active && entity.Id == entityId);

    private static float GetHealthRatio(Entity entity)
    {
        var health = entity.GetComponent<HealthComponent>();
        if (health == null || health.MaxHealth <= 0f)
            return 1f;

        return Math.Clamp(health.Health / health.MaxHealth, 0f, 1f);
    }

    private static void Heal(Entity entity, float amount, float stopBleeding)
    {
        var wounds = entity.GetComponent<WoundComponent>();
        var health = entity.GetComponent<HealthComponent>();

        if (wounds != null)
        {
            WoundComponent.StopBleeding(entity, stopBleeding);
            var remaining = amount;
            remaining -= WoundComponent.HealDamage(entity, DamageType.Slash, remaining * 0.45f);
            remaining -= WoundComponent.HealDamage(entity, DamageType.Blunt, remaining * 0.5f);
            remaining -= WoundComponent.HealDamage(entity, DamageType.Burn, remaining);

            if (health != null)
                health.Health = Math.Max(0f, health.MaxHealth - wounds.TotalDamage);
            return;
        }

        if (health != null)
            health.Health = Math.Min(health.MaxHealth, health.Health + amount);
    }

    private void FinishHealing(Entity npc)
    {
        npc.RemoveComponent<NpcHealingComponent>();
        npc.GetComponent<NpcIntentComponent>()?.ClearTarget();
        World.GetSystem<ScheduleSystem>()?.RefreshNow();
        MarkDirty();
    }

    private void CancelHealing(Entity npc, NpcHealingComponent healing)
    {
        if (healing.HealerEntityId != 0)
            FindEntity(healing.HealerEntityId)?.RemoveComponent<NpcHealingComponent>();
        if (healing.PatientEntityId != 0)
            FindEntity(healing.PatientEntityId)?.RemoveComponent<NpcHealingComponent>();

        npc.RemoveComponent<NpcHealingComponent>();
        npc.GetComponent<NpcIntentComponent>()?.ClearTarget();
        World.GetSystem<ScheduleSystem>()?.RefreshNow();
    }

    private static void Stop(Entity entity)
    {
        if (entity.GetComponent<VelocityComponent>() is { } velocity)
            velocity.Velocity = Vector2.Zero;
        entity.GetComponent<SpriteComponent>()?.PlayDirectionalIdle(Vector2.Zero);
    }

    private static void FaceEachOther(Entity a, Entity b)
    {
        var posA = a.GetComponent<TransformComponent>()?.Position;
        var posB = b.GetComponent<TransformComponent>()?.Position;
        if (!posA.HasValue || !posB.HasValue)
            return;

        a.GetComponent<SpriteComponent>()?.PlayDirectionalIdle(posB.Value - posA.Value);
        b.GetComponent<SpriteComponent>()?.PlayDirectionalIdle(posA.Value - posB.Value);
    }

    private static void MarkDirty()
    {
        if (ServiceLocator.Has<IWorldStateTracker>())
            ServiceLocator.Get<IWorldStateTracker>().MarkDirty();
    }
}
