using System;
using System.Collections.Generic;
using System.Linq;
using MTEngine.Components;
using MTEngine.ECS;
using MTEngine.Interactions;

namespace MTEngine.Items;

/// <summary>
/// Makes an entity a pickable item. Any entity with this can be stored
/// in containers, held in hands, dropped on the ground.
/// </summary>
[RegisterComponent("item")]
public class ItemComponent : Component, IInteractionSource
{
    /// <summary>Item name shown in UI and interaction labels.</summary>
    [DataField("name")]
    [SaveField("name")]
    public string ItemName { get; set; } = "Item";

    /// <summary>Size category — containers check this against MaxItemSize.</summary>
    [DataField("size")]
    [SaveField("size")]
    public ItemSize Size { get; set; } = ItemSize.Small;

    /// <summary>If true, requires two hands to hold.</summary>
    [DataField("twoHanded")]
    [SaveField("twoHanded")]
    public bool TwoHanded { get; set; } = false;

    /// <summary>If true, identical items can stack in one slot.</summary>
    [DataField("stackable")]
    [SaveField("stackable")]
    public bool Stackable { get; set; } = false;

    /// <summary>Max stack size (only matters if Stackable).</summary>
    [DataField("maxStack")]
    [SaveField("maxStack")]
    public int MaxStack { get; set; } = 1;

    /// <summary>Current stack count.</summary>
    [DataField("stack")]
    [SaveField("stack")]
    public int StackCount { get; set; } = 1;

    /// <summary>How many storage slots this item occupies.</summary>
    [DataField("slots")]
    [SaveField("slots")]
    public int SlotSize { get; set; } = 1;

    /// <summary>Free-form semantic tags like "tool", "ammo", "medical", "backpack".</summary>
    [DataField("tags")]
    [SaveField("tags")]
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// The entity this item is currently inside (container/hand owner).
    /// Null = on the ground / free in the world.
    /// </summary>
    public Entity? ContainedIn { get; set; }

    /// <summary>Is this item currently free in the world (not in any container/hand)?</summary>
    public bool IsFree => ContainedIn == null;

    public bool HasTag(string tag)
        => Tags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));

    public int AvailableStackSpace
        => Stackable ? Math.Max(0, MaxStack - StackCount) : 0;

    public bool CanStackWith(ItemComponent? other)
    {
        if (other == null || !Stackable || !other.Stackable)
            return false;

        if (ReferenceEquals(this, other))
            return false;

        if (TwoHanded || other.TwoHanded)
            return false;

        if (MaxStack <= 1 || other.MaxStack <= 1)
            return false;

        if (!string.IsNullOrWhiteSpace(Owner?.PrototypeId) && !string.IsNullOrWhiteSpace(other.Owner?.PrototypeId))
            return string.Equals(Owner.PrototypeId, other.Owner.PrototypeId, StringComparison.OrdinalIgnoreCase);

        return string.Equals(ItemName, other.ItemName, StringComparison.OrdinalIgnoreCase);
    }

    public int MergeFrom(ItemComponent? other)
    {
        if (!CanStackWith(other) || other == null || AvailableStackSpace <= 0 || other.StackCount <= 0)
            return 0;

        var transferred = Math.Min(AvailableStackSpace, other.StackCount);
        StackCount += transferred;
        other.StackCount -= transferred;
        return transferred;
    }

    public IEnumerable<InteractionEntry> GetInteractions(InteractionContext ctx)
    {
        // Only show "Pick up" if item is free in the world
        if (!IsFree) yield break;

        var hands = ctx.Actor.GetComponent<HandsComponent>();
        if (hands == null) yield break;

        // Check if actor can hold this item
        if (hands.CanPickUp(Owner!))
        {
            var label = TwoHanded ? $"Взять ({ItemName}) [2H]" : $"Взять ({ItemName})";
            yield return new InteractionEntry
            {
                Id = "item.pickup",
                Label = label,
                Priority = 10,
                Execute = c =>
                {
                    var h = c.Actor.GetComponent<HandsComponent>();
                    h?.TryPickUp(Owner!);
                }
            };
        }
    }
}
