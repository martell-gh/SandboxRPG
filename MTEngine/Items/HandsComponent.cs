using Microsoft.Xna.Framework;
using MTEngine.Components;
using MTEngine.ECS;
using MTEngine.Interactions;
using MTEngine.Core;
using MTEngine.Rendering;
using MTEngine.Systems;

namespace MTEngine.Items;

/// <summary>
/// Gives an entity hands to hold items. Supports active hand,
/// hand swapping, two-handed items blocking other hands.
/// Add this to the player (or NPCs that should hold things).
/// </summary>
[RegisterComponent("hands")]
public class HandsComponent : Component, IInteractionSource
{
    private const float CursorDropRadius = 72f;

    /// <summary>Number of hands (created on first access).</summary>
    [DataField("count")]
    public int HandCount { get; set; } = 2;

    /// <summary>Hand names in order, comma-separated. Defaults to "Left,Right" etc.</summary>
    [DataField("names")]
    public string HandNames { get; set; } = "";

    private List<Hand>? _hands;

    /// <summary>All hands on this entity.</summary>
    public List<Hand> Hands
    {
        get
        {
            if (_hands == null)
                InitializeHands();
            return _hands!;
        }
    }

    /// <summary>Index of the currently active hand.</summary>
    public int ActiveHandIndex { get; set; } = 0;

    /// <summary>The currently active hand (or null if no hands).</summary>
    public Hand? ActiveHand => ActiveHandIndex < Hands.Count ? Hands[ActiveHandIndex] : null;

    /// <summary>The item in the active hand (or null).</summary>
    public Entity? ActiveItem => ActiveHand?.HeldItem;

    private void InitializeHands()
    {
        _hands = new List<Hand>();
        var names = string.IsNullOrEmpty(HandNames)
            ? GetDefaultNames(HandCount)
            : HandNames.Split(',', StringSplitOptions.TrimEntries);

        for (int i = 0; i < HandCount; i++)
        {
            var name = i < names.Length ? names[i] : $"Hand {i + 1}";
            _hands.Add(new Hand(name));
        }
    }

    private static string[] GetDefaultNames(int count) => count switch
    {
        1 => ["Hand"],
        2 => ["Left", "Right"],
        _ => Enumerable.Range(1, count).Select(i => $"Hand {i}").ToArray()
    };

    // ── Queries ──

    /// <summary>Find the first free hand, or null.</summary>
    public Hand? GetFreeHand()
        => Hands.FirstOrDefault(h => h.IsFree);

    /// <summary>Count how many hands are currently free.</summary>
    public int FreeHandCount()
        => Hands.Count(h => h.IsFree);

    /// <summary>Find which hand holds a specific item.</summary>
    public Hand? GetHandWith(Entity item)
        => Hands.FirstOrDefault(h => h.HeldItem == item);

    public int GetHandIndex(Hand hand)
        => Hands.IndexOf(hand);

    /// <summary>Do we have enough free hands for this item?</summary>
    public bool HasFreeHandFor(Entity itemEntity)
    {
        var item = itemEntity.GetComponent<ItemComponent>();
        if (item == null) return false;

        if (item.TwoHanded)
            return FreeHandCount() >= 2;

        return GetFreeHand() != null;
    }

    /// <summary>Can this entity pick up the given item right now? (checks item is free + hands available)</summary>
    public bool CanPickUp(Entity itemEntity)
    {
        var item = itemEntity.GetComponent<ItemComponent>();
        if (item == null || !item.IsFree) return false;

        return HasFreeHandFor(itemEntity);
    }

    // ── Actions ──

    /// <summary>Pick up an item into the active hand (or first free hand).</summary>
    public bool TryPickUp(Entity itemEntity)
    {
        var item = itemEntity.GetComponent<ItemComponent>();
        if (item == null || !item.IsFree) return false;

        if (item.TwoHanded)
            return TryPickUpTwoHanded(itemEntity, item);

        // Prefer active hand, fall back to first free
        var hand = ActiveHand?.IsFree == true ? ActiveHand : GetFreeHand();
        if (hand == null) return false;

        hand.HeldItem = itemEntity;
        item.ContainedIn = Owner;

        // Hide item from the world
        itemEntity.Active = false;

        PopupTextSystem.Show(Owner!, $"+ {item.ItemName}", Color.LightGreen);
        Console.WriteLine($"[Hands] {Owner?.Name} picked up {item.ItemName} in {hand.Name}");
        return true;
    }

    public bool TryPickUpInto(Entity itemEntity, int handIndex)
    {
        var item = itemEntity.GetComponent<ItemComponent>();
        if (item == null || !item.IsFree) return false;
        if (handIndex < 0 || handIndex >= Hands.Count) return false;

        var targetHand = Hands[handIndex];
        if (!targetHand.IsFree) return false;

        if (item.TwoHanded)
        {
            if (FreeHandCount() < 2) return false;

            targetHand.HeldItem = itemEntity;
            item.ContainedIn = Owner;
            itemEntity.Active = false;

            foreach (var hand in Hands)
            {
                if (hand != targetHand && hand.IsFree)
                {
                    hand.BlockedByTwoHanded = true;
                    break;
                }
            }

            ActiveHandIndex = handIndex;
            PopupTextSystem.Show(Owner!, $"+ {item.ItemName}", Color.LightGreen);
            return true;
        }

        targetHand.HeldItem = itemEntity;
        item.ContainedIn = Owner;
        itemEntity.Active = false;
        ActiveHandIndex = handIndex;
        PopupTextSystem.Show(Owner!, $"+ {item.ItemName}", Color.LightGreen);
        return true;
    }

    public bool TryMoveToHand(Entity itemEntity, int handIndex)
    {
        if (handIndex < 0 || handIndex >= Hands.Count) return false;

        var sourceHand = GetHandWith(itemEntity);
        if (sourceHand == null) return false;

        var item = itemEntity.GetComponent<ItemComponent>();
        if (item == null) return false;

        if (item.TwoHanded)
        {
            ActiveHandIndex = handIndex;
            return true;
        }

        var targetHand = Hands[handIndex];
        if (!targetHand.IsFree) return false;

        sourceHand.HeldItem = null;
        targetHand.HeldItem = itemEntity;
        ActiveHandIndex = handIndex;
        return true;
    }

    private bool TryPickUpTwoHanded(Entity itemEntity, ItemComponent item)
    {
        if (FreeHandCount() < 2) return false;

        // Put in active hand (or first free), block all other free hands
        var primaryHand = ActiveHand?.IsFree == true ? ActiveHand : GetFreeHand();
        if (primaryHand == null) return false;

        primaryHand.HeldItem = itemEntity;
        item.ContainedIn = Owner;
        itemEntity.Active = false;

        // Block one other free hand
        foreach (var h in Hands)
        {
            if (h != primaryHand && h.IsFree)
            {
                h.BlockedByTwoHanded = true;
                break;
            }
        }

        PopupTextSystem.Show(Owner!, $"+ {item.ItemName}", Color.LightGreen);
        Console.WriteLine($"[Hands] {Owner?.Name} picked up {item.ItemName} (two-handed)");
        return true;
    }

    /// <summary>Drop the item from a specific hand onto the ground.</summary>
    public bool TryDrop(Hand hand)
        => TryDrop(hand, null);

    public bool TryDrop(Hand hand, Vector2? worldPosition)
    {
        if (hand.HeldItem == null) return false;

        var itemEntity = hand.HeldItem;
        var item = itemEntity.GetComponent<ItemComponent>();

        hand.HeldItem = null;

        // Unblock hands if it was two-handed
        if (item is { TwoHanded: true })
        {
            foreach (var h in Hands)
                h.BlockedByTwoHanded = false;
        }

        if (item != null)
            item.ContainedIn = null;

        // Place item at owner's position and make visible
        var ownerTf = Owner?.GetComponent<TransformComponent>();
        var itemTf = itemEntity.GetComponent<TransformComponent>();
        if (ownerTf != null && itemTf != null)
            itemTf.Position = ResolveDropPosition(ownerTf.Position, worldPosition);

        itemEntity.Active = true;

        PopupTextSystem.Show(Owner!, item?.ItemName ?? "Dropped", Color.Silver);
        Console.WriteLine($"[Hands] {Owner?.Name} dropped {item?.ItemName ?? "item"}");
        return true;
    }

    public bool RemoveFromHand(Entity itemEntity)
    {
        var hand = GetHandWith(itemEntity);
        if (hand == null) return false;

        var item = itemEntity.GetComponent<ItemComponent>();
        hand.HeldItem = null;

        if (item is { TwoHanded: true })
        {
            foreach (var h in Hands)
                h.BlockedByTwoHanded = false;
        }

        if (item != null)
            item.ContainedIn = null;

        return true;
    }

    /// <summary>Drop the item from the active hand.</summary>
    public bool TryDropActive()
    {
        if (ActiveHand == null)
            return false;

        return TryDrop(ActiveHand, TryGetCursorDropPosition());
    }

    /// <summary>Cycle the active hand to the next one.</summary>
    public void SwapActiveHand()
    {
        if (Hands.Count <= 1) return;
        if (ActiveItem?.GetComponent<ItemComponent>()?.TwoHanded == true) return;
        ActiveHandIndex = (ActiveHandIndex + 1) % Hands.Count;
        Console.WriteLine($"[Hands] Active hand: {ActiveHand?.Name}");
    }

    // ── Interaction Source ──

    public IEnumerable<InteractionEntry> GetInteractions(InteractionContext ctx)
    {
        // HandsComponent is on the ACTOR (player), not the target.
        // It doesn't add actions to the right-click menu of other entities.
        // ItemComponent and StorageComponent handle pickup/store interactions.
        yield break;
    }

    private static Vector2 ResolveDropPosition(Vector2 ownerPosition, Vector2? desiredPosition)
    {
        if (!desiredPosition.HasValue)
            return ownerPosition;

        var offset = desiredPosition.Value - ownerPosition;
        if (offset.LengthSquared() <= CursorDropRadius * CursorDropRadius)
            return desiredPosition.Value;

        if (offset == Vector2.Zero)
            return ownerPosition;

        offset.Normalize();
        return ownerPosition + offset * CursorDropRadius;
    }

    private static Vector2? TryGetCursorDropPosition()
    {
        if (!ServiceLocator.Has<InputManager>() || !ServiceLocator.Has<Camera>())
            return null;

        var input = ServiceLocator.Get<InputManager>();
        var camera = ServiceLocator.Get<Camera>();
        return camera.ScreenToWorld(new Vector2(input.MousePosition.X, input.MousePosition.Y));
    }
}
