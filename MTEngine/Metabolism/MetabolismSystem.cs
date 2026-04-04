using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Systems;

namespace MTEngine.Metabolism;

/// <summary>
/// Ticks every frame for all entities with MetabolismComponent.
///
/// Responsibilities:
///   1. Decay hunger and thirst over time.
///   2. Process digestion queue (gradually absorb nutrients).
///   3. Fill bladder/bowel passively + from digestion.
///   4. Apply speed penalties at Warning/Critical levels.
///   5. Show popup warnings when crossing thresholds.
///   6. Handle "accidents" if bladder/bowel hit 100.
/// </summary>
public class MetabolismSystem : GameSystem
{
    private const float WarningInterval = 8f; // seconds between popup warnings

    public override void Update(float deltaTime)
    {
        foreach (var entity in World.GetEntitiesWith<MetabolismComponent>())
        {
            var m = entity.GetComponent<MetabolismComponent>()!;
            var health = entity.GetComponent<HealthComponent>();
            if (!m.Enabled) continue;
            if (health?.IsDead == true) continue;

            UpdateDecay(m, deltaTime);
            UpdateDigestion(m, deltaTime);
            UpdateSubstances(entity, m, deltaTime);
            UpdateHealth(m, health, deltaTime);
            UpdateEffects(entity, m, deltaTime);
        }
    }

    // ── 1. Natural decay ───────────────────────────────────────────

    private static void UpdateDecay(MetabolismComponent m, float dt)
    {
        m.Hunger = Math.Clamp(m.Hunger - m.HungerDecay * dt, 0f, 100f);
        m.Thirst = Math.Clamp(m.Thirst - m.ThirstDecay * dt, 0f, 100f);
        m.Bladder = Math.Clamp(m.Bladder + m.BladderFillRate * dt, 0f, 100f);
        m.Bowel = Math.Clamp(m.Bowel + m.BowelFillRate * dt, 0f, 100f);
    }

    // ── 2. Digestion ───────────────────────────────────────────────

    private static void UpdateDigestion(MetabolismComponent m, float dt)
    {
        for (int i = m.DigestingItems.Count - 1; i >= 0; i--)
        {
            var item = m.DigestingItems[i];
            var prevProgress = item.Progress;

            item.Elapsed += dt;
            var fraction = item.Duration > 0 ? dt / item.Duration : 1f;
            fraction = Math.Min(fraction, 1f - prevProgress); // don't overshoot

            // Absorb nutrients proportionally
            var nutritionChunk = item.RemainingNutrition * (fraction / (1f - prevProgress + 0.001f));
            var hydrationChunk = item.RemainingHydration * (fraction / (1f - prevProgress + 0.001f));
            var bladderChunk = item.BladderLoad * (fraction / (1f - prevProgress + 0.001f));
            var bowelChunk = item.BowelLoad * (fraction / (1f - prevProgress + 0.001f));

            m.Hunger = Math.Clamp(m.Hunger + nutritionChunk, 0f, 100f);
            m.Thirst = Math.Clamp(m.Thirst + hydrationChunk, 0f, 100f);
            m.Bladder = Math.Clamp(m.Bladder + bladderChunk, 0f, 100f);
            m.Bowel = Math.Clamp(m.Bowel + bowelChunk, 0f, 100f);

            item.RemainingNutrition -= nutritionChunk;
            item.RemainingHydration -= hydrationChunk;
            item.BladderLoad -= bladderChunk;
            item.BowelLoad -= bowelChunk;

            if (item.IsFinished)
                m.DigestingItems.RemoveAt(i);
        }
    }

    // ── 3. Substance processing ────────────────────────────────────

    private static void UpdateSubstances(Entity entity, MetabolismComponent m, float dt)
    {
        m.SubstanceSpeedModifier = 1f;
        var nextConcentrationEffectKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = m.ActiveSubstances.Count - 1; i >= 0; i--)
        {
            var substance = m.ActiveSubstances[i];
            substance.Elapsed += dt;

            for (int effectIndex = 0; effectIndex < substance.Effects.Count; effectIndex++)
            {
                var effect = substance.Effects[effectIndex];
                if (!SubstanceEffectRegistry.TryGet(effect.Type, out var handler))
                    continue;

                var effectKey = string.IsNullOrWhiteSpace(effect.Id)
                    ? $"{effect.Type}:{effectIndex}"
                    : effect.Id;

                var startTime = substance.AbsorptionTime + Math.Max(0f, effect.Delay);
                var endTime = startTime + Math.Max(0f, effect.Duration);
                var isInstant = effect.Duration <= 0f;
                var isActive = isInstant
                    ? substance.Elapsed >= startTime
                    : substance.Elapsed >= startTime && substance.Elapsed < endTime;

                handler.Apply(new SubstanceEffectContext
                {
                    Entity = entity,
                    Metabolism = m,
                    Substance = substance,
                    Effect = effect,
                    DeltaTime = dt,
                    EffectKey = effectKey,
                    SubstanceId = substance.Id,
                    SubstanceName = substance.Name,
                    CurrentAmount = substance.CurrentAmount,
                    Profile = null,
                    IsActive = isActive,
                    IsInstant = isInstant,
                    MarkStarted = () => substance.StartedEffects.Add(effectKey),
                    MarkFinished = () => substance.FinishedEffects.Add(effectKey)
                });
            }

            if (substance.IsExpired)
                m.ActiveSubstances.RemoveAt(i);
        }

        UpdateConcentrations(entity, m, dt, nextConcentrationEffectKeys);
        m.ActiveConcentrationEffectKeys.Clear();
        foreach (var key in nextConcentrationEffectKeys)
            m.ActiveConcentrationEffectKeys.Add(key);
    }

    private static void UpdateConcentrations(
        Entity entity,
        MetabolismComponent metabolism,
        float dt,
        HashSet<string> nextConcentrationEffectKeys)
    {
        metabolism.SubstanceConcentrations.Clear();

        var groups = metabolism.ActiveSubstances
            .Where(substance => substance.CurrentAmount > 0.001f)
            .GroupBy(substance => substance.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var doses = group.ToList();
            var representative = doses
                .OrderByDescending(dose => dose.ResponseProfiles.Count)
                .ThenByDescending(dose => dose.CurrentAmount)
                .First();

            var totalAmount = doses.Sum(dose => dose.CurrentAmount);
            var profiles = representative.ResponseProfiles;

            var snapshot = new SubstanceConcentrationSnapshot
            {
                Id = representative.Id,
                Name = representative.Name,
                Amount = totalAmount,
                Profiles = profiles
            };

            metabolism.SubstanceConcentrations[group.Key] = snapshot;

            for (int profileIndex = 0; profileIndex < profiles.Count; profileIndex++)
            {
                var profile = profiles[profileIndex];
                if (!profile.Matches(totalAmount))
                    continue;

                for (int effectIndex = 0; effectIndex < profile.Effects.Count; effectIndex++)
                {
                    var effect = profile.Effects[effectIndex];
                    if (!SubstanceEffectRegistry.TryGet(effect.Type, out var handler))
                        continue;

                    var profileId = string.IsNullOrWhiteSpace(profile.Id)
                        ? $"range:{profileIndex}"
                        : profile.Id;
                    var effectId = string.IsNullOrWhiteSpace(effect.Id)
                        ? $"{effect.Type}:{effectIndex}"
                        : effect.Id;
                    var runtimeKey = $"{group.Key}:{profileId}:{effectId}";
                    nextConcentrationEffectKeys.Add(runtimeKey);

                    handler.Apply(new SubstanceEffectContext
                    {
                        Entity = entity,
                        Metabolism = metabolism,
                        Substance = representative,
                        Effect = effect,
                        DeltaTime = dt,
                        EffectKey = runtimeKey,
                        SubstanceId = representative.Id,
                        SubstanceName = representative.Name,
                        CurrentAmount = totalAmount,
                        Profile = profile,
                        IsActive = true,
                        IsInstant = effect.Duration <= 0f,
                        MarkStarted = () => !metabolism.ActiveConcentrationEffectKeys.Contains(runtimeKey),
                        MarkFinished = () => false
                    });
                }
            }
        }
    }

    private static void UpdateHealth(MetabolismComponent m, HealthComponent? health, float dt)
    {
        if (health == null || health.IsDead)
            return;

        var damage = 0f;

        if (m.HungerStatus == NeedStatus.Critical)
            damage += m.StarvationDamage * dt;

        if (m.ThirstStatus == NeedStatus.Critical)
            damage += m.DehydrationDamage * dt;

        if (damage > 0f)
            health.Health = Math.Max(0f, health.Health - damage);
    }

    // ── 4. Effects & warnings ──────────────────────────────────────

    private static void UpdateEffects(Entity entity, MetabolismComponent m, float dt)
    {
        // ── Speed modifier ─────────────────────────────────────────
        float speedMod = 1f;

        // Hunger penalties
        if (m.HungerStatus == NeedStatus.Warning) speedMod *= 0.90f;
        else if (m.HungerStatus == NeedStatus.Critical) speedMod *= 0.75f;

        // Thirst penalties (harsher)
        if (m.ThirstStatus == NeedStatus.Warning) speedMod *= 0.88f;
        else if (m.ThirstStatus == NeedStatus.Critical) speedMod *= 0.65f;

        // Bladder/bowel urgency
        if (m.BladderStatus == NeedStatus.Critical) speedMod *= 0.80f;
        if (m.BowelStatus == NeedStatus.Critical) speedMod *= 0.85f;

        // Well-fed bonus
        if (m.HungerStatus == NeedStatus.Excellent && m.ThirstStatus == NeedStatus.Excellent)
            speedMod *= 1.05f;

        speedMod *= m.SubstanceSpeedModifier;
        m.SpeedModifier = speedMod;

        // Apply to VelocityComponent if present
        var velocity = entity.GetComponent<VelocityComponent>();
        if (velocity != null)
        {
            // Store base speed on first access via Tag
            if (entity.GetComponent<MetabolismBaseSpeedTag>() == null)
            {
                entity.AddComponent(new MetabolismBaseSpeedTag { BaseSpeed = velocity.Speed });
            }
            var baseTag = entity.GetComponent<MetabolismBaseSpeedTag>()!;
            velocity.Speed = baseTag.BaseSpeed * m.SpeedModifier;
        }

        // ── Warnings ───────────────────────────────────────────────
        m.TimeSinceLastWarning += dt;
        if (m.TimeSinceLastWarning < WarningInterval) return;

        string? warning = null;
        Color color = Color.White;

        if (m.HungerStatus == NeedStatus.Critical)
        { warning = "Голодаю!"; color = Color.OrangeRed; }
        else if (m.ThirstStatus == NeedStatus.Critical)
        { warning = "Обезвоживание!"; color = Color.OrangeRed; }
        else if (m.BladderStatus == NeedStatus.Critical)
        { warning = "Срочно нужен туалет!"; color = Color.Yellow; }
        else if (m.BowelStatus == NeedStatus.Critical)
        { warning = "Срочно нужен туалет!"; color = Color.Yellow; }
        else if (m.HungerStatus == NeedStatus.Warning)
        { warning = "Хочу есть..."; color = Color.Khaki; }
        else if (m.ThirstStatus == NeedStatus.Warning)
        { warning = "Хочу пить..."; color = Color.Khaki; }

        if (warning != null)
        {
            PopupTextSystem.Show(entity, warning, color);
            m.TimeSinceLastWarning = 0f;
        }

        // ── Accidents (always unacceptable) ──────────────────────────
        if (m.Bladder >= 100f && !m.HadAccident)
        {
            m.HadAccident = true;
            m.Bladder = 20f;
            PopupTextSystem.Show(entity, "Не удержал...", Color.Purple, lifetime: 2f);
            Console.WriteLine($"[Metabolism] {entity.Name} had a bladder accident!");

            if (ServiceLocator.Has<EventBus>())
            {
                ServiceLocator.Get<EventBus>().Publish(new ReliefEvent
                {
                    Actor = entity,
                    Need = ReliefNeed.Bladder,
                    Type = ReliefType.Unacceptable
                });
            }
        }

        if (m.Bowel >= 100f && !m.HadAccident)
        {
            m.HadAccident = true;
            m.Bowel = 20f;
            PopupTextSystem.Show(entity, "Не удержал...", Color.Purple, lifetime: 2f);
            Console.WriteLine($"[Metabolism] {entity.Name} had a bowel accident!");

            if (ServiceLocator.Has<EventBus>())
            {
                ServiceLocator.Get<EventBus>().Publish(new ReliefEvent
                {
                    Actor = entity,
                    Need = ReliefNeed.Bowel,
                    Type = ReliefType.Unacceptable
                });
            }
        }

        // Reset accident flag when values are back to normal
        if (m.Bladder < 60f && m.Bowel < 60f)
            m.HadAccident = false;
    }
}

/// <summary>
/// Internal helper component to remember the entity's base speed
/// before metabolism modifiers are applied.
/// </summary>
public class MetabolismBaseSpeedTag : Component
{
    public float BaseSpeed { get; set; } = 100f;
}
