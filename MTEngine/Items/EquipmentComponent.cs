using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Interactions;
using MTEngine.Systems;

namespace MTEngine.Items;

[RegisterComponent("equipment")]
public class EquipmentComponent : Component, IInteractionSource
{
    [DataField("slots")]
    public string SlotIds { get; set; } = "torso,pants,shoes,back";

    [DataField("names")]
    public string SlotNames { get; set; } = "Torso,Pants,Shoes,Back";

    private List<EquipmentSlot>? _slots;

    public IReadOnlyList<EquipmentSlot> Slots
    {
        get
        {
            if (_slots == null)
                InitializeSlots();
            return _slots!;
        }
    }

    public EquipmentSlot? GetSlot(string slotId)
    {
        if (string.IsNullOrWhiteSpace(slotId))
            return null;

        return Slots.FirstOrDefault(slot => string.Equals(slot.Id, NormalizeSlotId(slotId), StringComparison.OrdinalIgnoreCase));
    }

    public EquipmentSlot? GetSlotFor(Entity itemEntity)
        => Slots.FirstOrDefault(slot => slot.Item == itemEntity);

    public Entity? GetEquipped(string slotId)
        => GetSlot(slotId)?.Item;

    public bool CanEquip(Entity itemEntity, string? targetSlotId = null)
    {
        var wearable = itemEntity.GetComponent<WearableComponent>();
        if (wearable == null)
            return false;

        var resolvedSlotId = NormalizeSlotId(targetSlotId ?? wearable.SlotId);
        if (string.IsNullOrWhiteSpace(resolvedSlotId))
            return false;

        var slot = GetSlot(resolvedSlotId);
        if (slot == null || slot.Item != null)
            return false;

        return string.Equals(NormalizeSlotId(wearable.SlotId), resolvedSlotId, StringComparison.OrdinalIgnoreCase);
    }

    public bool TryEquipFromHands(HandsComponent hands, string slotId)
    {
        if (hands.ActiveItem == null)
            return false;

        return TryEquipItem(hands, hands.ActiveItem, slotId);
    }

    public bool TryEquipItem(HandsComponent hands, Entity itemEntity, string? targetSlotId = null)
    {
        if (!CanEquip(itemEntity, targetSlotId))
            return false;

        if (hands.GetHandWith(itemEntity) == null)
            return false;

        var slot = GetSlot(targetSlotId ?? itemEntity.GetComponent<WearableComponent>()!.SlotId);
        var item = itemEntity.GetComponent<ItemComponent>();
        if (slot == null || item == null)
            return false;

        if (!hands.RemoveFromHand(itemEntity))
            return false;

        slot.Item = itemEntity;
        item.ContainedIn = Owner;
        itemEntity.Active = false;
        MarkWorldDirty();

        PopupTextSystem.Show(Owner!, $"+ {item.ItemName}", Color.LightGreen);
        Console.WriteLine($"[Equipment] {Owner?.Name} equipped {item.ItemName} in {slot.DisplayName}");
        return true;
    }

    public bool TryEquipOrSwapFromHands(HandsComponent hands, Entity itemEntity, string? targetSlotId = null)
    {
        var wearable = itemEntity.GetComponent<WearableComponent>();
        var item = itemEntity.GetComponent<ItemComponent>();
        var sourceHand = hands.GetHandWith(itemEntity);
        if (wearable == null || item == null || sourceHand == null)
            return false;

        var resolvedSlotId = NormalizeSlotId(targetSlotId ?? wearable.SlotId);
        var slot = GetSlot(resolvedSlotId);
        if (slot == null)
            return false;

        if (!string.Equals(NormalizeSlotId(wearable.SlotId), resolvedSlotId, StringComparison.OrdinalIgnoreCase))
            return false;

        if (slot.Item == null)
            return TryEquipItem(hands, itemEntity, resolvedSlotId);

        var equippedEntity = slot.Item;
        if (equippedEntity == itemEntity)
            return false;

        var equippedItem = equippedEntity.GetComponent<ItemComponent>();
        if (equippedItem == null || equippedItem.TwoHanded)
            return false;

        if (!hands.RemoveFromHand(itemEntity))
            return false;

        slot.Item = itemEntity;
        item.ContainedIn = Owner;
        itemEntity.Active = false;

        sourceHand.HeldItem = equippedEntity;
        equippedItem.ContainedIn = Owner;
        equippedEntity.Active = false;

        MarkWorldDirty();
        PopupTextSystem.Show(Owner!, $"+ {item.ItemName}", Color.LightGreen);
        Console.WriteLine($"[Equipment] {Owner?.Name} swapped {equippedItem.ItemName} for {item.ItemName} in {slot.DisplayName}");
        return true;
    }

    public bool TryUnequipToHands(string slotId, HandsComponent hands, int? handIndex = null)
    {
        var slot = GetSlot(slotId);
        if (slot?.Item == null)
            return false;

        var itemEntity = slot.Item;
        var item = itemEntity.GetComponent<ItemComponent>();
        if (item == null)
            return false;

        slot.Item = null;
        item.ContainedIn = null;

        var success = handIndex.HasValue
            ? hands.TryPickUpInto(itemEntity, handIndex.Value)
            : hands.TryPickUp(itemEntity);

        if (!success)
        {
            slot.Item = itemEntity;
            item.ContainedIn = Owner;
            itemEntity.Active = false;
            return false;
        }

        MarkWorldDirty();
        PopupTextSystem.Show(Owner!, $"- {item.ItemName}", Color.LightSkyBlue);
        Console.WriteLine($"[Equipment] {Owner?.Name} unequipped {item.ItemName} from {slot.DisplayName}");
        return true;
    }

    public bool TryUnequipOrDrop(string slotId, HandsComponent hands, int? preferredHandIndex = null)
    {
        if (TryUnequipToHands(slotId, hands, preferredHandIndex))
            return true;

        var slot = GetSlot(slotId);
        if (slot?.Item == null)
            return false;

        var itemEntity = slot.Item;
        var item = itemEntity.GetComponent<ItemComponent>();
        slot.Item = null;

        if (item != null)
            item.ContainedIn = null;

        var ownerTf = Owner?.GetComponent<TransformComponent>();
        var itemTf = itemEntity.GetComponent<TransformComponent>();
        if (ownerTf != null && itemTf != null)
            itemTf.Position = ownerTf.Position;

        itemEntity.Active = true;
        MarkWorldDirty();
        PopupTextSystem.Show(Owner!, item?.ItemName ?? "Dropped", Color.Silver);
        return true;
    }

    public bool RemoveEquipped(Entity itemEntity)
    {
        var slot = GetSlotFor(itemEntity);
        if (slot == null)
            return false;

        slot.Item = null;
        var item = itemEntity.GetComponent<ItemComponent>();
        if (item != null)
            item.ContainedIn = null;

        MarkWorldDirty();
        return true;
    }

    public float GetMoveSpeedMultiplier()
    {
        var multiplier = 1f;

        foreach (var slot in Slots)
        {
            var wearable = slot.Item?.GetComponent<WearableComponent>();
            if (wearable == null)
                continue;

            multiplier *= wearable.MoveSpeedMultiplier;
        }

        return Math.Max(0.1f, multiplier);
    }

    public IEnumerable<InteractionEntry> GetInteractions(InteractionContext ctx)
    {
        yield break;
    }

    private void InitializeSlots()
    {
        var ids = SplitCsv(SlotIds, "torso", "pants", "shoes", "back");
        var names = SplitCsv(SlotNames, "Torso", "Pants", "Shoes", "Back");

        _slots = new List<EquipmentSlot>(ids.Length);
        for (int i = 0; i < ids.Length; i++)
        {
            var id = NormalizeSlotId(ids[i]);
            if (string.IsNullOrWhiteSpace(id))
                continue;

            var displayName = i < names.Length && !string.IsNullOrWhiteSpace(names[i])
                ? names[i]
                : HumanizeSlotName(id);

            _slots.Add(new EquipmentSlot(id, displayName));
        }
    }

    private static string[] SplitCsv(string value, params string[] fallback)
    {
        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts : fallback;
    }

    public static string NormalizeSlotId(string? slotId)
        => (slotId ?? string.Empty).Trim().ToLowerInvariant();

    private static string HumanizeSlotName(string slotId)
        => string.IsNullOrWhiteSpace(slotId)
            ? "Slot"
            : char.ToUpperInvariant(slotId[0]) + slotId[1..];

    private static void MarkWorldDirty()
    {
        if (ServiceLocator.Has<IWorldStateTracker>())
            ServiceLocator.Get<IWorldStateTracker>().MarkDirty();
    }
}
