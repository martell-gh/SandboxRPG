using System;
using System.Collections.Generic;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Interactions;
using MTEngine.Items;

namespace MTEngine.Metabolism;

/// <summary>
/// Makes an item edible, drinkable, or both.
/// Add this to any item prototype alongside "item" component.
///
/// When consumed:
///   1. Nutrition/Hydration enter digestion queue (absorbed over DigestTime).
///   2. BladderLoad/BowelLoad also enter queue (applied as digestion progresses).
///   3. Item is destroyed (or stack decremented if stackable).
///
/// Proto example:
///   "consumable": {
///     "nutrition": 35,
///     "hydration": 10,
///     "bladderLoad": 8,
///     "bowelLoad": 20,
///     "digestTime": 30,
///     "eatVerb": "Съесть",
///     "type": "Food"
///   }
/// </summary>
[RegisterComponent("consumable")]
public class ConsumableComponent : Component, IInteractionSource, IPrototypeInitializable
{
    /// <summary>How much hunger this restores (spread over digestion time).</summary>
    [DataField("nutrition")]
    [SaveField("nutrition")]
    public float Nutrition { get; set; } = 25f;

    /// <summary>How much thirst this restores (spread over digestion time).</summary>
    [DataField("hydration")]
    [SaveField("hydration")]
    public float Hydration { get; set; } = 0f;

    /// <summary>How much bladder fills from consuming this.</summary>
    [DataField("bladderLoad")]
    [SaveField("bladderLoad")]
    public float BladderLoad { get; set; } = 5f;

    /// <summary>How much bowel fills from consuming this.</summary>
    [DataField("bowelLoad")]
    [SaveField("bowelLoad")]
    public float BowelLoad { get; set; } = 15f;

    /// <summary>Seconds to fully digest (nutrients absorbed gradually).</summary>
    [DataField("digestTime")]
    [SaveField("digestTime")]
    public float DigestTime { get; set; } = 30f;

    /// <summary>Food, Drink, or Both — affects interaction label.</summary>
    [DataField("type")]
    [SaveField("type")]
    public ConsumableType Type { get; set; } = ConsumableType.Food;

    /// <summary>Custom verb for the interaction menu (e.g. "Выпить", "Съесть").</summary>
    [DataField("eatVerb")]
    [SaveField("eatVerb")]
    public string? EatVerb { get; set; }

    /// <summary>Optional embedded substances that enter the body together with this item.</summary>
    [DataField("substances")]
    [SaveField("substances")]
    public List<SubstanceReference> SubstanceRefs { get; set; } = new();

    public List<SubstanceDose> Substances { get; private set; } = new();

    public void InitializeFromPrototype(EntityPrototype proto, AssetManager assets)
    {
        Substances = SubstanceResolver.ResolveMany(SubstanceRefs);
    }

    public string GetVerb() => EatVerb ?? Type switch
    {
        ConsumableType.Food => "Съесть",
        ConsumableType.Drink => "Выпить",
        ConsumableType.Both => "Употребить",
        _ => "Употребить"
    };

    // ── Interactions ───────────────────────────────────────────────

    public IEnumerable<InteractionEntry> GetInteractions(InteractionContext ctx)
    {
        // Only show eat/drink if actor has metabolism AND is holding this item
        var actorMetab = ctx.Actor.GetComponent<MetabolismComponent>();
        if (actorMetab == null) yield break;

        var hands = ctx.Actor.GetComponent<HandsComponent>();
        if (hands == null) yield break;

        // Item must be in the actor's hands
        var item = Owner?.GetComponent<ItemComponent>();
        if (item == null) yield break;
        if (item.ContainedIn != ctx.Actor) yield break;

        // Self-use should work both from the item context and from actor self-context.
        if (ctx.Target != Owner && ctx.Target != ctx.Actor) yield break;

        var verb = GetVerb();
        var name = item.ItemName;

        yield return new InteractionEntry
        {
            Id = "consumable.consume",
            Label = $"{verb} ({name})",
            Priority = 25,
            Execute = c => Consume(c.Actor)
        };
    }

    /// <summary>Consume this item — add to digestion queue and destroy/decrement stack.</summary>
    public void Consume(Entity actor)
    {
        var metab = actor.GetComponent<MetabolismComponent>();
        if (metab == null) return;

        var item = Owner?.GetComponent<ItemComponent>();
        if (item == null) return;

        // Add to digestion queue
        metab.DigestingItems.Add(new DigestingItem
        {
            Name = item.ItemName,
            RemainingNutrition = Nutrition,
            RemainingHydration = Hydration,
            BladderLoad = BladderLoad,
            BowelLoad = BowelLoad,
            Duration = Math.Max(1f, DigestTime),
            Elapsed = 0f
        });

        foreach (var substance in Substances)
        {
            if (substance.Nutrition > 0f || substance.Hydration > 0f || substance.BladderLoad > 0f || substance.BowelLoad > 0f)
            {
                metab.DigestingItems.Add(new DigestingItem
                {
                    Name = $"{item.ItemName}:{substance.Name}",
                    RemainingNutrition = substance.Nutrition,
                    RemainingHydration = substance.Hydration,
                    BladderLoad = substance.BladderLoad,
                    BowelLoad = substance.BowelLoad,
                    Duration = Math.Max(1f, substance.AbsorptionTime),
                    Elapsed = 0f
                });
            }

            metab.ActiveSubstances.Add(substance.ToActiveDose(item.ItemName));
        }

        var verb = GetVerb();
        Systems.PopupTextSystem.Show(actor, $"{verb}: {item.ItemName}", Microsoft.Xna.Framework.Color.YellowGreen);
        Console.WriteLine($"[Metabolism] {actor.Name} consumed {item.ItemName} " +
                          $"(+{Nutrition} food, +{Hydration} water, digest {DigestTime}s)");

        // Remove item from hands
        var hands = actor.GetComponent<HandsComponent>();
        if (hands != null)
        {
            var hand = hands.GetHandWith(Owner!);
            if (hand != null)
            {
                hand.HeldItem = null;
                if (item.TwoHanded)
                    foreach (var h in hands.Hands)
                        h.BlockedByTwoHanded = false;
            }
        }

        // Destroy or decrement stack
        if (item.Stackable && item.StackCount > 1)
        {
            item.StackCount--;
        }
        else
        {
            item.ContainedIn = null;
            Owner!.Active = false;
            Owner.World?.DestroyEntity(Owner);
        }

        if (ServiceLocator.Has<IWorldStateTracker>())
            ServiceLocator.Get<IWorldStateTracker>().MarkDirty();
    }
}

public enum ConsumableType
{
    Food,
    Drink,
    Both
}
