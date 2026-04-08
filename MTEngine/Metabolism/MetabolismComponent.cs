using System;
using System.Collections.Generic;
using MTEngine.ECS;
using MTEngine.Interactions;

namespace MTEngine.Metabolism;

/// <summary>
/// Hunger, thirst, bladder, bowel — all in one component.
///
/// Values:
///   Hunger/Thirst:  100 = full/hydrated, 0 = starving/dehydrated. Decay over time.
///   Bladder/Bowel:  0 = empty, 100 = full. Fill up from eating/drinking.
///
/// Status thresholds (configurable via proto.json):
///   Hunger/Thirst — WellFed/Hydrated ≥ 80, Normal 30–80, Hungry/Thirsty 10–30, Starving/Dehydrated &lt; 10
///   Bladder/Bowel — Fine 0–60, NeedToGo 60–85, Urgent 85–100
/// </summary>
[RegisterComponent("metabolism")]
public class MetabolismComponent : Component, IInteractionSource
{
    // ── Hunger ─────────────────────────────────────────────────────
    [SaveField("hunger")]
    [DataField("hunger")]
    public float Hunger { get; set; } = 100f;

    /// <summary>Hunger loss per real second (before GameClock scale).</summary>
    [SaveField("hungerDecay")]
    [DataField("hungerDecay")]
    public float HungerDecay { get; set; } = 0.8f;

    // ── Thirst ─────────────────────────────────────────────────────
    [SaveField("thirst")]
    [DataField("thirst")]
    public float Thirst { get; set; } = 100f;

    /// <summary>Thirst loss per real second (decays faster than hunger).</summary>
    [SaveField("thirstDecay")]
    [DataField("thirstDecay")]
    public float ThirstDecay { get; set; } = 1.2f;

    // ── Bladder ────────────────────────────────────────────────────
    [SaveField("bladder")]
    [DataField("bladder")]
    public float Bladder { get; set; } = 0f;

    /// <summary>Passive bladder fill per real second.</summary>
    [SaveField("bladderFill")]
    [DataField("bladderFill")]
    public float BladderFillRate { get; set; } = 0.15f;

    // ── Bowel ──────────────────────────────────────────────────────
    [SaveField("bowel")]
    [DataField("bowel")]
    public float Bowel { get; set; } = 0f;

    /// <summary>Passive bowel fill per real second.</summary>
    [SaveField("bowelFill")]
    [DataField("bowelFill")]
    public float BowelFillRate { get; set; } = 0.08f;

    [SaveField("starvationDamage")]
    [DataField("starvationDamage")]
    public float StarvationDamage { get; set; } = 0.35f;

    [SaveField("dehydrationDamage")]
    [DataField("dehydrationDamage")]
    public float DehydrationDamage { get; set; } = 0.75f;

    [SaveField("naturalRegenRate")]
    [DataField("naturalRegenRate")]
    public float NaturalRegenRate { get; set; } = 1.2f;

    [SaveField("naturalRegenThreshold")]
    [DataField("naturalRegenThreshold")]
    public float NaturalRegenThreshold { get; set; } = 70f;

    [SaveField("exhaustionRecoveryRate")]
    [DataField("exhaustionRecoveryRate")]
    public float ExhaustionRecoveryRate { get; set; } = 3.5f;

    [SaveField("exhaustionRecoveryThreshold")]
    [DataField("exhaustionRecoveryThreshold")]
    public float ExhaustionRecoveryThreshold { get; set; } = 45f;

    // ── Digestion queue ────────────────────────────────────────────
    // When food/drink is consumed, nutrients are not applied instantly.
    // They enter a digestion queue and are absorbed over time.

    [SaveField]
    public List<DigestingItem> DigestingItems { get; } = new();
    [SaveField]
    public List<ActiveSubstanceDose> ActiveSubstances { get; } = new();
    [SaveField]
    public Dictionary<string, SubstanceConcentrationSnapshot> SubstanceConcentrations { get; } =
        new(StringComparer.OrdinalIgnoreCase);
    [SaveField]
    public HashSet<string> ActiveConcentrationEffectKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

    // ── Speed modifier (applied by MetabolismSystem) ───────────────
    /// <summary>Multiplier applied to VelocityComponent.Speed. 1.0 = normal.</summary>
    [SaveField]
    public float SpeedModifier { get; set; } = 1f;
    [SaveField]
    public float SubstanceSpeedModifier { get; set; } = 1f;

    // ── Thresholds (can tweak in proto.json) ───────────────────────
    [SaveField("wellFedThreshold")]
    [DataField("wellFedThreshold")]
    public float WellFedThreshold { get; set; } = 80f;

    [SaveField("hungryThreshold")]
    [DataField("hungryThreshold")]
    public float HungryThreshold { get; set; } = 30f;

    [SaveField("starvingThreshold")]
    [DataField("starvingThreshold")]
    public float StarvingThreshold { get; set; } = 10f;

    [SaveField("needToGoThreshold")]
    [DataField("needToGoThreshold")]
    public float NeedToGoThreshold { get; set; } = 60f;

    [SaveField("urgentThreshold")]
    [DataField("urgentThreshold")]
    public float UrgentThreshold { get; set; } = 85f;

    // ── Runtime state (not serialized) ─────────────────────────────
    [SaveField]
    public float TimeSinceLastWarning { get; set; }
    [SaveField]
    public bool HadAccident { get; set; }

    // ── Status helpers ─────────────────────────────────────────────

    public NeedStatus HungerStatus =>
        Hunger >= WellFedThreshold ? NeedStatus.Excellent :
        Hunger >= HungryThreshold ? NeedStatus.Normal :
        Hunger >= StarvingThreshold ? NeedStatus.Warning :
        NeedStatus.Critical;

    public NeedStatus ThirstStatus =>
        Thirst >= WellFedThreshold ? NeedStatus.Excellent :
        Thirst >= HungryThreshold ? NeedStatus.Normal :
        Thirst >= StarvingThreshold ? NeedStatus.Warning :
        NeedStatus.Critical;

    public NeedStatus BladderStatus =>
        Bladder < NeedToGoThreshold ? NeedStatus.Excellent :
        Bladder < UrgentThreshold ? NeedStatus.Warning :
        NeedStatus.Critical;

    public NeedStatus BowelStatus =>
        Bowel < NeedToGoThreshold ? NeedStatus.Excellent :
        Bowel < UrgentThreshold ? NeedStatus.Warning :
        NeedStatus.Critical;

    // ── Interactions ───────────────────────────────────────────────
    // Relief actions: toilet = acceptable, self = unacceptable.
    // All relief publishes a ReliefEvent on EventBus for NPC reactions.

    public IEnumerable<InteractionEntry> GetInteractions(InteractionContext ctx)
    {
        if (ctx.Actor != Owner)
            yield break;

        var actorMetab = ctx.Actor.GetComponent<MetabolismComponent>();
        if (actorMetab == null) yield break;

        var actorHealth = ctx.Actor.GetComponent<Components.HealthComponent>();
        if (actorHealth?.IsDead == true)
            yield break;

        // Determine context: toilet object or self-interaction
        var targetItem = ctx.Target.GetComponent<Items.ItemComponent>();
        var isToilet = ctx.Target.HasComponent<ToiletComponent>() || targetItem?.HasTag("toilet") == true;
        var isSelf = ctx.Target == ctx.Actor;

        if (!isToilet && !isSelf) yield break;

        var reliefType = isToilet ? ReliefType.Acceptable : ReliefType.Unacceptable;

        // ── Bladder ────────────────────────────────────────────────
        if (actorMetab.Bladder > 15f)
        {
            var label = isToilet
                ? "Сходить (малая нужда)"
                : "Справить нужду(малая)";

            yield return new InteractionEntry
            {
                Id = "metabolism.useBladder",
                Label = label,
                Priority = 15,
                Execute = c => DoRelief(c.Actor, ReliefNeed.Bladder, reliefType)
            };
        }

        // ── Bowel ──────────────────────────────────────────────────
        if (actorMetab.Bowel > 15f)
        {
            var label = isToilet
                ? "Сходить (большая нужда)"
                : "Справить нужду (большая)";

            yield return new InteractionEntry
            {
                Id = "metabolism.useBowel",
                Label = label,
                Priority = 14,
                Execute = c => DoRelief(c.Actor, ReliefNeed.Bowel, reliefType)
            };
        }
    }

    /// <summary>
    /// Execute a relief action. Publishes ReliefEvent on EventBus.
    /// </summary>
    public static void DoRelief(Entity actor, ReliefNeed need, ReliefType type)
    {
        var m = actor.GetComponent<MetabolismComponent>();
        if (m == null) return;

        var health = actor.GetComponent<Components.HealthComponent>();
        if (health?.IsDead == true)
            return;

        switch (need)
        {
            case ReliefNeed.Bladder:
                m.Bladder = Math.Max(0, m.Bladder - 80f);
                break;
            case ReliefNeed.Bowel:
                m.Bowel = Math.Max(0, m.Bowel - 80f);
                break;
        }

        // Visual feedback
        var color = type == ReliefType.Acceptable
            ? Microsoft.Xna.Framework.Color.CornflowerBlue
            : Microsoft.Xna.Framework.Color.MediumPurple;
        var text = type == ReliefType.Acceptable
            ? "Облегчение..."
            : "Справил нужду на месте!";

        Systems.PopupTextSystem.Show(actor, text, color, lifetime: 1.5f);

        // Publish event for NPC reaction system
        if (Core.ServiceLocator.Has<Core.EventBus>())
        {
            Core.ServiceLocator.Get<Core.EventBus>().Publish(new ReliefEvent
            {
                Actor = actor,
                Need = need,
                Type = type
            });
        }

        var typeName = type == ReliefType.Acceptable ? "acceptable" : "UNACCEPTABLE";
        Console.WriteLine($"[Metabolism] {actor.Name} relieved {need} ({typeName})");
    }
}

/// <summary>Status levels for any need.</summary>
public enum NeedStatus
{
    Excellent,  // all good
    Normal,     // fine
    Warning,    // getting bad
    Critical    // very bad, penalties active
}

/// <summary>
/// An item being digested. Nutrients are absorbed gradually over Duration seconds.
/// This makes eating feel more realistic — you don't go from 0→100 instantly.
/// </summary>
public class DigestingItem
{
    public string Name { get; init; } = "";
    public float RemainingNutrition { get; set; }
    public float RemainingHydration { get; set; }
    public float BladderLoad { get; set; }
    public float BowelLoad { get; set; }
    public float Duration { get; set; }
    public float Elapsed { get; set; }

    public bool IsFinished => Elapsed >= Duration;
    public float Progress => Duration > 0 ? Math.Clamp(Elapsed / Duration, 0, 1) : 1;
}
