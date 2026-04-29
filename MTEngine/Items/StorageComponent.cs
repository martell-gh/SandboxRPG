using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Interactions;
using MTEngine.Npc;
using MTEngine.Systems;

namespace MTEngine.Items;

/// <summary>
/// Universal container. Works for anything: chest, backpack, pocket,
/// shelf, fridge, locker — no hardcoded types.
/// Configure via MaxSlots, MaxItemSize, and optional Whitelist/Blacklist.
/// </summary>
[RegisterComponent("storage")]
public class StorageComponent : Component, IInteractionSource, IPrototypeInitializable
{
    /// <summary>Display name for UI (e.g. "Backpack", "Chest", "Pocket").</summary>
    [DataField("name")]
    [SaveField("name")]
    public string StorageName { get; set; } = "Storage";

    /// <summary>How many item slots this container has.</summary>
    [DataField("slots")]
    [SaveField("slots")]
    public int MaxSlots { get; set; } = 10;

    /// <summary>Maximum item size that fits (see ItemSize enum).</summary>
    [DataField("maxItemSize")]
    [SaveField("maxItemSize")]
    public int MaxItemSize { get; set; } = (int)ItemSize.Huge;

    /// <summary>
    /// Optional whitelist of item IDs (proto IDs) that can be stored.
    /// Empty = anything fits (within size limit).
    /// </summary>
    [DataField("whitelist")]
    [SaveField("whitelist")]
    public List<string> Whitelist { get; set; } = new();

    /// <summary>Optional blacklist of item proto IDs or tags that cannot be stored.</summary>
    [DataField("blacklist")]
    [SaveField("blacklist")]
    public List<string> Blacklist { get; set; } = new();

    /// <summary>
    /// Explicit tag whitelist: if not empty, item must have at least one of these tags.
    /// Useful for containers like keyrings, ammo pouches, herb bags.
    /// </summary>
    [DataField("allowedTags")]
    [SaveField("allowedTags")]
    public List<string> AllowedTags { get; set; } = new();

    /// <summary>Explicit tag blacklist for items that should never fit into this storage.</summary>
    [DataField("blockedTags")]
    [SaveField("blockedTags")]
    public List<string> BlockedTags { get; set; } = new();

    [DataField("spawnContents")]
    [SaveField("spawnContents")]
    public List<string> SpawnContents { get; set; } = new();

    [SaveField("spawnContentsInitialized")]
    public bool SpawnContentsInitialized { get; set; }

    [SaveField("initialContentsResolved")]
    public bool InitialContentsResolved { get; set; }

    public bool RepairAttemptedThisSession { get; set; }

    /// <summary>Items currently stored in this container.</summary>
    public List<Entity> Contents { get; } = new();

    /// <summary>How many slots are currently used.</summary>
    public int UsedSlots => Contents.Sum(GetSlotSize);

    /// <summary>How many slots are free.</summary>
    public int FreeSlots => MaxSlots - UsedSlots;

    /// <summary>Is there room for at least one more item?</summary>
    public bool HasSpace => UsedSlots < MaxSlots;

    // ── Queries ──

    /// <summary>Can this specific item be inserted?</summary>
    public bool CanInsert(Entity itemEntity)
    {
        var item = itemEntity.GetComponent<ItemComponent>();
        if (item == null) return false;
        if (!HasSpaceFor(itemEntity)) return false;
        if ((int)item.Size > MaxItemSize) return false;

        if (AllowedTags.Count > 0 && !MatchesAnyTag(item, AllowedTags))
            return false;

        if (BlockedTags.Count > 0 && MatchesAnyTag(item, BlockedTags))
            return false;

        if (Whitelist.Count > 0 && !MatchesAnyFilter(itemEntity, item, Whitelist))
            return false;

        if (Blacklist.Count > 0 && MatchesAnyFilter(itemEntity, item, Blacklist))
            return false;

        // Don't store yourself
        if (itemEntity == Owner) return false;

        return true;
    }

    public int GetSlotSize(Entity itemEntity)
        => Math.Max(1, itemEntity.GetComponent<ItemComponent>()?.SlotSize ?? 1);

    public bool HasSpaceFor(Entity itemEntity)
        => UsedSlots + GetSlotSize(itemEntity) <= MaxSlots;

    private static bool MatchesAnyFilter(Entity itemEntity, ItemComponent item, IEnumerable<string> filters)
    {
        foreach (var filter in filters)
        {
            if (string.IsNullOrWhiteSpace(filter))
                continue;

            if (string.Equals(itemEntity.PrototypeId, filter, StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(itemEntity.Name, filter, StringComparison.OrdinalIgnoreCase))
                return true;

            if (item.HasTag(filter))
                return true;
        }

        return false;
    }

    private static bool MatchesAnyTag(ItemComponent item, IEnumerable<string> tags)
    {
        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag))
                continue;

            if (item.HasTag(tag))
                return true;
        }

        return false;
    }

    // ── Actions ──

    /// <summary>Insert an item into this container.</summary>
    public bool TryInsert(Entity itemEntity)
    {
        if (!CanInsert(itemEntity)) return false;
        var item = itemEntity.GetComponent<ItemComponent>()!;
        InsertCore(itemEntity, item);
        MarkWorldDirty();

        PopupTextSystem.Show(ResolveDropAnchor() ?? Owner!, $"+ {item.ItemName}", Color.Khaki);
        Console.WriteLine($"[Storage] {item.ItemName} → {StorageName}");
        return true;
    }

    /// <summary>Remove an item from this container and drop it at owner's position.</summary>
    public bool TryRemove(Entity itemEntity)
    {
        if (!Contents.Contains(itemEntity)) return false;

        var item = itemEntity.GetComponent<ItemComponent>();
        Contents.Remove(itemEntity);

        if (item != null)
            item.ContainedIn = null;

        // Place at owner's position
        var ownerTf = ResolveDropAnchor()?.GetComponent<TransformComponent>();
        var itemTf = itemEntity.GetComponent<TransformComponent>();
        if (ownerTf != null && itemTf != null)
            itemTf.Position = ownerTf.Position;

        itemEntity.Active = true;
        MarkWorldDirty();

        PopupTextSystem.Show(ResolveDropAnchor() ?? Owner!, item?.ItemName ?? "Removed", Color.Silver);
        Console.WriteLine($"[Storage] Removed {item?.ItemName ?? "item"} from {StorageName}");
        return true;
    }

    /// <summary>Remove an item and put it directly into the actor's hands.</summary>
    public bool TryRemoveToHands(Entity itemEntity, HandsComponent hands)
    {
        if (!Contents.Contains(itemEntity)) return false;
        if (!hands.HasFreeHandFor(itemEntity)) return false;

        var item = itemEntity.GetComponent<ItemComponent>();
        Contents.Remove(itemEntity);

        if (item != null)
            item.ContainedIn = null;

        // Item is now free, so TryPickUp will work
        var success = hands.TryPickUp(itemEntity);
        if (success)
            MarkWorldDirty();
        return success;
    }

    public bool TryRemoveToHand(Entity itemEntity, HandsComponent hands, int handIndex)
    {
        if (!Contents.Contains(itemEntity)) return false;

        Contents.Remove(itemEntity);

        var item = itemEntity.GetComponent<ItemComponent>();
        if (item != null)
            item.ContainedIn = null;

        if (!hands.TryPickUpInto(itemEntity, handIndex))
        {
            Contents.Add(itemEntity);
            if (item != null)
                item.ContainedIn = Owner;
            return false;
        }

        MarkWorldDirty();
        return true;
    }

    // ── Interaction Source ──

    public IEnumerable<InteractionEntry> GetInteractions(InteractionContext ctx)
    {
        var hands = ctx.Actor.GetComponent<HandsComponent>();
        var isPickpocketTarget = Owner?.GetComponent<PickpocketComponent>() != null && ctx.Actor != Owner;
        var isNpcInventory = Owner?.GetComponent<NpcTagComponent>() != null && ctx.Actor != Owner;

        if (!isPickpocketTarget && !isNpcInventory)
        {
            yield return new InteractionEntry
            {
                Id = "storage.open",
                Label = $"Открыть ({StorageName})",
                Priority = 20,
                Execute = c =>
                {
                    c.World.GetSystem<MTEngine.Systems.InteractionSystem>()?.OpenStorage(c.Actor, Owner!);
                }
            };
        }

        // "Store [item]" — if player is holding something and it fits
        if (!isPickpocketTarget && !isNpcInventory && hands?.ActiveItem != null)
        {
            var heldItem = hands.ActiveItem;
            var itemComp = heldItem.GetComponent<ItemComponent>();

            if (itemComp != null && CanInsert(heldItem))
            {
                var itemName = itemComp.ItemName;
                yield return new InteractionEntry
                {
                    Id = "storage.insert",
                    Label = $"Положить ({itemName})",
                    Priority = 5,
                    Execute = c =>
                    {
                        var h = c.Actor.GetComponent<HandsComponent>();
                        if (h?.ActiveItem == null) return;

                        var held = h.ActiveItem;
                        var hand = h.ActiveHand!;

                        // Remove from hand first
                        hand.HeldItem = null;
                        var it = held.GetComponent<ItemComponent>();
                        if (it is { TwoHanded: true })
                            foreach (var hh in h.Hands)
                                hh.BlockedByTwoHanded = false;
                        if (it != null) it.ContainedIn = null;

                        // Insert into storage
                        TryInsert(held);
                    }
                };
            }
        }

    }

    public void InitializeFromPrototype(EntityPrototype proto, AssetManager assets)
    {
        EnsureSpawnContentsInitialized();
    }

    public void EnsureSpawnContentsInitialized()
    {
        if (SpawnContentsInitialized || SpawnContents.Count == 0 || Owner == null || !ServiceLocator.Has<EntityFactory>() || !ServiceLocator.Has<PrototypeManager>())
            return;

        var factory = ServiceLocator.Get<EntityFactory>();
        var prototypes = ServiceLocator.Get<PrototypeManager>();
        var spawnPosition = Owner.GetComponent<TransformComponent>()?.Position ?? Vector2.Zero;
        var spawnedAny = false;

        foreach (var prototypeId in SpawnContents)
        {
            var itemProto = prototypes.GetEntity(prototypeId);
            if (itemProto == null)
                continue;

            var entity = factory.CreateFromPrototype(itemProto, spawnPosition);
            if (entity != null && TryInsertInitial(entity))
                spawnedAny = true;
        }

        SpawnContentsInitialized = true;
        if (spawnedAny)
            InitialContentsResolved = true;
    }

    public bool TryRestoreSpawnContentsIfMissing(bool ignoreResolved = false)
    {
        if ((!ignoreResolved && InitialContentsResolved) || Contents.Count > 0 || SpawnContents.Count == 0 || Owner == null)
            return false;

        SpawnContentsInitialized = false;
        EnsureSpawnContentsInitialized();
        return Contents.Count > 0;
    }

    public void MarkInitialContentsResolved() => InitialContentsResolved = true;

    public bool TryInsertInitial(Entity itemEntity)
    {
        var item = itemEntity.GetComponent<ItemComponent>();
        if (item == null || Owner == null || itemEntity == Owner)
            return false;

        if (Contents.Contains(itemEntity))
            return true;

        InsertCore(itemEntity, item);
        return true;
    }

    private Entity? ResolveDropAnchor()
    {
        Entity? current = Owner;

        while (current != null)
        {
            var item = current.GetComponent<ItemComponent>();
            if (item?.ContainedIn == null)
                return current;

            current = item.ContainedIn;
        }

        return Owner;
    }

    private static void MarkWorldDirty()
    {
        if (ServiceLocator.Has<IWorldStateTracker>())
            ServiceLocator.Get<IWorldStateTracker>().MarkDirty();
    }

    private void InsertCore(Entity itemEntity, ItemComponent item)
    {
        RemoveFromPreviousContainer(itemEntity, item);
        Contents.Add(itemEntity);
        item.ContainedIn = Owner;
        itemEntity.Active = false;
    }

    private static void RemoveFromPreviousContainer(Entity itemEntity, ItemComponent item)
    {
        if (item.ContainedIn == null)
            return;

        var prevStorage = item.ContainedIn.GetComponent<StorageComponent>();
        prevStorage?.Contents.Remove(itemEntity);

        var prevHands = item.ContainedIn.GetComponent<HandsComponent>();
        if (prevHands == null)
            return;

        var hand = prevHands.GetHandWith(itemEntity);
        if (hand == null)
            return;

        hand.HeldItem = null;
        if (item.TwoHanded)
        {
            foreach (var h in prevHands.Hands)
                h.BlockedByTwoHanded = false;
        }
    }
}
