using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Interactions;

namespace MTEngine.Npc;

[RegisterComponent("shop")]
public class ShopComponent : Component, IInteractionSource
{
    [DataField("professionSlotId")]
    [SaveField("professionSlotId")]
    public string ProfessionSlotId { get; set; } = "";

    [DataField("ownerNpcSaveId")]
    [SaveField("ownerNpcSaveId")]
    public string OwnerNpcSaveId { get; set; } = "";

    [DataField("nextRestockDayIndex")]
    [SaveField("nextRestockDayIndex")]
    public long NextRestockDayIndex { get; set; } = -1L;

    [DataField("stock")]
    [SaveField("stock")]
    public List<ShopStockEntry> Stock { get; set; } = new();

    public bool HasStock => Stock.Any(entry => entry.Quantity > 0);

    public void AddStock(string prototypeId, int quantity, int unitPrice, int qualityTier, string displayName)
    {
        if (string.IsNullOrWhiteSpace(prototypeId) || quantity <= 0)
            return;

        var existing = Stock.FirstOrDefault(entry =>
            string.Equals(entry.PrototypeId, prototypeId, StringComparison.OrdinalIgnoreCase)
            && entry.UnitPrice == unitPrice);

        if (existing != null)
        {
            existing.Quantity += quantity;
            if (string.IsNullOrWhiteSpace(existing.DisplayName))
                existing.DisplayName = displayName;
            if (existing.QualityTier <= 0)
                existing.QualityTier = qualityTier;
            return;
        }

        Stock.Add(new ShopStockEntry
        {
            PrototypeId = prototypeId,
            Quantity = quantity,
            UnitPrice = unitPrice,
            QualityTier = qualityTier,
            DisplayName = displayName
        });
    }

    public bool TryTakeOne(ShopStockEntry entry)
    {
        if (entry.Quantity <= 0)
            return false;

        entry.Quantity--;
        Stock.RemoveAll(item => item.Quantity <= 0);
        return true;
    }

    public IEnumerable<InteractionEntry> GetInteractions(InteractionContext ctx)
    {
        if (Owner == null || ctx.Actor == Owner)
            yield break;

        if (Owner.GetComponent<MTEngine.Components.HealthComponent>()?.IsDead == true)
            yield break;

        var tradeOpen = MerchantWorkRules.IsTradeOpenNow(Owner);
        if (tradeOpen)
        {
            yield return new InteractionEntry
            {
                Id = "shop.trade",
                Label = HasStock ? "Торговать" : "Торговать (пусто)",
                Priority = 35,
                Execute = context =>
                {
                    context.World.GetSystem<TradeSystem>()?.OpenTrade(context.Actor, Owner);
                }
            };
        }

        var rental = ctx.World.GetSystem<InnRentalSystem>();
        if (tradeOpen
            && rental != null
            && MerchantWorkRules.IsInnkeeper(Owner)
            && rental.TryBuildOffer(Owner, ctx.Actor, out var offer))
        {
            yield return new InteractionEntry
            {
                Id = offer.Kind == InnRentalKind.Room ? "inn.rentRoom" : "inn.rentBed",
                Label = offer.Kind == InnRentalKind.Room
                    ? $"Снять комнату ({offer.Price} мон.)"
                    : $"Снять кровать ({offer.Price} мон.)",
                Priority = 34,
                Execute = context => rental.TryRent(context.Actor, Owner)
            };
        }
    }
}

public class ShopStockEntry
{
    public string PrototypeId { get; set; } = "";
    public int Quantity { get; set; }
    public int UnitPrice { get; set; }
    public int QualityTier { get; set; } = 4;
    public string DisplayName { get; set; } = "";
}
