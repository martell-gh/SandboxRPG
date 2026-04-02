using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Interactions;
using MTEngine.Items;
using MTEngine.Rendering;

namespace MTEngine.Systems;

public class InteractionSystem : GameSystem
{
    public override DrawLayer DrawLayer => DrawLayer.Overlay;
    private const float DefaultInteractRange = 64f;
    private const int HandsHudPadding = 8;
    private const int HandSlotSize = 44;
    private const int HandSlotGap = 10;
    private const int EquipmentSlotSize = 34;
    private const int EquipmentSlotGap = 8;
    private const int EquipmentSideGap = 12;
    private const int StorageWindowWidth = 360;
    private const int StorageHeaderHeight = 28;
    private const int StorageRowHeight = 22;
    private const int StoragePadding = 8;
    private const int ItemIconSize = 16;
    private const int StorageCloseButtonSize = 18;
    private static readonly string UiTextureRoot = Path.Combine("SandboxGame", "Content", "Textures", "UI");

    private InputManager? _input;
    private Camera? _camera;
    private SpriteBatch? _sb;
    private SpriteFont? _font;
    private Texture2D? _pixel;
    private GraphicsDevice? _gd;
    private Texture2D? _handSlotTexture;
    private Texture2D? _handSlotBlockedTexture;
    private readonly Dictionary<string, Texture2D?> _equipmentSlotTextures = new();

    // Menu state
    private bool _menuOpen;
    private Vector2 _menuScreenPos;
    private Entity? _targetEntity;
    private InteractionContext? _activeContext;
    private List<InteractionEntry> _menuActions = new();
    private int _hoveredIndex = -1;
    private Entity? _openStorageEntity;
    private Entity? _storageActor;
    private Entity? _draggedItem;
    private int? _draggedFromHandIndex;
    private string? _draggedFromEquipmentSlotId;
    private bool _draggedFromStorage;
    private Point? _storageWindowPosition;
    private bool _draggingStorageWindow;
    private Point _storageDragOffset;

    private const int MenuWidth = 200;
    private const int HeaderHeight = 26;
    private const int ItemHeight = 24;
    private const int MenuPadding = 4;

    private sealed class StorageRowInfo
    {
        public required Rectangle Rect { get; init; }
        public required Entity Item { get; init; }
        public required bool IsStoredItem { get; init; }
    }

    private sealed class HandSlotInfo
    {
        public required Rectangle Rect { get; init; }
        public required int HandIndex { get; init; }
        public required Hand Hand { get; init; }
    }

    private sealed class EquipmentSlotInfo
    {
        public required Rectangle Rect { get; init; }
        public required EquipmentSlot Slot { get; init; }
    }

    public void SetFont(SpriteFont font) => _font = font;

    public override void OnInitialize()
    {
        _input = ServiceLocator.Get<InputManager>();
        _camera = ServiceLocator.Get<Camera>();
        _gd = ServiceLocator.Get<GraphicsDevice>();
    }

    private void EnsureResources()
    {
        _sb ??= ServiceLocator.Get<SpriteBatch>();
        if (_pixel == null && _gd != null)
        {
            _pixel = new Texture2D(_gd, 1, 1);
            _pixel.SetData(new[] { Color.White });
        }

        if (_handSlotTexture == null && ServiceLocator.Has<AssetManager>())
        {
            var assets = ServiceLocator.Get<AssetManager>();
            _handSlotTexture = assets.LoadFromFile(Path.Combine(UiTextureRoot, "hand_slot.png"));
            _handSlotBlockedTexture = assets.LoadFromFile(Path.Combine(UiTextureRoot, "hand_slot_blocked.png"));
            _equipmentSlotTextures["torso"] = assets.LoadFromFile(Path.Combine(UiTextureRoot, "slot_torso.png"));
            _equipmentSlotTextures["pants"] = assets.LoadFromFile(Path.Combine(UiTextureRoot, "slot_pants.png"));
            _equipmentSlotTextures["shoes"] = assets.LoadFromFile(Path.Combine(UiTextureRoot, "slot_shoes.png"));
            _equipmentSlotTextures["back"] = assets.LoadFromFile(Path.Combine(UiTextureRoot, "slot_back.png"));
        }
    }

    public override void Update(float deltaTime)
    {
        if (_input == null || _camera == null) return;

        if (DevConsole.IsOpen) { CloseMenu(); CloseStorage(); return; }

        var player = GetPrimaryActor();
        var hands = player?.GetComponent<HandsComponent>();
        var equipment = player?.GetComponent<EquipmentComponent>();
        var mousePos = new Vector2(_input.MousePosition.X, _input.MousePosition.Y);
        var worldPos = _camera.ScreenToWorld(mousePos);

        if (Keyboard.GetState().IsKeyDown(Keys.Escape) && _menuOpen)
        {
            CloseMenu();
            return;
        }

        if (Keyboard.GetState().IsKeyDown(Keys.Escape) && _openStorageEntity != null)
        {
            CloseStorage();
            return;
        }

        if (_openStorageEntity != null && ShouldCloseStorageByDistance())
        {
            CloseStorage();
            return;
        }

        if (!_menuOpen && hands != null)
        {
            if (_input.IsPressed(Keys.Tab))
            {
                hands.SwapActiveHand();
                return;
            }

            if (_input.IsPressed(Keys.Q))
            {
                hands.TryDropActive();
                return;
            }

            if (_input.IsPressed(Keys.E))
            {
                TryUseActiveItem(player!, hands);
                return;
            }
        }

        if (_draggedItem == null && _input.LeftClicked)
        {
            if (TryBeginDrag(mousePos, hands, equipment))
                return;
        }

        if (_draggedItem != null && _input.LeftReleased)
        {
            CompleteDrag(mousePos, hands, equipment);
            return;
        }

        if (equipment != null)
        {
            if (_input.LeftClicked && TryHandleEquipmentLeftClick(mousePos, hands, equipment))
                return;

            if (_input.RightClicked && TryHandleEquipmentRightClick(mousePos, player!, equipment))
                return;
        }

        if (_openStorageEntity != null)
        {
            HandleStorageInput(mousePos);
            return;
        }

        if (_menuOpen)
        {
            if (_input.LeftClicked)
            {
                var menuRect = GetMenuRect();
                if (!menuRect.Contains((int)mousePos.X, (int)mousePos.Y))
                    CloseMenu();
                else
                    HandleMenuClick(mousePos);
                return;
            }
            UpdateHover(mousePos);
            return;
        }

        if (_input.RightClicked)
            TryOpenMenu(worldPos, mousePos);
    }

    /// <summary>
    /// Collects all InteractionEntry from every IInteractionSource component on the entity.
    /// </summary>
    private List<InteractionEntry> CollectActions(InteractionContext ctx)
    {
        var entries = new List<InteractionEntry>();

        foreach (var component in ctx.Target.GetAllComponents())
        {
            if (component is IInteractionSource source)
            {
                foreach (var entry in source.GetInteractions(ctx))
                    entries.Add(entry);
            }
        }

        // Sort by priority descending (higher priority = closer to top)
        entries.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        return entries;
    }

    private void TryOpenMenu(Vector2 worldPos, Vector2 screenPos)
    {
        var player = GetPrimaryActor();
        if (player == null) return;

        var playerTf = player.GetComponent<TransformComponent>();
        if (playerTf == null) return;

        Entity? best = null;
        float bestMouseDist = float.MaxValue;

        foreach (var entity in World.GetEntitiesWith<TransformComponent>())
        {
            if (entity == player) continue;
            if (!CanEntityBeInteractedWith(entity)) continue;

            var tf = entity.GetComponent<TransformComponent>()!;

            float pDist = Vector2.Distance(playerTf.Position, tf.Position);
            if (pDist > GetInteractRange(entity)) continue;

            float mDist = Vector2.Distance(worldPos, tf.Position);
            if (mDist < bestMouseDist)
            {
                bestMouseDist = mDist;
                best = entity;
            }
        }

        if (best == null) return;

        var ctx = new InteractionContext
        {
            Actor = player,
            Target = best,
            World = World
        };

        var actions = CollectActions(ctx);
        if (actions.Count == 0) return;

        _targetEntity = best;
        _activeContext = ctx;
        _menuActions = actions;
        _menuScreenPos = screenPos;
        _menuOpen = true;
        _hoveredIndex = -1;

        var name = GetInteractName(best);
        Console.WriteLine($"[Interaction] Opened: {name} ({actions.Count} actions)");
    }

    private void TryUseActiveItem(Entity actor, HandsComponent hands)
    {
        var activeItem = hands.ActiveItem;
        if (activeItem == null || _gd == null)
            return;

        if (activeItem.HasComponent<StorageComponent>())
        {
            OpenStorage(actor, activeItem);
            return;
        }

        var ctx = new InteractionContext
        {
            Actor = actor,
            Target = activeItem,
            World = World
        };

        var actions = CollectActions(ctx);
        if (actions.Count == 0)
            return;

        _targetEntity = activeItem;
        _activeContext = ctx;
        _menuActions = actions;
        _menuScreenPos = new Vector2(_gd.Viewport.Width - MenuWidth - 16, _gd.Viewport.Height - 160);
        _menuOpen = true;
        _hoveredIndex = -1;
    }

    private Entity? GetPrimaryActor()
    {
        foreach (var entity in World.GetEntitiesWith<PlayerTagComponent>())
            return entity;

        return null;
    }

    private static bool CanEntityBeInteractedWith(Entity entity)
    {
        if (entity.HasComponent<InteractableComponent>())
            return true;

        return entity.GetAllComponents().Any(component => component is IInteractionSource);
    }

    private static float GetInteractRange(Entity entity)
        => entity.GetComponent<InteractableComponent>()?.InteractRange ?? DefaultInteractRange;

    private static string GetInteractName(Entity entity)
    {
        var interactable = entity.GetComponent<InteractableComponent>();
        if (!string.IsNullOrWhiteSpace(interactable?.DisplayName))
            return interactable.DisplayName;

        var item = entity.GetComponent<ItemComponent>();
        if (!string.IsNullOrWhiteSpace(item?.ItemName))
            return item.ItemName;

        var storage = entity.GetComponent<StorageComponent>();
        if (!string.IsNullOrWhiteSpace(storage?.StorageName))
            return storage.StorageName;

        return entity.Name;
    }

    public void OpenStorage(Entity actor, Entity storageEntity)
    {
        if (!storageEntity.HasComponent<StorageComponent>())
            return;

        _storageActor = actor;
        _openStorageEntity = storageEntity;
        if (_storageWindowPosition == null && _gd != null)
            _storageWindowPosition = new Point(16, 48);
        CloseMenu();
    }

    public void CloseStorage()
    {
        _openStorageEntity = null;
        _storageActor = null;
        _draggingStorageWindow = false;
        CancelDrag();
    }

    private void HandleMenuClick(Vector2 mousePos)
    {
        for (int i = 0; i < _menuActions.Count; i++)
        {
            if (GetItemRect(i).Contains((int)mousePos.X, (int)mousePos.Y))
            {
                ExecuteAction(i);
                return;
            }
        }
    }

    private void ExecuteAction(int index)
    {
        if (_activeContext == null || index < 0 || index >= _menuActions.Count) return;

        var action = _menuActions[index];
        Console.WriteLine($"[Interaction] Execute: {action.Label}");
        action.Execute?.Invoke(_activeContext);

        CloseMenu();
    }

    private void CloseMenu()
    {
        _menuOpen = false;
        _targetEntity = null;
        _activeContext = null;
        _menuActions.Clear();
        _hoveredIndex = -1;
    }

    private void HandleStorageInput(Vector2 mousePos)
    {
        if (_openStorageEntity == null || _storageActor == null || _input == null)
            return;

        if (_draggedItem != null)
            return;

        var rect = GetStorageRect();
        var closeRect = GetStorageCloseRect();
        var headerRect = GetStorageHeaderRect();

        if (_draggingStorageWindow)
        {
            if (_input.LeftDown)
            {
                MoveStorageWindow(mousePos);
                return;
            }

            _draggingStorageWindow = false;
        }

        if (_input.LeftClicked)
        {
            if (closeRect.Contains((int)mousePos.X, (int)mousePos.Y))
            {
                CloseStorage();
                return;
            }

            if (headerRect.Contains((int)mousePos.X, (int)mousePos.Y))
            {
                _draggingStorageWindow = true;
                _storageDragOffset = new Point((int)mousePos.X - rect.X, (int)mousePos.Y - rect.Y);
                return;
            }

            if (!rect.Contains((int)mousePos.X, (int)mousePos.Y))
            {
                CloseStorage();
                return;
            }

            HandleStorageClick(mousePos, false);
            return;
        }

        if (_input.RightClicked)
        {
            if (rect.Contains((int)mousePos.X, (int)mousePos.Y))
                HandleStorageClick(mousePos, true);
            else
                CloseStorage();
        }
    }

    private void HandleStorageClick(Vector2 mousePos, bool rightClick)
    {
        if (_openStorageEntity == null || _storageActor == null)
            return;

        var storage = _openStorageEntity.GetComponent<StorageComponent>();
        var hands = _storageActor.GetComponent<HandsComponent>();
        if (storage == null) return;

        foreach (var row in BuildStorageRows(storage, hands))
        {
            if (!row.Rect.Contains((int)mousePos.X, (int)mousePos.Y))
                continue;

            if (row.IsStoredItem)
            {
                if (rightClick)
                    storage.TryRemove(row.Item);
                else if (hands != null)
                    storage.TryRemoveToHands(row.Item, hands);
            }
            else if (!rightClick)
            {
                storage.TryInsert(row.Item);
            }

            return;
        }
    }

    private void UpdateHover(Vector2 mousePos)
    {
        _hoveredIndex = -1;
        for (int i = 0; i < _menuActions.Count; i++)
            if (GetItemRect(i).Contains((int)mousePos.X, (int)mousePos.Y))
            { _hoveredIndex = i; break; }
    }

    public override void Draw()
    {
        if (_font == null) return;
        EnsureResources();
        if (_sb == null || _pixel == null) return;

        var actor = GetPrimaryActor();
        var hands = actor?.GetComponent<HandsComponent>();
        var equipment = actor?.GetComponent<EquipmentComponent>();
        if (!_menuOpen && hands == null && equipment == null && _openStorageEntity == null) return;

        _sb.Begin();

        if (_menuOpen)
        {
            DrawInteractionMenu();
        }

        if (_openStorageEntity != null)
            DrawStorageWindow();

        if (hands != null)
            DrawActiveHandCursorIcon(hands);

        if (hands != null || equipment != null)
            DrawEquipmentBar(hands, equipment);

        if (_draggedItem != null && _input != null)
            DrawDraggedItem(new Vector2(_input.MousePosition.X, _input.MousePosition.Y));

        _sb.End();
    }

    private void DrawInteractionMenu()
    {
        var targetName = _targetEntity != null ? GetInteractName(_targetEntity) : "Object";
        var menuRect = GetMenuRect();

        _sb!.Draw(_pixel!, new Rectangle(menuRect.X + 3, menuRect.Y + 3, menuRect.Width, menuRect.Height),
            Color.Black * 0.4f);
        _sb.Draw(_pixel, menuRect, new Color(18, 22, 28));

        var hdr = new Rectangle(menuRect.X, menuRect.Y, menuRect.Width, HeaderHeight);
        _sb.Draw(_pixel, hdr, new Color(35, 55, 35));
        _sb.Draw(_pixel, new Rectangle(menuRect.X, menuRect.Y + HeaderHeight, menuRect.Width, 1),
            new Color(70, 110, 70));

        _sb.DrawString(_font!, targetName,
            new Vector2(menuRect.X + 8, menuRect.Y + (HeaderHeight - 14) / 2),
            Color.LimeGreen);

        for (int i = 0; i < _menuActions.Count; i++)
        {
            var itemRect = GetItemRect(i);
            bool hovered = i == _hoveredIndex;

            if (hovered)
                _sb.Draw(_pixel, itemRect, new Color(55, 85, 55));

            _sb.DrawString(_font!, _menuActions[i].Label,
                new Vector2(itemRect.X + 10, itemRect.Y + (ItemHeight - 14) / 2),
                hovered ? Color.White : new Color(200, 200, 200));

            if (i < _menuActions.Count - 1)
                _sb.Draw(_pixel, new Rectangle(itemRect.X + 6, itemRect.Bottom, itemRect.Width - 12, 1),
                    Color.White * 0.08f);
        }

        _sb.Draw(_pixel, new Rectangle(menuRect.X, menuRect.Y, menuRect.Width, 1), new Color(70, 110, 70));
        _sb.Draw(_pixel, new Rectangle(menuRect.X, menuRect.Bottom, menuRect.Width, 1), new Color(70, 110, 70));
        _sb.Draw(_pixel, new Rectangle(menuRect.X, menuRect.Y, 1, menuRect.Height + 1), new Color(70, 110, 70));
        _sb.Draw(_pixel, new Rectangle(menuRect.Right, menuRect.Y, 1, menuRect.Height + 1), new Color(70, 110, 70));
    }

    private void DrawEquipmentBar(HandsComponent? hands, EquipmentComponent? equipment)
    {
        if (_gd == null)
            return;

        if (hands != null)
        {
            var twoHandedVisual = GetTwoHandedVisualItem(hands);
            foreach (var slot in BuildHandSlots(hands))
            {
                var hand = slot.Hand;
                var isActive = slot.HandIndex == hands.ActiveHandIndex;
                var slotTexture = hand.BlockedByTwoHanded ? _handSlotBlockedTexture : _handSlotTexture;
                var tint = hand.BlockedByTwoHanded
                    ? Color.White * 0.95f
                    : isActive ? new Color(180, 255, 180) : Color.White;

                DrawSlotFrame(slot.Rect, slotTexture, tint, hand.BlockedByTwoHanded ? new Color(64, 64, 64, 180) : new Color(255, 255, 255, 24));

                if (hand.HeldItem != null)
                    DrawEntityIcon(hand.HeldItem, Inset(slot.Rect, 8), hand.BlockedByTwoHanded ? 0.45f : 1f);
                else if (hand.BlockedByTwoHanded && twoHandedVisual != null)
                    DrawEntityIcon(twoHandedVisual, Inset(slot.Rect, 8), 0.35f);
            }
        }

        if (equipment != null)
        {
            foreach (var slotInfo in BuildEquipmentSlots(equipment))
            {
                var canEquip = hands?.ActiveItem != null && equipment.CanEquip(hands.ActiveItem, slotInfo.Slot.Id);
                _equipmentSlotTextures.TryGetValue(slotInfo.Slot.Id, out var slotTexture);
                var tint = canEquip ? new Color(180, 255, 180) : Color.White;

                DrawSlotFrame(slotInfo.Rect, slotTexture, tint, new Color(160, 160, 160, 45));

                if (slotInfo.Slot.Item != null)
                    DrawEntityIcon(slotInfo.Slot.Item, Inset(slotInfo.Rect, 5));
            }
        }
    }

    private void DrawActiveHandCursorIcon(HandsComponent hands)
    {
        if (_input == null || hands.ActiveItem == null || _draggedItem != null)
            return;

        var mouse = _input.MousePosition;
        var rect = new Rectangle(mouse.X + 14, mouse.Y - 6, 14, 14);
        DrawEntityIcon(hands.ActiveItem, rect, 0.45f);
    }

    private void DrawStorageWindow()
    {
        if (_openStorageEntity == null || _font == null) return;

        var storage = _openStorageEntity.GetComponent<StorageComponent>();
        if (storage == null) return;

        var hands = _storageActor?.GetComponent<HandsComponent>();
        var rect = GetStorageRect();
        var title = GetInteractName(_openStorageEntity);

        _sb!.Draw(_pixel!, rect, Color.Black * 0.86f);
        _sb.Draw(_pixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), new Color(70, 110, 70));
        _sb.Draw(_pixel, new Rectangle(rect.X, rect.Y, rect.Width, StorageHeaderHeight), new Color(35, 55, 35));
        _sb.DrawString(_font, $"{title} [{storage.UsedSlots}/{storage.MaxSlots} slots]",
            new Vector2(rect.X + 8, rect.Y + 6), Color.LimeGreen);
        DrawStorageCloseButton();

        var rows = BuildStorageRows(storage, hands);
        var y = rect.Y + StorageHeaderHeight + 4;

        var storeables = GetStoreableHandItems(storage, hands).ToList();
        if (storeables.Count > 0)
        {
            _sb.DrawString(_font, "Store from hands:", new Vector2(rect.X + StoragePadding, y), Color.Cyan);
            y += 18;

            foreach (var entity in storeables)
            {
                var row = rows.First(r => !r.IsStoredItem && r.Item == entity);
                DrawStorageRow(row, entity.GetComponent<ItemComponent>()!.ItemName, storage.GetSlotSize(entity), false);
                y += StorageRowHeight;
            }

            y += 4;
        }

        _sb.DrawString(_font, "Contents:", new Vector2(rect.X + StoragePadding, y), Color.Cyan);
        y += 18;

        foreach (var entity in storage.Contents)
        {
            var item = entity.GetComponent<ItemComponent>();
            if (item == null) continue;
            var row = rows.First(r => r.IsStoredItem && r.Item == entity);
            DrawStorageRow(row, item.ItemName, storage.GetSlotSize(entity), true);
            y += StorageRowHeight;
        }

        if (storage.Contents.Count == 0)
        {
            _sb.DrawString(_font, "[empty]", new Vector2(rect.X + StoragePadding, y), Color.Gray);
        }

        _sb.DrawString(_font, "LMB: hand / store   RMB: drop from storage",
            new Vector2(rect.X + StoragePadding, rect.Bottom - 20), Color.Gray);
    }

    private void DrawStorageRow(StorageRowInfo row, string label, int slotSize, bool isStoredItem)
    {
        _sb!.Draw(_pixel!, row.Rect, Color.DarkSlateGray * 0.35f);
        var prefix = isStoredItem ? "[in]" : "[hold]";
        var suffix = slotSize == 1 ? "slot" : "slots";
        DrawEntityIcon(row.Item, new Rectangle(row.Rect.X + 4, row.Rect.Y + 1, ItemIconSize, ItemIconSize));
        _sb.DrawString(_font!, $"{prefix} {label} ({slotSize} {suffix})", new Vector2(row.Rect.X + 24, row.Rect.Y + 2), Color.White);
    }

    private IEnumerable<Entity> GetStoreableHandItems(StorageComponent storage, HandsComponent? hands)
    {
        if (hands == null)
            yield break;

        foreach (var hand in hands.Hands)
        {
            var item = hand.HeldItem;
            if (item == null || item == _openStorageEntity)
                continue;

            if (storage.CanInsert(item))
                yield return item;
        }
    }

    private Rectangle GetStorageRect()
    {
        var storage = _openStorageEntity?.GetComponent<StorageComponent>();
        var hands = _storageActor?.GetComponent<HandsComponent>();
        var storeableCount = storage != null ? GetStoreableHandItems(storage, hands).Count() : 0;
        var storedCount = storage?.Contents.Count ?? 0;
        var totalRows = Math.Max(1, storedCount + storeableCount);
        var extraSections = storeableCount > 0 ? 2 : 1;
        var height = StorageHeaderHeight + 34 + (totalRows * StorageRowHeight) + (extraSections * 18) + 26;
        var position = _storageWindowPosition ?? new Point(16, 48);
        return new Rectangle(position.X, position.Y, StorageWindowWidth, height);
    }

    private List<StorageRowInfo> BuildStorageRows(StorageComponent storage, HandsComponent? hands)
    {
        var rect = GetStorageRect();
        var rows = new List<StorageRowInfo>();
        var y = rect.Y + StorageHeaderHeight + 4;

        var storeables = GetStoreableHandItems(storage, hands).ToList();
        if (storeables.Count > 0)
        {
            y += 18;
            foreach (var item in storeables)
            {
                rows.Add(new StorageRowInfo
                {
                    Rect = new Rectangle(rect.X + StoragePadding, y - 2, rect.Width - StoragePadding * 2, StorageRowHeight - 2),
                    Item = item,
                    IsStoredItem = false
                });
                y += StorageRowHeight;
            }

            y += 4;
        }

        y += 18;
        foreach (var item in storage.Contents)
        {
            rows.Add(new StorageRowInfo
            {
                Rect = new Rectangle(rect.X + StoragePadding, y - 2, rect.Width - StoragePadding * 2, StorageRowHeight - 2),
                Item = item,
                IsStoredItem = true
            });
            y += StorageRowHeight;
        }

        return rows;
    }

    private bool ShouldCloseStorageByDistance()
    {
        if (_openStorageEntity == null || _storageActor == null)
            return false;

        var storageItem = _openStorageEntity.GetComponent<ItemComponent>();
        if (storageItem?.ContainedIn == _storageActor)
            return false;

        var actorTf = _storageActor.GetComponent<TransformComponent>();
        var targetTf = _openStorageEntity.GetComponent<TransformComponent>();
        if (actorTf == null || targetTf == null)
            return false;

        var maxRange = GetInteractRange(_openStorageEntity);
        return Vector2.Distance(actorTf.Position, targetTf.Position) > maxRange;
    }

    private Rectangle GetStorageHeaderRect()
    {
        var rect = GetStorageRect();
        return new Rectangle(rect.X, rect.Y, rect.Width, StorageHeaderHeight);
    }

    private Rectangle GetStorageCloseRect()
    {
        var rect = GetStorageRect();
        return new Rectangle(
            rect.Right - StorageCloseButtonSize - 6,
            rect.Y + 5,
            StorageCloseButtonSize,
            StorageCloseButtonSize
        );
    }

    private void DrawStorageCloseButton()
    {
        var rect = GetStorageCloseRect();
        _sb!.Draw(_pixel!, rect, new Color(90, 30, 30));
        _sb.DrawString(_font!, "X", new Vector2(rect.X + 5, rect.Y + 1), Color.White);
    }

    private void MoveStorageWindow(Vector2 mousePos)
    {
        if (_gd == null)
            return;

        var x = (int)mousePos.X - _storageDragOffset.X;
        var y = (int)mousePos.Y - _storageDragOffset.Y;
        var currentRect = GetStorageRect();

        x = Math.Clamp(x, 0, Math.Max(0, _gd.Viewport.Width - currentRect.Width));
        y = Math.Clamp(y, 0, Math.Max(0, _gd.Viewport.Height - currentRect.Height));
        _storageWindowPosition = new Point(x, y);
    }

    private List<HandSlotInfo> BuildHandSlots(HandsComponent hands)
    {
        if (_gd == null)
            return new List<HandSlotInfo>();

        var rect = GetHandBarRect();

        var slots = new List<HandSlotInfo>();
        for (int i = 0; i < hands.Hands.Count; i++)
        {
            slots.Add(new HandSlotInfo
            {
                Rect = new Rectangle(rect.X + i * (HandSlotSize + HandSlotGap), rect.Y, HandSlotSize, HandSlotSize),
                HandIndex = i,
                Hand = hands.Hands[i]
            });
        }

        return slots;
    }

    private List<EquipmentSlotInfo> BuildEquipmentSlots(EquipmentComponent equipment)
    {
        if (_gd == null)
            return new List<EquipmentSlotInfo>();

        var slots = new List<EquipmentSlotInfo>(equipment.Slots.Count);
        for (int i = 0; i < equipment.Slots.Count; i++)
        {
            slots.Add(new EquipmentSlotInfo
            {
                Rect = GetEquipmentSlotRect(i),
                Slot = equipment.Slots[i]
            });
        }

        return slots;
    }

    private bool TryBeginDrag(Vector2 mousePos, HandsComponent? hands, EquipmentComponent? equipment)
    {
        if (hands != null)
        {
            foreach (var slot in BuildHandSlots(hands))
            {
                if (!slot.Rect.Contains((int)mousePos.X, (int)mousePos.Y))
                    continue;

                if (slot.Hand.HeldItem != null)
                {
                    _draggedItem = slot.Hand.HeldItem;
                    _draggedFromHandIndex = slot.HandIndex;
                    _draggedFromStorage = false;
                }
                else
                {
                    hands.ActiveHandIndex = slot.HandIndex;
                }

                return true;
            }
        }

        if (equipment != null)
        {
            foreach (var slot in BuildEquipmentSlots(equipment))
            {
                if (!slot.Rect.Contains((int)mousePos.X, (int)mousePos.Y))
                    continue;

                if (slot.Slot.Item != null)
                {
                    _draggedItem = slot.Slot.Item;
                    _draggedFromEquipmentSlotId = slot.Slot.Id;
                    _draggedFromStorage = false;
                    _draggedFromHandIndex = null;
                    return true;
                }

                break;
            }
        }

        if (_openStorageEntity != null)
        {
            var storage = _openStorageEntity.GetComponent<StorageComponent>();
            if (storage == null) return false;

            foreach (var row in BuildStorageRows(storage, hands))
            {
                if (!row.Rect.Contains((int)mousePos.X, (int)mousePos.Y))
                    continue;

                if (row.IsStoredItem)
                {
                    _draggedItem = row.Item;
                    _draggedFromStorage = true;
                    _draggedFromHandIndex = null;
                    return true;
                }

                break;
            }
        }

        return false;
    }

    private void CompleteDrag(Vector2 mousePos, HandsComponent? hands, EquipmentComponent? equipment)
    {
        if (_draggedItem == null)
            return;

        var item = _draggedItem;
        var fromHandIndex = _draggedFromHandIndex;
        var fromEquipmentSlotId = _draggedFromEquipmentSlotId;
        var fromStorage = _draggedFromStorage;

        if (hands != null)
        {
            foreach (var slot in BuildHandSlots(hands))
            {
                if (!slot.Rect.Contains((int)mousePos.X, (int)mousePos.Y))
                    continue;

                if (fromStorage && _openStorageEntity?.GetComponent<StorageComponent>() is { } storageFrom &&
                    storageFrom.TryRemoveToHand(item, hands, slot.HandIndex))
                {
                    CancelDrag();
                    return;
                }

                if (fromEquipmentSlotId != null && equipment != null &&
                    equipment.TryUnequipToHands(fromEquipmentSlotId, hands, slot.HandIndex))
                {
                    CancelDrag();
                    return;
                }

                if (!fromStorage && fromHandIndex.HasValue)
                {
                    hands.TryMoveToHand(item, slot.HandIndex);
                    CancelDrag();
                    return;
                }
            }
        }

        if (equipment != null)
        {
            foreach (var slot in BuildEquipmentSlots(equipment))
            {
                if (!slot.Rect.Contains((int)mousePos.X, (int)mousePos.Y))
                    continue;

                if (!fromStorage && fromHandIndex.HasValue && hands != null &&
                    equipment.TryEquipItem(hands, item, slot.Slot.Id))
                {
                    CancelDrag();
                    return;
                }

                CancelDrag();
                break;
            }
        }

        if (_openStorageEntity?.GetComponent<StorageComponent>() is { } storage)
        {
            var storageRect = GetStorageRect();
            if (storageRect.Contains((int)mousePos.X, (int)mousePos.Y))
            {
                if (!fromStorage)
                {
                    if (fromEquipmentSlotId != null && equipment != null)
                    {
                        if (storage.CanInsert(item) && equipment.RemoveEquipped(item))
                            storage.TryInsert(item);
                        else
                        {
                            CancelDrag();
                            return;
                        }
                    }
                    else
                    {
                        storage.TryInsert(item);
                    }
                }
                else if (hands != null)
                    storage.TryRemoveToHands(item, hands);

                CancelDrag();
                return;
            }
        }

        if (fromStorage && _openStorageEntity?.GetComponent<StorageComponent>() is { } openedStorage)
        {
            openedStorage.TryRemove(item);
        }
        else if (fromEquipmentSlotId != null && equipment != null)
        {
            DropEquippedItem(equipment, fromEquipmentSlotId);
        }
        else if (!fromStorage && fromHandIndex.HasValue && hands != null)
        {
            hands.TryDrop(hands.Hands[fromHandIndex.Value]);
        }

        CancelDrag();
    }

    private void CancelDrag()
    {
        _draggedItem = null;
        _draggedFromHandIndex = null;
        _draggedFromEquipmentSlotId = null;
        _draggedFromStorage = false;
    }

    private void DrawDraggedItem(Vector2 mousePos)
    {
        if (_draggedItem == null) return;

        var rect = new Rectangle((int)mousePos.X + 12, (int)mousePos.Y + 12, 160, 20);
        _sb!.Draw(_pixel!, rect, Color.Black * 0.8f);
        DrawEntityIcon(_draggedItem, new Rectangle(rect.X + 4, rect.Y + 2, ItemIconSize, ItemIconSize));
        var name = _draggedItem.GetComponent<ItemComponent>()?.ItemName ?? GetInteractName(_draggedItem);
        _sb.DrawString(_font!, name, new Vector2(rect.X + 24, rect.Y + 2), Color.White);
    }

    private void DrawEntityIcon(Entity entity, Rectangle rect, float alpha = 1f)
    {
        var wearable = entity.GetComponent<WearableComponent>();
        if (wearable?.IconTexture != null)
        {
            _sb!.Draw(wearable.IconTexture, rect, wearable.IconSourceRect, wearable.GetRenderColor(entity) * alpha);
            return;
        }

        var sprite = entity.GetComponent<SpriteComponent>();
        if (sprite?.Texture != null)
        {
            _sb!.Draw(sprite.Texture, rect, sprite.SourceRect, sprite.Color * alpha);
            return;
        }

        _sb!.Draw(_pixel!, rect, Color.Gray * alpha);
    }

    private void DrawSlotFrame(Rectangle rect, Texture2D? texture, Color tint, Color fallbackFill)
    {
        if (texture != null)
        {
            _sb!.Draw(texture, rect, tint);
            return;
        }

        _sb!.Draw(_pixel!, rect, fallbackFill);
        _sb.Draw(_pixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), tint);
        _sb.Draw(_pixel, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), tint);
        _sb.Draw(_pixel, new Rectangle(rect.X, rect.Y, 1, rect.Height), tint);
        _sb.Draw(_pixel, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), tint);
    }

    private Rectangle GetHandBarRect()
    {
        var totalWidth = HandSlotSize * 2 + HandSlotGap;
        return new Rectangle((_gd!.Viewport.Width - totalWidth) / 2, _gd.Viewport.Height - HandSlotSize - 18, totalWidth, HandSlotSize);
    }

    private Rectangle GetEquipmentGridRect()
    {
        var handsRect = GetHandBarRect();
        var leftWidth = EquipmentSlotSize * 2 + EquipmentSlotGap;
        var rightWidth = EquipmentSlotSize * 2 + EquipmentSlotGap;
        var leftX = handsRect.X - EquipmentSideGap - leftWidth;
        var rightX = handsRect.Right + EquipmentSideGap;
        var y = handsRect.Center.Y - EquipmentSlotSize / 2;
        return new Rectangle(leftX, y, (rightX + rightWidth) - leftX, EquipmentSlotSize);
    }

    private Rectangle GetEquipmentSlotRect(int index)
    {
        var rect = GetEquipmentGridRect();
        var isLeftSide = index < 2;
        var sideIndex = index % 2;
        var sideWidth = EquipmentSlotSize * 2 + EquipmentSlotGap;
        var x = isLeftSide
            ? rect.X + sideIndex * (EquipmentSlotSize + EquipmentSlotGap)
            : rect.Right - sideWidth + sideIndex * (EquipmentSlotSize + EquipmentSlotGap);
        var y = rect.Y;
        return new Rectangle(x, y, EquipmentSlotSize, EquipmentSlotSize);
    }

    private static Rectangle Inset(Rectangle rect, int amount)
        => new Rectangle(rect.X + amount, rect.Y + amount, Math.Max(1, rect.Width - amount * 2), Math.Max(1, rect.Height - amount * 2));

    private static Entity? GetTwoHandedVisualItem(HandsComponent hands)
    {
        foreach (var hand in hands.Hands)
        {
            var item = hand.HeldItem;
            if (item?.GetComponent<ItemComponent>()?.TwoHanded == true)
                return item;
        }

        return null;
    }

    private Rectangle GetMenuRect()
    {
        int totalH = HeaderHeight + _menuActions.Count * ItemHeight + MenuPadding;
        int x = (int)_menuScreenPos.X;
        int y = (int)_menuScreenPos.Y;

        if (_gd != null)
        {
            if (x + MenuWidth > _gd.Viewport.Width) x = _gd.Viewport.Width - MenuWidth - 4;
            if (y + totalH > _gd.Viewport.Height) y = _gd.Viewport.Height - totalH - 4;
            x = Math.Max(0, x);
            y = Math.Max(0, y);
        }

        return new Rectangle(x, y, MenuWidth, totalH);
    }

    private Rectangle GetItemRect(int i)
    {
        var mr = GetMenuRect();
        return new Rectangle(mr.X + 1, mr.Y + HeaderHeight + i * ItemHeight, mr.Width - 2, ItemHeight);
    }

    private bool TryHandleEquipmentLeftClick(Vector2 mousePos, HandsComponent? hands, EquipmentComponent equipment)
    {
        foreach (var slot in BuildEquipmentSlots(equipment))
        {
            if (!slot.Rect.Contains((int)mousePos.X, (int)mousePos.Y))
                continue;

            if (hands?.ActiveItem != null && equipment.CanEquip(hands.ActiveItem, slot.Slot.Id))
                return equipment.TryEquipFromHands(hands, slot.Slot.Id);

            if (slot.Slot.Item != null && hands != null)
            {
                return equipment.TryUnequipOrDrop(slot.Slot.Id, hands, hands.ActiveHandIndex);
            }

            return true;
        }

        return false;
    }

    private bool TryHandleEquipmentRightClick(Vector2 mousePos, Entity actor, EquipmentComponent equipment)
    {
        foreach (var slot in BuildEquipmentSlots(equipment))
        {
            if (!slot.Rect.Contains((int)mousePos.X, (int)mousePos.Y))
                continue;

            if (slot.Slot.Item?.HasComponent<StorageComponent>() == true)
                OpenStorage(actor, slot.Slot.Item);

            return true;
        }

        return false;
    }

    private void DropEquippedItem(EquipmentComponent equipment, string slotId)
    {
        var slot = equipment.GetSlot(slotId);
        if (slot?.Item == null)
            return;

        var itemEntity = slot.Item;
        var item = itemEntity.GetComponent<ItemComponent>();
        slot.Item = null;

        if (item != null)
            item.ContainedIn = null;

        var ownerTf = equipment.Owner?.GetComponent<TransformComponent>();
        var itemTf = itemEntity.GetComponent<TransformComponent>();
        if (ownerTf != null && itemTf != null)
            itemTf.Position = ownerTf.Position;

        itemEntity.Active = true;
    }
}
