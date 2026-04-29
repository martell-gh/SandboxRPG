using System.Collections.Generic;
using MTEngine.ECS;
using MTEngine.Interactions;
using MTEngine.Items;

namespace MTEngine.Crafting;

[RegisterComponent("craftingStation")]
public class CraftingStationComponent : Component, IInteractionSource
{
    [DataField("station")]
    [SaveField("station")]
    public string StationId { get; set; } = "";

    [DataField("name")]
    [SaveField("name")]
    public string StationName { get; set; } = "Станция";

    [DataField("verb")]
    [SaveField("verb")]
    public string ActionVerb { get; set; } = "Работать";

    public IEnumerable<InteractionEntry> GetInteractions(InteractionContext ctx)
    {
        if (Owner == null)
            yield break;

        var item = Owner.GetComponent<ItemComponent>();
        var usableFromHands = item?.ContainedIn == ctx.Actor && ctx.Target == ctx.Actor;
        var usableAsWorldTarget = ctx.Target == Owner;
        if (!usableFromHands && !usableAsWorldTarget)
            yield break;

        yield return new InteractionEntry
        {
            Id = $"crafting.open.{Owner.Id}",
            Label = $"{ActionVerb} ({StationName})",
            Priority = 26,
            Execute = c => c.World.GetSystem<CraftingSystem>()?.OpenCrafting(c.Actor, Owner, this)
        };
    }
}
