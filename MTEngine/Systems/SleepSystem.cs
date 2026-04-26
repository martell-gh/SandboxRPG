using Microsoft.Xna.Framework;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Metabolism;
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

    [SaveField("sleepTimeScale")]
    public float ActiveSleepTimeScale { get; private set; }

    [SaveField("sleepHealingPerSecond")]
    public float ActiveHealingPerSecond { get; private set; }

    private GameClock _clock = null!;
    private Entity? _sleepingActor;
    private float _previousTimeScale;

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
        RemainingSleepSeconds -= deltaTime * ActiveSleepTimeScale;

        ApplySleepHealing(_sleepingActor, deltaTime);

        if (RemainingSleepSeconds <= 0f)
            StopSleep(setWakeTime: true);
    }

    public bool TryStartSleep(Entity actor, Entity bed, BedComponent bedComponent)
    {
        if (IsSleepAccelerationActive || _clock.IsDay)
            return false;

        var health = actor.GetComponent<HealthComponent>();
        if (health?.IsDead == true)
            return false;

        _sleepingActor = actor;
        WakeHour = Math.Clamp(bedComponent.WakeHour, 0f, 23.99f);
        ActiveSleepTimeScale = Math.Max(_clock.TimeScale, bedComponent.SleepTimeScale);
        ActiveHealingPerSecond = Math.Max(0f, bedComponent.HealingPerSecond);
        _previousTimeScale = _clock.TimeScale;
        RemainingSleepSeconds = CalculateRemainingNightSeconds(_clock.Hour, WakeHour);
        IsSleepAccelerationActive = RemainingSleepSeconds > 0.01f;

        if (!IsSleepAccelerationActive)
            return false;

        PopupTextSystem.Show(actor, $"Сон до {WakeHour:00}:00...", Color.LightSkyBlue, lifetime: 1.5f);
        return true;
    }

    public bool IsSleeping(Entity entity)
        => IsSleepAccelerationActive && _sleepingActor == entity;

    public bool ShouldSuspendNeedDecay()
        => IsSleepAccelerationActive;

    private void StopSleep(bool setWakeTime)
    {
        if (setWakeTime)
            _clock.AdvanceToHour(WakeHour);

        _clock.TimeScale = _previousTimeScale > 0f ? _previousTimeScale : 72f;

        if (_sleepingActor != null && setWakeTime)
            PopupTextSystem.Show(_sleepingActor, "Проснулся.", Color.LightGoldenrodYellow, lifetime: 1.5f);

        _sleepingActor = null;
        IsSleepAccelerationActive = false;
        RemainingSleepSeconds = 0f;
        WakeHour = 6f;
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

    private static float CalculateRemainingNightSeconds(float currentHour, float wakeHour)
    {
        var clampedHour = Math.Clamp(currentHour, 0f, 24f);
        if (clampedHour >= wakeHour && clampedHour < 20f)
            return 0f;

        if (clampedHour >= 20f)
            return ((24f - clampedHour) + wakeHour) * 3600f;

        return Math.Max(0f, wakeHour - clampedHour) * 3600f;
    }
}
