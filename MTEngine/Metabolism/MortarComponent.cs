using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using MTEngine.ECS;
using MTEngine.Interactions;
using MTEngine.Items;

namespace MTEngine.Metabolism;

[RegisterComponent("mortar")]
public class MortarComponent : Component, IInteractionSource, ISubstanceReservoir
{
    [DataField("name")]
    public string MortarName { get; set; } = "Mortar";

    [DataField("capacity")]
    public float Capacity { get; set; } = 100f;

    public List<SubstanceDose> Buffer { get; } = new();

    public string DisplayName => MortarName;
    public bool HasSubstances => Buffer.Sum(dose => dose.Amount) > 0.001f;

    public IReadOnlyList<SubstanceDose> GetSubstances() => Buffer;

    public IEnumerable<InteractionEntry> GetInteractions(InteractionContext ctx)
    {
        var item = Owner?.GetComponent<ItemComponent>();
        if (item == null || ctx.Target != Owner || item.ContainedIn != ctx.Actor)
            yield break;

        var hands = ctx.Actor.GetComponent<HandsComponent>();
        if (hands == null)
            yield break;

        foreach (var hand in hands.Hands)
        {
            var held = hand.HeldItem;
            if (held == null || held == Owner)
                continue;

            var source = held.GetComponent<SubstanceSourceComponent>();
            if (source == null)
                continue;

            var heldItem = held.GetComponent<ItemComponent>();
            yield return new InteractionEntry
            {
                Id = $"mortar.load.{held.Id}",
                Label = $"Измельчить ({heldItem?.ItemName ?? held.Name})",
                Priority = 26,
                Execute = c => LoadFromItem(c.Actor, held)
            };
        }

        if (HasSubstances)
        {
            yield return new InteractionEntry
            {
                Id = "mortar.inspect",
                Label = $"Осмотреть ({MortarName})",
                Priority = 20,
                Execute = c =>
                {
                    Systems.PopupTextSystem.Show(c.Actor, "Состав выведен в консоль", Color.LightCyan, lifetime: 1.5f);
                    Console.WriteLine($"[Mortar] {DescribeContents()}");
                }
            };

            yield return new InteractionEntry
            {
                Id = "mortar.clear",
                Label = $"Очистить ({MortarName})",
                Priority = 18,
                Execute = c =>
                {
                    Buffer.Clear();
                    Systems.PopupTextSystem.Show(c.Actor, "Толкушка очищена", Color.Silver, lifetime: 1.5f);
                }
            };
        }
    }

    public void LoadFromItem(Entity actor, Entity sourceEntity)
    {
        var source = sourceEntity.GetComponent<SubstanceSourceComponent>();
        var hands = actor.GetComponent<HandsComponent>();
        var item = sourceEntity.GetComponent<ItemComponent>();

        if (source == null || hands == null)
            return;

        var incoming = source.CreateYield();
        var currentAmount = Buffer.Sum(dose => dose.Amount);
        var incomingAmount = incoming.Sum(dose => dose.Amount);
        if (currentAmount + incomingAmount > Capacity + 0.001f)
        {
            Systems.PopupTextSystem.Show(actor, "В толкушке нет места", Color.OrangeRed, lifetime: 1.5f);
            return;
        }

        foreach (var dose in incoming)
            Buffer.Add(dose);

        hands.RemoveFromHand(sourceEntity);
        item!.ContainedIn = null;
        sourceEntity.Active = false;
        sourceEntity.World?.DestroyEntity(sourceEntity);

        Systems.PopupTextSystem.Show(actor, $"Измельчено: {item.ItemName}", Color.LightGoldenrodYellow, lifetime: 1.5f);
        Console.WriteLine($"[Mortar] Loaded {item.ItemName} into {MortarName}");
    }

    public float TransferSubstanceTo(LiquidContainerComponent target, string substanceId, float amount)
    {
        if (amount <= 0.001f || target.FreeCapacity <= 0.001f)
            return 0f;

        var remaining = Math.Min(amount, target.FreeCapacity);
        var moved = 0f;

        foreach (var dose in Buffer.Where(dose =>
                     string.Equals(dose.Id, substanceId, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            if (remaining <= 0.001f)
                break;

            var take = Math.Min(dose.Amount, remaining);
            if (take <= 0.001f)
                continue;

            var ratio = dose.Amount <= 0f ? 1f : take / dose.Amount;
            var part = dose.CloneScaled(ratio);
            part.Amount = take;
            part.Volume = Math.Min(part.EffectiveVolume, take);
            target.AddSubstance(part);

            dose.Amount = Math.Max(0f, dose.Amount - take);
            dose.Volume = Math.Max(0f, dose.EffectiveVolume - part.EffectiveVolume);
            remaining -= take;
            moved += take;
        }

        Cleanup();
        return moved;
    }

    public string DescribeContents()
    {
        if (!HasSubstances)
            return $"{MortarName}: пусто.";

        var parts = string.Join(", ", Buffer.Select(dose => $"{dose.Name} x{dose.Amount:0.##}"));
        return $"{MortarName}: {parts}.";
    }

    private void Cleanup()
    {
        Buffer.RemoveAll(dose => dose.Amount <= 0.001f && dose.EffectiveVolume <= 0.001f);
    }
}
