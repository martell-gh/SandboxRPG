using Microsoft.Xna.Framework;
using MTEngine.ECS;
using MTEngine.Interactions;
using MTEngine.Items;
using MTEngine.Systems;

namespace MTEngine.Components;

[RegisterComponent("currency")]
public class CurrencyComponent : Component, IInteractionSource
{
    [DataField("amount")]
    [SaveField("amount")]
    public int Amount { get; set; }

    [DataField("name")]
    [SaveField("name")]
    public string CurrencyName { get; set; } = "Монеты";

    [DataField("symbol")]
    [SaveField("symbol")]
    public string Symbol { get; set; } = "мон.";

    public bool HasAny => Amount > 0;

    public bool CanAfford(int value)
        => value <= 0 || Amount >= value;

    public void Add(int value)
    {
        if (value <= 0)
            return;

        Amount += value;
    }

    public int Remove(int value)
    {
        if (value <= 0 || Amount <= 0)
            return 0;

        var removed = Math.Min(Amount, value);
        Amount -= removed;
        return removed;
    }

    public bool TrySpend(int value)
    {
        if (!CanAfford(value))
            return false;

        Remove(value);
        return true;
    }

    public string GetDisplayText()
        => $"{Amount} {Symbol}";

    public IEnumerable<InteractionEntry> GetInteractions(InteractionContext ctx)
    {
        if (Owner == null || Amount <= 0)
            yield break;

        var item = Owner.GetComponent<ItemComponent>();
        if (item == null || item.ContainedIn != ctx.Actor || ctx.Actor != ctx.Target)
            yield break;

        yield return new InteractionEntry
        {
            Id = "currency.collectFromWallet",
            Label = $"Ссыпать деньги из {item.ItemName}",
            Priority = 17,
            Execute = c => TransferToActor(c.Actor)
        };
    }

    private void TransferToActor(Entity actor)
    {
        if (Amount <= 0)
            return;

        var sourceEntity = Owner;
        var targetCurrency = actor.GetComponent<CurrencyComponent>();
        if (targetCurrency == null)
        {
            targetCurrency = actor.AddComponent(new CurrencyComponent
            {
                CurrencyName = CurrencyName,
                Symbol = Symbol
            });
        }

        var moved = Amount;
        Amount = 0;
        targetCurrency.Add(moved);

        var sourceName = sourceEntity?.GetComponent<ItemComponent>()?.ItemName ?? sourceEntity?.Name ?? "кошель";
        PopupTextSystem.Show(actor, $"+{moved} {targetCurrency.Symbol} из {sourceName}", Color.Gold, lifetime: 1.6f);

        if (sourceEntity?.GetComponent<ItemComponent>() != null)
        {
            actor.GetComponent<HandsComponent>()?.RemoveFromHand(sourceEntity);
            sourceEntity.Active = false;
            sourceEntity.World?.DestroyEntity(sourceEntity);
        }
    }
}
