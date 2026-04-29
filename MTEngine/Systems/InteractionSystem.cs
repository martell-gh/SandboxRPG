using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MTEngine.Combat;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.Crafting;
using MTEngine.ECS;
using MTEngine.Interactions;
using MTEngine.Items;
using MTEngine.Metabolism;
using MTEngine.Npc;
using MTEngine.Rendering;
using MTEngine.UI;
using MTEngine.World;
using MTEngine.Wounds;

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
    private IKeyBindingSource? _keys;
    private Camera? _camera;
    private SpriteBatch? _sb;
    private SpriteFont? _font;
    private Texture2D? _pixel;
    private GraphicsDevice? _gd;
    private Texture2D? _handSlotTexture;
    private Texture2D? _handSlotBlockedTexture;
    private readonly Dictionary<string, Texture2D?> _equipmentSlotTextures = new();
    private readonly Dictionary<string, SpriteMaskData> _spriteMaskCache = new();
    private readonly Dictionary<string, Texture2D> _statusIconTextures = new();
    private UITheme? _theme;

    // Menu state
    private bool _menuOpen;
    private Vector2 _menuScreenPos;
    private Entity? _targetEntity;
    private Entity? _menuTitleEntity;
    private InteractionContext? _activeContext;
    private List<MenuActionState> _menuActions = new();
    private int _hoveredIndex = -1;
    private Entity? _hoveredWorldEntity;
    private Entity? _openStorageEntity;
    private Entity? _storageActor;
    private Entity? _draggedItem;
    private int? _draggedFromHandIndex;
    private string? _draggedFromEquipmentSlotId;
    private bool _draggedFromStorage;
    private Point? _storageWindowPosition;
    private bool _draggingStorageWindow;
    private Point _storageDragOffset;
    private DelayedInteractionState? _activeDelayedInteraction;

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

    private sealed class MenuActionState
    {
        public required InteractionEntry Entry { get; init; }
        public required InteractionContext Context { get; init; }
    }

    private sealed class DelayedInteractionState
    {
        public required InteractionEntry Entry { get; init; }
        public required InteractionContext Context { get; init; }
        public required Entity Actor { get; init; }
        public required Vector2 StartPosition { get; init; }
        public required float Duration { get; init; }
        public required string ProgressLabel { get; init; }
        public float Elapsed { get; set; }
    }

    private readonly struct FloatBounds
    {
        public FloatBounds(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public float X { get; }
        public float Y { get; }
        public float Width { get; }
        public float Height { get; }
        public float Left => X;
        public float Top => Y;
        public float Right => X + Width;
        public float Bottom => Y + Height;

        public bool Contains(Vector2 point)
            => point.X >= Left && point.X <= Right && point.Y >= Top && point.Y <= Bottom;
    }

    private sealed class SpriteMaskData
    {
        public required Rectangle OpaqueBounds { get; init; }
        public required Point[] OpaquePixels { get; init; }
        public required Point[] EdgePixels { get; init; }
    }

    private sealed class CombinedOutlineData
    {
        public required Point[] EdgePixels { get; init; }
        public required Rectangle Bounds { get; init; }
    }

    private sealed class StatusEffectIconInfo
    {
        public required Rectangle Rect { get; init; }
        public required StatusEffectDefinition Effect { get; init; }
    }

    public bool RequestsInteractionSlowdown
        => _menuOpen
           && _targetEntity != null
           && _targetEntity != _activeContext?.Actor
           && _targetEntity.HasComponent<NpcTagComponent>();

    public void SetFont(SpriteFont font) => _font = font;

    public override void OnInitialize()
    {
        _input = ServiceLocator.Get<InputManager>();
        _keys = ServiceLocator.Has<IKeyBindingSource>() ? ServiceLocator.Get<IKeyBindingSource>() : null;
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

        _theme ??= ServiceLocator.Has<UITheme>() ? ServiceLocator.Get<UITheme>() : null;
    }

    /// <summary>Draw a themed 9-slice background or fallback to flat color.</summary>
    private void DrawThemedBackground(Rectangle rect, Color fallbackColor)
    {
        if (_sb == null || _pixel == null) return;
        var slice = _theme?.WindowBackground;
        if (slice != null)
        {
            // Shadow
            var shadowAlpha = _theme?.ShadowAlpha ?? 0.35f;
            _sb.Draw(_pixel, new Rectangle(rect.X + 4, rect.Y + 4, rect.Width, rect.Height),
                Color.Black * shadowAlpha);
            slice.Draw(_sb, rect, _theme?.WindowBackgroundTint ?? Color.White);
        }
        else
        {
            _sb.Draw(_pixel, new Rectangle(rect.X + 3, rect.Y + 3, rect.Width, rect.Height), Color.Black * 0.4f);
            _sb.Draw(_pixel, rect, fallbackColor);
            UIDrawHelper.DrawBorder(_sb, _pixel, rect, new Color(70, 110, 70));
        }
    }

    /// <summary>Draw the themed close button (cross.png or fallback).</summary>
    private void DrawThemedCloseButton(Rectangle rect, bool hovered)
    {
        if (_sb == null || _pixel == null) return;
        var closeTex = _theme?.CloseButtonTexture;
        if (closeTex != null)
        {
            var tint = hovered
                ? (_theme?.CloseButtonHoverTint ?? Color.White)
                : (_theme?.CloseButtonTint ?? Color.White);
            _sb.Draw(closeTex, rect, tint);
        }
        else
        {
            _sb.Draw(_pixel, rect, hovered ? new Color(180, 50, 50) : new Color(90, 30, 30));
            if (_font != null)
                _sb.DrawString(_font, "X", new Vector2(rect.X + 5, rect.Y + 1), Color.White);
        }
    }

    private float GetUiScale()
        => ServiceLocator.Has<IUiScaleSource>()
            ? Math.Clamp(ServiceLocator.Get<IUiScaleSource>().UiScale, 0.75f, 2f)
            : 1f;

    private Point GetUiMousePoint()
    {
        if (_input == null)
            return Point.Zero;

        var scale = GetUiScale();
        var mouse = _input.MousePosition;
        return new Point(
            (int)MathF.Round(mouse.X / scale),
            (int)MathF.Round(mouse.Y / scale));
    }

    private int GetUiViewportWidth()
        => GameEngine.Instance.GetUiLogicalBounds(GetUiScale()).Width;

    private int GetUiViewportHeight()
        => GameEngine.Instance.GetUiLogicalBounds(GetUiScale()).Height;

    public override void Update(float deltaTime)
    {
        if (_input == null || _camera == null) return;

        if (ServiceLocator.Has<IGodModeService>() && ServiceLocator.Get<IGodModeService>().IsGodModeActive)
        {
            CloseMenu();
            CloseStorage();
            return;
        }

        if (ServiceLocator.Has<ITradeUiService>() && ServiceLocator.Get<ITradeUiService>().IsTradeOpen)
        {
            CloseMenu();
            CloseStorage();
            return;
        }

        if (DevConsole.IsOpen) { CloseMenu(); CloseStorage(); return; }

        var player = GetPrimaryActor();
        var hands = player?.GetComponent<HandsComponent>();
        var equipment = player?.GetComponent<EquipmentComponent>();
        var combatMode = player?.GetComponent<CombatModeComponent>();
        var rawMousePos = new Vector2(_input.ViewportMousePosition.X, _input.ViewportMousePosition.Y);
        var mousePos = new Vector2(GetUiMousePoint().X, GetUiMousePoint().Y);
        var worldPos = _camera.ScreenToWorld(rawMousePos);
        var combatSystem = World.GetSystem<CombatSystem>();
        _hoveredWorldEntity = combatMode?.CombatEnabled == true && player != null && combatSystem != null
            ? FindHoveredCombatTarget(player, worldPos, combatSystem) ?? FindHoveredInteractable(worldPos, player)
            : FindHoveredInteractable(worldPos, player);

        if (player != null && combatMode != null && _input.IsPressed(GetKey("CombatMode", Keys.C)))
        {
            combatMode.CombatEnabled = !combatMode.CombatEnabled;
            PopupTextSystem.Show(
                player,
                combatMode.CombatEnabled ? "Боевой режим" : "Обычный режим",
                combatMode.CombatEnabled ? Color.IndianRed : Color.LightGray,
                lifetime: 1.2f);
            if (ServiceLocator.Has<IWorldStateTracker>())
                ServiceLocator.Get<IWorldStateTracker>().MarkDirty();
            return;
        }

        UpdateDelayedInteraction(deltaTime);

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

        if (_menuOpen && ShouldCloseWorldMenu(player))
        {
            CloseMenu();
            return;
        }

        if (!_menuOpen && hands != null)
        {
            if (_input.IsPressed(GetKey("SwapHand", Keys.Tab)))
            {
                InterruptDelayedInteractionForAction();
                hands.SwapActiveHand();
                return;
            }

            if (_input.IsPressed(GetKey("Drop", Keys.Q)))
            {
                InterruptDelayedInteractionForAction();
                hands.TryDropActive();
                return;
            }

            if (_input.IsPressed(GetKey("Use", Keys.E)))
            {
                InterruptDelayedInteractionForAction();
                TryUseActiveItem(player!, hands);
                return;
            }
        }

        if (_draggedItem == null && _input.LeftClicked)
        {
            InterruptDelayedInteractionForAction();
            if (TryBeginDrag(mousePos, hands, equipment))
                return;
        }

        if (_draggedItem != null && _input.LeftReleased)
        {
            InterruptDelayedInteractionForAction();
            CompleteDrag(mousePos, hands, equipment);
            return;
        }

        if (equipment != null)
        {
            if (_input.LeftClicked && TryHandleEquipmentLeftClick(mousePos, hands, equipment))
            {
                InterruptDelayedInteractionForAction();
                return;
            }

            if (_input.RightClicked && TryHandleEquipmentRightClick(mousePos, player!, equipment))
            {
                InterruptDelayedInteractionForAction();
                return;
            }
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

        if (player != null
            && combatMode?.CombatEnabled == true
            && RangedCombatSystem.HasActiveRangedWeapon(player)
            && (_input.LeftClicked || _input.LeftDown || _input.LeftReleased))
        {
            return;
        }

        if (_input.LeftClicked && player != null && combatMode?.CombatEnabled == true)
        {
            TryExecuteCombatModeAttack(player, worldPos);
            return;
        }

        if (_input.LeftClicked && player != null && TryExecutePrimaryWorldInteraction(player, worldPos))
            return;

        if (_input.RightClicked)
            TryOpenMenu(worldPos, mousePos);
    }

    /// <summary>
    /// Collects all InteractionEntry from every IInteractionSource component on the entity.
    /// </summary>
    private List<MenuActionState> CollectActions(InteractionContext ctx)
    {
        var entries = new List<MenuActionState>();
        var dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        CollectActionsFromEntity(ctx.Target, ctx, entries, dedupe);

        if (ctx.Actor != ctx.Target)
        {
            CollectActionsFromEntity(ctx.Actor, ctx, entries, dedupe);
        }

        var hands = ctx.Actor.GetComponent<HandsComponent>();
        var activeItem = hands?.ActiveItem;
        if (activeItem != null)
            CollectActionsFromHeldItem(activeItem, ctx, entries, dedupe);

        // Sort by priority descending (higher priority = closer to top)
        entries.Sort((a, b) => b.Entry.Priority.CompareTo(a.Entry.Priority));
        return entries;
    }

    private static void CollectActionsFromEntity(Entity sourceEntity, InteractionContext ctx, List<MenuActionState> entries, HashSet<string> dedupe)
    {
        foreach (var component in sourceEntity.GetAllComponents())
        {
            if (component is not IInteractionSource source)
                continue;

            foreach (var entry in source.GetInteractions(ctx))
            {
                if (dedupe.Add(entry.Id))
                    entries.Add(new MenuActionState
                    {
                        Entry = entry,
                        Context = ctx
                    });
            }
        }
    }

    private static void CollectActionsFromHeldItem(Entity activeItem, InteractionContext ctx, List<MenuActionState> entries, HashSet<string> dedupe)
    {
        CollectActionsFromEntity(activeItem, ctx, entries, dedupe);

        if (ctx.Target != ctx.Actor)
        {
            var selfCtx = new InteractionContext
            {
                Actor = ctx.Actor,
                Target = ctx.Actor,
                World = ctx.World,
                OriginalTarget = ctx.Target
            };
            CollectActionsFromEntity(activeItem, selfCtx, entries, dedupe);
        }

        if (ctx.Target != activeItem)
        {
            var itemCtx = new InteractionContext
            {
                Actor = ctx.Actor,
                Target = activeItem,
                World = ctx.World,
                OriginalTarget = ctx.Target
            };
            CollectActionsFromEntity(activeItem, itemCtx, entries, dedupe);
        }
    }

    private bool TryExecutePrimaryWorldInteraction(Entity actor, Vector2 worldPos)
    {
        var target = FindHoveredInteractable(worldPos, actor);
        if (target == null)
            return false;

        if (IsUseBlockedByActiveWorker(actor, target, out var reason))
        {
            PopupTextSystem.Show(actor, reason, Color.LightGoldenrodYellow, lifetime: 1.25f);
            return true;
        }

        var ctx = new InteractionContext
        {
            Actor = actor,
            Target = target,
            World = World
        };

        var actions = CollectTargetOnlyActions(ctx);
        var primaryAction = actions
            .Where(action => action.Entry.IsPrimaryAction)
            .OrderByDescending(action => action.Entry.Priority)
            .FirstOrDefault();

        if (primaryAction == null)
        {
            var hands = actor.GetComponent<HandsComponent>();
            var activeItem = hands?.ActiveItem;
            if (activeItem != null)
            {
                var heldActions = CollectHeldItemPrimaryActions(activeItem, ctx);
                primaryAction = heldActions
                    .Where(action => action.Entry.IsPrimaryAction)
                    .OrderByDescending(action => action.Entry.Priority)
                    .FirstOrDefault();
            }
        }

        if (primaryAction == null)
            return false;

        return TryExecuteInteractionAction(primaryAction.Entry, primaryAction.Context);
    }

    private bool TryExecuteCombatModeAttack(Entity actor, Vector2 worldPos)
    {
        var combat = World.GetSystem<CombatSystem>();
        if (combat == null)
            return false;

        var target = FindHoveredCombatTarget(actor, worldPos, combat);
        return combat.TryAttackOrSwing(actor, target, worldPos);
    }

    private static List<MenuActionState> CollectTargetOnlyActions(InteractionContext ctx)
    {
        var entries = new List<MenuActionState>();
        var dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectActionsFromEntity(ctx.Target, ctx, entries, dedupe);
        entries.Sort((a, b) => b.Entry.Priority.CompareTo(a.Entry.Priority));
        return entries;
    }

    private static List<MenuActionState> CollectHeldItemPrimaryActions(Entity activeItem, InteractionContext ctx)
    {
        var entries = new List<MenuActionState>();
        var dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectActionsFromEntity(activeItem, ctx, entries, dedupe);
        entries.Sort((a, b) => b.Entry.Priority.CompareTo(a.Entry.Priority));
        return entries;
    }

    private Entity? FindHoveredCombatTarget(Entity actor, Vector2 worldPos, CombatSystem combat)
    {
        var attack = combat.GetCurrentAttackProfile(actor);
        Entity? best = null;
        float bestLayer = float.MinValue;
        float bestSortY = float.MinValue;

        foreach (var entity in World.GetEntitiesWith<TransformComponent>())
        {
            if (entity == actor || !entity.Active)
                continue;

            if (entity.GetComponent<HealthComponent>() == null &&
                entity.GetComponent<WoundComponent>() == null &&
                entity.GetComponent<TrainingDummyComponent>() == null)
            {
                continue;
            }

            if (!combat.CanAttack(actor, entity, attack))
                continue;

            if (!TryGetInteractionBounds(entity, out var bounds) || !bounds.Contains(worldPos))
                continue;

            var sprite = entity.GetComponent<SpriteComponent>();
            var layer = sprite?.LayerDepth ?? 0f;
            var sortY = GetInteractionSortY(entity);
            if (best == null || layer > bestLayer || (Math.Abs(layer - bestLayer) < 0.0001f && sortY > bestSortY))
            {
                best = entity;
                bestLayer = layer;
                bestSortY = sortY;
            }
        }

        return best;
    }

    private void TryOpenMenu(Vector2 worldPos, Vector2 screenPos)
    {
        var player = GetPrimaryActor();
        if (player == null) return;

        var best = FindHoveredInteractable(worldPos, player);

        // Self-interaction should only happen when the cursor is actually over the actor.
        if (best == null && IsPointOverEntity(player, worldPos))
        {
            var selfCtx = new InteractionContext
            {
                Actor = player,
                Target = player,
                World = World
            };

            var selfActions = CollectActions(selfCtx);
            if (selfActions.Count == 0) return;

            _targetEntity = player;
            _menuTitleEntity = player;
            _activeContext = selfCtx;
            _menuActions = selfActions;
            _menuScreenPos = screenPos;
            _menuOpen = true;
            _hoveredIndex = -1;

            Console.WriteLine($"[Interaction] Self-interaction ({selfActions.Count} actions)");
            return;
        }

        if (best == null)
            return;

        if (IsUseBlockedByActiveWorker(player, best, out var reason))
        {
            PopupTextSystem.Show(player, reason, Color.LightGoldenrodYellow, lifetime: 1.25f);
            return;
        }

        var ctx = new InteractionContext
        {
            Actor = player,
            Target = best,
            World = World
        };

        var actions = CollectActions(ctx);
        if (actions.Count == 0) return;

        _targetEntity = best;
        _menuTitleEntity = best;
        _activeContext = ctx;
        _menuActions = actions;
        _menuScreenPos = screenPos;
        _menuOpen = true;
        _hoveredIndex = -1;

        var name = GetInteractName(best);
        Console.WriteLine($"[Interaction] Opened: {name} ({actions.Count} actions)");
    }

    private bool IsUseBlockedByActiveWorker(Entity actor, Entity target, out string reason)
    {
        reason = "";
        if (actor == target
            || target.HasComponent<NpcTagComponent>()
            || target.HasComponent<PlayerTagComponent>()
            || target.HasComponent<DoorComponent>())
        {
            return false;
        }

        if (!ServiceLocator.Has<MapManager>())
            return false;

        var mapManager = ServiceLocator.Get<MapManager>();
        var map = mapManager.CurrentMap;
        var targetTransform = target.GetComponent<TransformComponent>();
        if (map == null || targetTransform == null)
            return false;

        var tile = new Point(
            (int)MathF.Floor(targetTransform.Position.X / map.TileSize),
            (int)MathF.Floor(targetTransform.Position.Y / map.TileSize));

        var area = map.Areas.FirstOrDefault(a =>
            string.Equals(a.Kind, AreaZoneKinds.Profession, StringComparison.OrdinalIgnoreCase)
            && a.ContainsTile(tile.X, tile.Y));
        if (area == null)
            return false;

        var worker = FindActiveWorkerForArea(area, map);
        if (worker == null || worker == actor)
            return false;

        reason = "Сейчас этим пользуется работник.";
        return true;
    }

    private Entity? FindActiveWorkerForArea(AreaZoneData area, MapData map)
    {
        foreach (var npc in World.GetEntitiesWith<NpcTagComponent, ProfessionComponent>())
        {
            if (npc.GetComponent<HealthComponent>()?.IsDead == true)
                continue;

            var profession = npc.GetComponent<ProfessionComponent>()!;
            if (!string.Equals(profession.SlotId, area.Id, StringComparison.OrdinalIgnoreCase))
                continue;

            var intent = npc.GetComponent<NpcIntentComponent>();
            if (intent?.Action != ScheduleAction.Work
                || !string.Equals(intent.TargetMapId, map.Id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!MerchantWorkRules.IsTradeOpenNow(npc))
                continue;

            var transform = npc.GetComponent<TransformComponent>();
            if (transform == null)
                continue;

            var tile = new Point(
                (int)MathF.Floor(transform.Position.X / map.TileSize),
                (int)MathF.Floor(transform.Position.Y / map.TileSize));
            if (!area.ContainsTile(tile.X, tile.Y))
                continue;

            return npc;
        }

        return null;
    }

    private Entity? FindHoveredInteractable(Vector2 worldPos, Entity? actor)
    {
        if (actor == null)
            return null;

        var actorTf = actor.GetComponent<TransformComponent>();
        if (actorTf == null)
            return null;

        Entity? best = null;
        float bestLayer = float.MinValue;
        float bestSortY = float.MinValue;

        foreach (var entity in World.GetEntitiesWith<TransformComponent>())
        {
            if (!CanEntityBeInteractedWith(entity)) continue;

            var tf = entity.GetComponent<TransformComponent>()!;
            if (Vector2.Distance(actorTf.Position, tf.Position) > GetInteractRange(entity))
                continue;

            if (!TryGetInteractionBounds(entity, out var bounds) || !bounds.Contains(worldPos))
                continue;

            var sprite = entity.GetComponent<SpriteComponent>();
            var layer = sprite?.LayerDepth ?? 0f;
            var sortY = GetInteractionSortY(entity);
            if (best == null || layer > bestLayer || (Math.Abs(layer - bestLayer) < 0.0001f && sortY > bestSortY))
            {
                best = entity;
                bestLayer = layer;
                bestSortY = sortY;
            }
        }

        return best;
    }

    private static float GetInteractionSortY(Entity entity)
    {
        var tf = entity.GetComponent<TransformComponent>();
        var sprite = entity.GetComponent<SpriteComponent>();
        if (tf == null || sprite == null || !sprite.YSort)
            return tf?.Position.Y ?? 0f;

        var sourceHeight = sprite.SourceRect?.Height ?? sprite.Height;
        return tf.Position.Y + (sourceHeight * tf.Scale.Y * 0.5f) + sprite.SortOffsetY;
    }

    private bool IsPointOverEntity(Entity entity, Vector2 worldPos)
        => TryGetInteractionBounds(entity, out var bounds) && bounds.Contains(worldPos);

    private bool TryGetInteractionBounds(Entity entity, out FloatBounds bounds)
    {
        bounds = default;

        var tf = entity.GetComponent<TransformComponent>();
        if (tf == null)
            return false;

        var sprite = entity.GetComponent<SpriteComponent>();
        if (sprite != null)
        {
            var localBounds = GetSpriteMaskData(sprite).OpaqueBounds;
            var left = tf.Position.X + (localBounds.X - sprite.Origin.X) * tf.Scale.X;
            var top = tf.Position.Y + (localBounds.Y - sprite.Origin.Y) * tf.Scale.Y;
            var width = localBounds.Width * Math.Abs(tf.Scale.X);
            var height = localBounds.Height * Math.Abs(tf.Scale.Y);

            // Расширяем bounds за счёт экипированных предметов
            var equipment = entity.GetComponent<EquipmentComponent>();
            if (equipment != null)
            {
                float minLeft = left, minTop = top;
                float maxRight = left + width, maxBottom = top + height;

                foreach (var slot in equipment.Slots)
                {
                    var wearable = slot.Item?.GetComponent<WearableComponent>();
                    if (wearable == null) continue;
                    var itemSprite = slot.Item?.GetComponent<SpriteComponent>();
                    var tex = wearable.EquippedTexture ?? itemSprite?.Texture;
                    if (tex == null) continue;

                    var srcRect = wearable.GetEquippedSourceRect() ?? itemSprite?.SourceRect;
                    var origin = wearable.EquippedTexture != null
                        ? wearable.EquippedOrigin
                        : itemSprite?.Origin ?? sprite.Origin;
                    var sw = srcRect?.Width ?? tex.Width;
                    var sh = srcRect?.Height ?? tex.Height;

                    var eqMask = GetMaskDataForTexture(tex, srcRect);
                    var eqLeft = tf.Position.X + (eqMask.OpaqueBounds.X - origin.X) * tf.Scale.X;
                    var eqTop = tf.Position.Y + (eqMask.OpaqueBounds.Y - origin.Y) * tf.Scale.Y;
                    var eqW = eqMask.OpaqueBounds.Width * Math.Abs(tf.Scale.X);
                    var eqH = eqMask.OpaqueBounds.Height * Math.Abs(tf.Scale.Y);

                    if (eqW > 0f && eqH > 0f)
                    {
                        minLeft = Math.Min(minLeft, eqLeft);
                        minTop = Math.Min(minTop, eqTop);
                        maxRight = Math.Max(maxRight, eqLeft + eqW);
                        maxBottom = Math.Max(maxBottom, eqTop + eqH);
                    }
                }

                left = minLeft;
                top = minTop;
                width = maxRight - minLeft;
                height = maxBottom - minTop;
            }

            if (width <= 0f || height <= 0f)
                return false;

            bounds = new FloatBounds(left, top, width, height);
            return true;
        }

        const float fallbackSize = 32f;
        bounds = new FloatBounds(tf.Position.X - fallbackSize / 2f, tf.Position.Y - fallbackSize / 2f, fallbackSize, fallbackSize);
        return true;
    }

    private SpriteMaskData GetSpriteMaskData(SpriteComponent sprite)
    {
        var source = sprite.SourceRect ?? new Rectangle(0, 0, sprite.Width, sprite.Height);
        if (sprite.Texture == null || source.Width <= 0 || source.Height <= 0)
        {
            return new SpriteMaskData
            {
                OpaqueBounds = new Rectangle(0, 0, Math.Max(1, source.Width), Math.Max(1, source.Height)),
                OpaquePixels = Array.Empty<Point>(),
                EdgePixels = Array.Empty<Point>()
            };
        }

        var cacheKey = $"{sprite.Texture.GetHashCode()}:{source.X}:{source.Y}:{source.Width}:{source.Height}";
        if (_spriteMaskCache.TryGetValue(cacheKey, out var cached))
            return cached;

        try
        {
            var pixels = new Color[source.Width * source.Height];
            sprite.Texture.GetData(0, source, pixels, 0, pixels.Length);

            var minX = source.Width;
            var minY = source.Height;
            var maxX = -1;
            var maxY = -1;
            var opaquePixels = new List<Point>();
            var edgePixels = new List<Point>();

            for (int y = 0; y < source.Height; y++)
            {
                for (int x = 0; x < source.Width; x++)
                {
                    var color = pixels[y * source.Width + x];
                    if (color.A <= 10)
                        continue;

                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                    opaquePixels.Add(new Point(x, y));

                    if (IsEdgePixel(pixels, source.Width, source.Height, x, y))
                        edgePixels.Add(new Point(x, y));
                }
            }

            var mask = new SpriteMaskData
            {
                OpaqueBounds = maxX >= minX && maxY >= minY
                    ? new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1)
                    : new Rectangle(0, 0, source.Width, source.Height),
                OpaquePixels = opaquePixels.ToArray(),
                EdgePixels = edgePixels.ToArray()
            };

            _spriteMaskCache[cacheKey] = mask;
            return mask;
        }
        catch
        {
            var fallback = new SpriteMaskData
            {
                OpaqueBounds = new Rectangle(0, 0, source.Width, source.Height),
                OpaquePixels = Array.Empty<Point>(),
                EdgePixels = Array.Empty<Point>()
            };
            _spriteMaskCache[cacheKey] = fallback;
            return fallback;
        }
    }

    private SpriteMaskData GetMaskDataForTexture(Texture2D texture, Rectangle? sourceRect)
    {
        var source = sourceRect ?? new Rectangle(0, 0, texture.Width, texture.Height);
        var cacheKey = $"{texture.GetHashCode()}:{source.X}:{source.Y}:{source.Width}:{source.Height}";
        if (_spriteMaskCache.TryGetValue(cacheKey, out var cached))
            return cached;

        try
        {
            var pixels = new Color[source.Width * source.Height];
            texture.GetData(0, source, pixels, 0, pixels.Length);

            int minX = source.Width, minY = source.Height, maxX = -1, maxY = -1;
            var opaquePixels = new List<Point>();
            var edgePixels = new List<Point>();

            for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
            {
                if (pixels[y * source.Width + x].A <= 10) continue;
                if (x < minX) minX = x; if (y < minY) minY = y;
                if (x > maxX) maxX = x; if (y > maxY) maxY = y;
                opaquePixels.Add(new Point(x, y));
                if (IsEdgePixel(pixels, source.Width, source.Height, x, y))
                    edgePixels.Add(new Point(x, y));
            }

            var mask = new SpriteMaskData
            {
                OpaqueBounds = maxX >= minX && maxY >= minY
                    ? new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1)
                    : new Rectangle(0, 0, source.Width, source.Height),
                OpaquePixels = opaquePixels.ToArray(),
                EdgePixels = edgePixels.ToArray()
            };
            _spriteMaskCache[cacheKey] = mask;
            return mask;
        }
        catch
        {
            var fallback = new SpriteMaskData
            {
                OpaqueBounds = new Rectangle(0, 0, source.Width, source.Height),
                OpaquePixels = Array.Empty<Point>(),
                EdgePixels = Array.Empty<Point>()
            };
            _spriteMaskCache[cacheKey] = fallback;
            return fallback;
        }
    }

    private static bool IsEdgePixel(Color[] pixels, int width, int height, int x, int y)
    {
        static bool IsTransparent(Color[] data, int w, int h, int px, int py)
            => px < 0 || py < 0 || px >= w || py >= h || data[py * w + px].A <= 10;

        return IsTransparent(pixels, width, height, x - 1, y)
            || IsTransparent(pixels, width, height, x + 1, y)
            || IsTransparent(pixels, width, height, x, y - 1)
            || IsTransparent(pixels, width, height, x, y + 1);
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
            Target = actor,
            World = World
        };

        var actions = CollectActionsFromHeldItem(activeItem, ctx);
        if (actions.Count == 0)
            return;

        _targetEntity = actor;
        _menuTitleEntity = activeItem;
        _activeContext = ctx;
        _menuActions = actions;
        _menuScreenPos = GetMouseAnchoredMenuPosition();
        _menuOpen = true;
        _hoveredIndex = -1;
    }

    private static List<MenuActionState> CollectActionsFromHeldItem(Entity activeItem, InteractionContext ctx)
    {
        var entries = new List<MenuActionState>();
        var dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectActionsFromHeldItem(activeItem, ctx, entries, dedupe);
        entries.Sort((a, b) => b.Entry.Priority.CompareTo(a.Entry.Priority));
        return entries;
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

    private bool ShouldCloseWorldMenu(Entity? actor)
    {
        if (!_menuOpen || actor == null || _targetEntity == null)
            return false;

        if (_targetEntity == actor)
            return false;

        var actorTf = actor.GetComponent<TransformComponent>();
        var targetTf = _targetEntity.GetComponent<TransformComponent>();
        if (actorTf == null || targetTf == null)
            return true;

        if (!CanEntityBeInteractedWith(_targetEntity))
            return true;

        return Vector2.Distance(actorTf.Position, targetTf.Position) > GetInteractRange(_targetEntity);
    }

    private static string GetInteractName(Entity entity)
    {
        var interactable = entity.GetComponent<InteractableComponent>();
        if (!string.IsNullOrWhiteSpace(interactable?.DisplayName))
            return LocalizationManager.T(interactable.DisplayName);

        var item = entity.GetComponent<ItemComponent>();
        if (!string.IsNullOrWhiteSpace(item?.ItemName))
            return LocalizationManager.T(item.ItemName);

        var storage = entity.GetComponent<StorageComponent>();
        if (!string.IsNullOrWhiteSpace(storage?.StorageName))
            return LocalizationManager.T(storage.StorageName);

        return LocalizationManager.T(entity.Name);
    }

    public void OpenStorage(Entity actor, Entity storageEntity, bool allowNpcStorage = false)
    {
        var storage = storageEntity.GetComponent<StorageComponent>();
        if (storage == null)
            return;

        if (!allowNpcStorage
            && storageEntity != actor
            && storageEntity.HasComponent<NpcTagComponent>())
        {
            PopupTextSystem.Show(actor, "Только через кражу.", Color.LightGoldenrodYellow, lifetime: 1.2f);
            return;
        }

        EnsureStorageInitialContentsReady(storageEntity, storage);
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
        Console.WriteLine($"[Interaction] Execute: {action.Entry.Label}");
        if (!TryExecuteInteractionAction(action.Entry, action.Context))
            return;

        CloseMenu();
    }

    private bool TryExecuteInteractionAction(InteractionEntry action, InteractionContext context)
    {
        if (action.InterruptsCurrentAction)
            InterruptDelayedInteractionForAction();

        if (action.Delay is { Duration: > 0f } delay)
        {
            if (_activeDelayedInteraction != null)
                return false;

            BeginDelayedInteraction(action, context, delay);
            return true;
        }

        action.Execute?.Invoke(context);
        return true;
    }

    private void BeginDelayedInteraction(InteractionEntry action, InteractionContext context, InteractionDelay delay)
    {
        var actorTf = context.Actor.GetComponent<TransformComponent>();
        ApplyDelayedInteractionHold(action, context);
        _activeDelayedInteraction = new DelayedInteractionState
        {
            Entry = action,
            Context = context,
            Actor = context.Actor,
            StartPosition = actorTf?.Position ?? Vector2.Zero,
            Duration = delay.Duration,
            ProgressLabel = string.IsNullOrWhiteSpace(delay.ProgressLabel) ? action.Label : delay.ProgressLabel
        };
    }

    private void UpdateDelayedInteraction(float deltaTime)
    {
        if (_activeDelayedInteraction == null)
            return;

        var state = _activeDelayedInteraction;
        var delay = state.Entry.Delay;
        if (delay == null)
        {
            _activeDelayedInteraction = null;
            return;
        }

        if (!state.Actor.Active || state.Actor.GetComponent<HealthComponent>()?.IsDead == true)
        {
            CancelDelayedInteraction("Прервано");
            return;
        }

        if (delay.CancelOnMove)
        {
            var tf = state.Actor.GetComponent<TransformComponent>();
            if (tf == null || Vector2.DistanceSquared(tf.Position, state.StartPosition) > 1f)
            {
                CancelDelayedInteraction("Прервано движением");
                return;
            }
        }

        state.Elapsed += deltaTime;
        RefreshDelayedInteractionHold(state);
        if (state.Elapsed < state.Duration)
            return;

        _activeDelayedInteraction = null;
        ReleaseDelayedInteractionHold(state);
        state.Entry.Execute?.Invoke(state.Context);
    }

    private void InterruptDelayedInteractionForAction()
    {
        if (_activeDelayedInteraction?.Entry.Delay?.CancelOnOtherAction == true)
            CancelDelayedInteraction("Действие прервано");
    }

    private void CancelDelayedInteraction(string popupText)
    {
        if (_activeDelayedInteraction == null)
            return;

        var actor = _activeDelayedInteraction.Actor;
        ReleaseDelayedInteractionHold(_activeDelayedInteraction);
        _activeDelayedInteraction = null;
        PopupTextSystem.Show(actor, popupText, Color.LightGray, lifetime: 1f);
    }

    private static void ApplyDelayedInteractionHold(InteractionEntry action, InteractionContext context)
    {
        if (!string.Equals(action.Id, "npc.talk", StringComparison.OrdinalIgnoreCase))
            return;

        var target = context.Target;
        if (!target.HasComponent<NpcTagComponent>())
            return;

        var hold = target.GetComponent<NpcInteractionHoldComponent>()
                   ?? target.AddComponent(new NpcInteractionHoldComponent());
        hold.ActorId = context.Actor.Id;
        FaceTarget(context.Actor, target);
    }

    private static void RefreshDelayedInteractionHold(DelayedInteractionState state)
    {
        if (!string.Equals(state.Entry.Id, "npc.talk", StringComparison.OrdinalIgnoreCase))
            return;

        FaceTarget(state.Context.Actor, state.Context.Target);
    }

    private static void ReleaseDelayedInteractionHold(DelayedInteractionState state)
    {
        if (string.Equals(state.Entry.Id, "npc.talk", StringComparison.OrdinalIgnoreCase)
            && state.Context.Target.GetComponent<NpcInteractionHoldComponent>() != null)
        {
            state.Context.Target.RemoveComponent<NpcInteractionHoldComponent>();
        }
    }

    private static void FaceTarget(Entity actor, Entity target)
    {
        var actorPosition = actor.GetComponent<TransformComponent>()?.Position;
        var targetPosition = target.GetComponent<TransformComponent>()?.Position;
        if (!actorPosition.HasValue || !targetPosition.HasValue)
            return;

        if (target.GetComponent<VelocityComponent>() is { } velocity)
            velocity.Velocity = Vector2.Zero;

        target.GetComponent<SpriteComponent>()?.PlayDirectionalIdle(actorPosition.Value - targetPosition.Value);
    }

    private void CloseMenu()
    {
        _menuOpen = false;
        _targetEntity = null;
        _menuTitleEntity = null;
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
        var pickpocket = _openStorageEntity.GetComponent<PickpocketComponent>();
        if (storage == null) return;

        foreach (var row in BuildStorageRows(storage, hands))
        {
            if (!row.Rect.Contains((int)mousePos.X, (int)mousePos.Y))
                continue;

            if (row.IsStoredItem)
            {
                if (rightClick)
                {
                    InterruptDelayedInteractionForAction();
                    if (pickpocket != null)
                    {
                        if (pickpocket.TryStealItem(_storageActor, row.Item))
                            storage.TryRemove(row.Item);
                        else
                            CloseStorage();
                    }
                    else
                    {
                        storage.TryRemove(row.Item);
                    }
                }
                else if (hands != null)
                {
                    InterruptDelayedInteractionForAction();
                    if (pickpocket != null)
                    {
                        if (pickpocket.TryStealItem(_storageActor, row.Item))
                            storage.TryRemoveToHands(row.Item, hands);
                        else
                            CloseStorage();
                    }
                    else
                    {
                        storage.TryRemoveToHands(row.Item, hands);
                    }
                }
            }
            else if (!rightClick)
            {
                InterruptDelayedInteractionForAction();
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
        if (!_menuOpen && hands == null && equipment == null && _openStorageEntity == null && _hoveredWorldEntity == null && _activeDelayedInteraction == null) return;

        if (_hoveredWorldEntity != null)
        {
            _sb.Begin(samplerState: SamplerState.PointClamp);
            DrawHoveredEntityOutline(_hoveredWorldEntity);
            _sb.End();
        }

        var uiScale = GetUiScale();
        _sb.Begin(
            samplerState: SamplerState.LinearClamp,
            transformMatrix: GameEngine.Instance.GetUiTransform(uiScale));

        DrawDelayedInteractionProgress();

        if (_menuOpen)
        {
            DrawInteractionMenu();
        }

        if (_openStorageEntity != null)
            DrawStorageWindow();

        if (GetPrimaryActor()?.GetComponent<CombatModeComponent>()?.CombatEnabled == true)
            DrawCombatModeCursor();
        else if (hands != null)
            DrawActiveHandCursorIcon(hands);

        if (hands != null || equipment != null)
            DrawEquipmentBar(hands, equipment);

        if (_draggedItem == null && _input != null)
            DrawHoveredSlotTooltip(new Vector2(GetUiMousePoint().X, GetUiMousePoint().Y), hands, equipment);

        if (_input != null)
            DrawHoveredStorageTooltip(new Vector2(GetUiMousePoint().X, GetUiMousePoint().Y), hands);

        if (_input != null)
            DrawStatusEffectTooltip(new Vector2(GetUiMousePoint().X, GetUiMousePoint().Y));

        if (_draggedItem != null && _input != null)
            DrawDraggedItem(new Vector2(GetUiMousePoint().X, GetUiMousePoint().Y));

        _sb.End();
    }

    private void DrawDelayedInteractionProgress()
    {
        if (_activeDelayedInteraction == null || _camera == null || _font == null || _pixel == null || _sb == null)
            return;

        var state = _activeDelayedInteraction;
        var tf = state.Actor.GetComponent<TransformComponent>();
        if (tf == null)
            return;

        var progress = Math.Clamp(state.Elapsed / Math.Max(0.01f, state.Duration), 0f, 1f);
        var anchor = _camera.WorldToScreen(tf.Position + new Vector2(0f, -34f));
        var width = 78;
        var height = 10;
        var rect = new Rectangle((int)MathF.Round(anchor.X - width / 2f), (int)MathF.Round(anchor.Y), width, height);
        var fillWidth = Math.Max(0, (int)MathF.Round((width - 2) * progress));
        var label = state.ProgressLabel;
        var labelSize = _font.MeasureString(label);
        var labelPos = new Vector2(rect.Center.X - labelSize.X * 0.5f, rect.Y - labelSize.Y - 4f);

        _sb.Draw(_pixel, new Rectangle(rect.X - 1, rect.Y - 1, rect.Width + 2, rect.Height + 2), Color.Black * 0.45f);
        _sb.Draw(_pixel, rect, new Color(28, 32, 38, 220));
        if (fillWidth > 0)
            _sb.Draw(_pixel, new Rectangle(rect.X + 1, rect.Y + 1, fillWidth, rect.Height - 2), new Color(110, 200, 120));
        _sb.Draw(_pixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), new Color(180, 200, 180));
        _sb.Draw(_pixel, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), new Color(20, 20, 20));
        _sb.DrawString(_font, label, labelPos, Color.White);
    }

    private void DrawHoveredEntityOutline(Entity entity)
    {
        if (_camera == null)
            return;

        var outlineColor = ResolveOutlineColor(entity);
        var tf = entity.GetComponent<TransformComponent>();
        var sprite = entity.GetComponent<SpriteComponent>();
        if (tf == null || sprite == null)
        {
            if (!TryGetInteractionBounds(entity, out var worldBounds))
                return;

            var fallbackTopLeft = _camera.WorldToScreen(new Vector2(worldBounds.Left, worldBounds.Top));
            var fallbackBottomRight = _camera.WorldToScreen(new Vector2(worldBounds.Right, worldBounds.Bottom));
            var fallbackX = (int)MathF.Round(Math.Min(fallbackTopLeft.X, fallbackBottomRight.X));
            var fallbackY = (int)MathF.Round(Math.Min(fallbackTopLeft.Y, fallbackBottomRight.Y));
            var fallbackWidth = Math.Max(4, (int)MathF.Round(Math.Abs(fallbackBottomRight.X - fallbackTopLeft.X)));
            var fallbackHeight = Math.Max(4, (int)MathF.Round(Math.Abs(fallbackBottomRight.Y - fallbackTopLeft.Y)));
            DrawOutlineRect(new Rectangle(fallbackX - 1, fallbackY - 1, fallbackWidth + 2, fallbackHeight + 2), outlineColor);
            return;
        }

        var combinedOutline = BuildCombinedOutlineData(entity, sprite);
        if (combinedOutline?.EdgePixels.Length > 0)
        {
            DrawOutlineMask(tf, tf.Scale, combinedOutline, outlineColor);
            return;
        }

        if (!TryGetInteractionBounds(entity, out var fallbackWorldBounds))
            return;

        var fallbackBoundsTopLeft = _camera.WorldToScreen(new Vector2(fallbackWorldBounds.Left, fallbackWorldBounds.Top));
        var fallbackBoundsBottomRight = _camera.WorldToScreen(new Vector2(fallbackWorldBounds.Right, fallbackWorldBounds.Bottom));
        var fallbackBoundsX = (int)MathF.Round(Math.Min(fallbackBoundsTopLeft.X, fallbackBoundsBottomRight.X));
        var fallbackBoundsY = (int)MathF.Round(Math.Min(fallbackBoundsTopLeft.Y, fallbackBoundsBottomRight.Y));
        var fallbackBoundsWidth = Math.Max(4, (int)MathF.Round(Math.Abs(fallbackBoundsBottomRight.X - fallbackBoundsTopLeft.X)));
        var fallbackBoundsHeight = Math.Max(4, (int)MathF.Round(Math.Abs(fallbackBoundsBottomRight.Y - fallbackBoundsTopLeft.Y)));
        DrawOutlineRect(new Rectangle(fallbackBoundsX - 1, fallbackBoundsY - 1, fallbackBoundsWidth + 2, fallbackBoundsHeight + 2), outlineColor);
    }

    private CombinedOutlineData? BuildCombinedOutlineData(Entity entity, SpriteComponent baseSprite)
    {
        var layers = new List<(SpriteMaskData Mask, Vector2 Origin)>
        {
            (GetSpriteMaskData(baseSprite), baseSprite.Origin)
        };

        var equipment = entity.GetComponent<EquipmentComponent>();
        if (equipment != null)
        {
            foreach (var slot in equipment.Slots)
            {
                var wearable = slot.Item?.GetComponent<WearableComponent>();
                if (wearable == null)
                    continue;

                var itemSprite = slot.Item?.GetComponent<SpriteComponent>();
                var texture = wearable.EquippedTexture ?? itemSprite?.Texture;
                if (texture == null)
                    continue;

                var sourceRect = wearable.GetEquippedSourceRect() ?? itemSprite?.SourceRect;
                var origin = wearable.EquippedTexture != null
                    ? wearable.EquippedOrigin
                    : itemSprite?.Origin ?? baseSprite.Origin;
                layers.Add((GetMaskDataForTexture(texture, sourceRect), origin));
            }
        }

        var opaque = new HashSet<Point>();
        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;

        foreach (var (mask, origin) in layers)
        {
            foreach (var pixel in mask.OpaquePixels)
            {
                var localX = (int)MathF.Round(pixel.X - origin.X);
                var localY = (int)MathF.Round(pixel.Y - origin.Y);
                var point = new Point(localX, localY);
                if (!opaque.Add(point))
                    continue;

                if (localX < minX) minX = localX;
                if (localY < minY) minY = localY;
                if (localX > maxX) maxX = localX;
                if (localY > maxY) maxY = localY;
            }
        }

        if (opaque.Count == 0)
            return null;

        var edges = new List<Point>();
        foreach (var point in opaque)
        {
            if (!opaque.Contains(new Point(point.X - 1, point.Y))
                || !opaque.Contains(new Point(point.X + 1, point.Y))
                || !opaque.Contains(new Point(point.X, point.Y - 1))
                || !opaque.Contains(new Point(point.X, point.Y + 1)))
            {
                edges.Add(point);
            }
        }

        return new CombinedOutlineData
        {
            EdgePixels = edges.ToArray(),
            Bounds = new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1)
        };
    }

    private Color ResolveOutlineColor(Entity entity)
    {
        var defaultColor = new Color(255, 220, 80);
        if (entity.GetComponent<DoorComponent>() == null)
            return defaultColor;

        var actor = GetPrimaryActor();
        if (actor == null)
            return defaultColor;

        if (World.GetSystem<InnRentalSystem>()?.TryGetDoorAccess(actor, entity, out var innAllowed) == true)
            return innAllowed ? new Color(255, 220, 80) : new Color(235, 72, 72);

        if (TryGetHouseDoorAccess(actor, entity, out var houseAllowed))
            return houseAllowed ? new Color(255, 220, 80) : new Color(235, 72, 72);

        return defaultColor;
    }

    private bool TryGetHouseDoorAccess(Entity actor, Entity door, out bool allowed)
    {
        allowed = false;
        if (!ServiceLocator.Has<MapManager>() || !ServiceLocator.Has<WorldRegistry>())
            return false;

        var map = ServiceLocator.Get<MapManager>().CurrentMap;
        var doorTransform = door.GetComponent<TransformComponent>();
        if (map == null || doorTransform == null)
            return false;

        var doorTile = new Point(
            (int)MathF.Floor(doorTransform.Position.X / map.TileSize),
            (int)MathF.Floor(doorTransform.Position.Y / map.TileSize));
        var registry = ServiceLocator.Get<WorldRegistry>();
        var house = registry.Houses.Values.FirstOrDefault(h =>
            string.Equals(h.MapId, map.Id, StringComparison.OrdinalIgnoreCase)
            && h.Tiles.Any(t => Math.Abs(t.X - doorTile.X) + Math.Abs(t.Y - doorTile.Y) <= 1));
        if (house == null || house.ResidentNpcSaveIds.Count == 0)
            return false;

        allowed = IsTrustedHouseVisitor(actor, house);
        return true;
    }

    private bool IsTrustedHouseVisitor(Entity actor, HouseDef house)
    {
        if (!actor.HasComponent<PlayerTagComponent>())
            return false;

        foreach (var npc in World.GetEntitiesWith<NpcTagComponent, ResidenceComponent>())
        {
            var residence = npc.GetComponent<ResidenceComponent>()!;
            if (!string.Equals(residence.HouseId, house.Id, StringComparison.OrdinalIgnoreCase))
                continue;

            var relationships = npc.GetComponent<RelationshipsComponent>();
            if (relationships is
                {
                    PartnerIsPlayer: true,
                    Status: RelationshipStatus.Dating or RelationshipStatus.Engaged or RelationshipStatus.Married
                })
            {
                return true;
            }
        }

        return false;
    }

    private void DrawOutlineMask(TransformComponent tf, Vector2 scale, CombinedOutlineData outline, Color outlineColor)
    {
        if (_camera == null || _sb == null || _pixel == null)
            return;

        var pixelWidth = Math.Max(1, (int)MathF.Ceiling(Math.Abs(scale.X) * _camera.Zoom));
        var pixelHeight = Math.Max(1, (int)MathF.Ceiling(Math.Abs(scale.Y) * _camera.Zoom));
        var glowColor = outlineColor * 0.30f;

        var cos = MathF.Cos(tf.Rotation);
        var sin = MathF.Sin(tf.Rotation);

        foreach (var edge in outline.EdgePixels)
        {
            var localX = edge.X * scale.X;
            var localY = edge.Y * scale.Y;
            var rotatedX = localX * cos - localY * sin;
            var rotatedY = localX * sin + localY * cos;

            var worldTopLeft = new Vector2(tf.Position.X + rotatedX, tf.Position.Y + rotatedY);
            var screenTopLeft = _camera.WorldToScreen(worldTopLeft);

            var rect = new Rectangle(
                (int)MathF.Round(screenTopLeft.X),
                (int)MathF.Round(screenTopLeft.Y),
                pixelWidth,
                pixelHeight);

            _sb.Draw(_pixel, new Rectangle(rect.X - 1, rect.Y - 1, rect.Width + 2, rect.Height + 2), glowColor);
            _sb.Draw(_pixel, rect, outlineColor);
        }
    }

    private void DrawOutlineRect(Rectangle rect, Color? color = null)
    {
        var outlineColor = color ?? new Color(255, 220, 80);
        var glowColor = outlineColor * 0.30f;

        _sb!.Draw(_pixel!, new Rectangle(rect.X - 1, rect.Y - 1, rect.Width + 2, 1), glowColor);
        _sb.Draw(_pixel, new Rectangle(rect.X - 1, rect.Bottom, rect.Width + 2, 1), glowColor);
        _sb.Draw(_pixel, new Rectangle(rect.X - 1, rect.Y, 1, rect.Height), glowColor);
        _sb.Draw(_pixel, new Rectangle(rect.Right, rect.Y, 1, rect.Height), glowColor);

        _sb.Draw(_pixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), outlineColor);
        _sb.Draw(_pixel, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), outlineColor);
        _sb.Draw(_pixel, new Rectangle(rect.X, rect.Y, 1, rect.Height), outlineColor);
        _sb.Draw(_pixel, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), outlineColor);
    }

    private void DrawInteractionMenu()
    {
        var targetName = _menuTitleEntity != null ? GetInteractName(_menuTitleEntity) : _targetEntity != null ? GetInteractName(_targetEntity) : "Object";
        var menuRect = GetMenuRect();

        // Background — 9-slice or flat
        DrawThemedBackground(menuRect, new Color(18, 22, 28));

        // Header text (no separate title bar bg)
        DrawFittedMenuText(
            targetName,
            new Rectangle(menuRect.X + 8, menuRect.Y + 4, menuRect.Width - 16, HeaderHeight - 4),
            Color.LimeGreen);

        for (int i = 0; i < _menuActions.Count; i++)
        {
            var itemRect = GetItemRect(i);
            bool hovered = i == _hoveredIndex;

            if (hovered)
                _sb!.Draw(_pixel!, itemRect, new Color(55, 85, 55) * 0.6f);

            DrawFittedMenuText(
                _menuActions[i].Entry.Label,
                new Rectangle(itemRect.X + 10, itemRect.Y + 2, itemRect.Width - 20, ItemHeight - 4),
                hovered ? Color.White : new Color(200, 200, 200));

            if (i < _menuActions.Count - 1)
                _sb!.Draw(_pixel!, new Rectangle(itemRect.X + 6, itemRect.Bottom, itemRect.Width - 12, 1),
                    Color.White * 0.08f);
        }
    }

    private void DrawFittedMenuText(string text, Rectangle bounds, Color color)
    {
        text = LocalizationManager.T(text);
        if (_sb == null || _font == null || string.IsNullOrWhiteSpace(text))
            return;

        var size = _font.MeasureString(text);
        if (size.X <= 0 || size.Y <= 0)
            return;

        var scale = MathF.Min(1f, bounds.Width / size.X);
        var scaledHeight = size.Y * scale;
        var pos = new Vector2(
            bounds.X,
            bounds.Y + MathF.Max(0f, (bounds.Height - scaledHeight) / 2f));

        _sb.DrawString(_font, text, pos, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
    }

    private void DrawEquipmentBar(HandsComponent? hands, EquipmentComponent? equipment)
    {
        if (_gd == null)
            return;

        DrawStatusEffectStrip();

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

    private void DrawStatusEffectStrip()
    {
        var actor = GetPrimaryActor();
        if (actor == null || _sb == null || _pixel == null)
            return;

        var icons = BuildStatusEffectIcons(actor);
        if (icons.Count == 0)
            return;

        foreach (var iconInfo in icons)
        {
            var effect = iconInfo.Effect;
            var rect = iconInfo.Rect;
            var tint = AssetManager.ParseHexColor(effect.Tint);
            var icon = GetOrCreateStatusIcon(effect.Id, effect.Pattern, tint);

            _sb.Draw(_pixel, new Rectangle(rect.X - 1, rect.Y - 1, rect.Width + 2, rect.Height + 2), Color.Black * 0.55f);
            _sb.Draw(_pixel, rect, new Color(18, 22, 28, 240));
            _sb.Draw(icon, rect, Color.White);
            _sb.Draw(_pixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), tint * 0.95f);
            _sb.Draw(_pixel, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), Color.Black * 0.75f);
            _sb.Draw(_pixel, new Rectangle(rect.X, rect.Y, 1, rect.Height), tint * 0.95f);
            _sb.Draw(_pixel, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), Color.Black * 0.75f);
        }
    }

    private void DrawCombatModeCursor()
    {
        if (_input == null || _sb == null || _pixel == null)
            return;

        var effect = StatusEffectCatalog.Get("combat_mode");
        if (effect == null)
            return;

        var mouse = GetUiMousePoint();
        var rect = new Rectangle(mouse.X + 14, mouse.Y - 6, 14, 14);
        var tint = AssetManager.ParseHexColor(effect.Tint);
        var icon = GetOrCreateStatusIcon(effect.Id, effect.Pattern, tint);

        _sb.Draw(_pixel, new Rectangle(rect.X - 1, rect.Y - 1, rect.Width + 2, rect.Height + 2), Color.Black * 0.55f);
        _sb.Draw(_pixel, rect, new Color(24, 16, 16, 235));
        _sb.Draw(icon, rect, Color.White);
        _sb.Draw(_pixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), tint * 0.95f);
        _sb.Draw(_pixel, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), Color.Black * 0.75f);
        _sb.Draw(_pixel, new Rectangle(rect.X, rect.Y, 1, rect.Height), tint * 0.95f);
        _sb.Draw(_pixel, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), Color.Black * 0.75f);
    }

    private void DrawStatusEffectTooltip(Vector2 mousePos)
    {
        if (_sb == null || _font == null || _pixel == null)
            return;

        var actor = GetPrimaryActor();
        if (actor == null)
            return;

        var hovered = BuildStatusEffectIcons(actor)
            .FirstOrDefault(icon => icon.Rect.Contains((int)mousePos.X, (int)mousePos.Y));
        if (hovered == null)
            return;

        var text = $"{hovered.Effect.Label}\n{hovered.Effect.Description}";
        var wrapped = SanitizeTooltipText(text);
        var size = _font.MeasureString(wrapped);
        var rect = new Rectangle(
            (int)mousePos.X + 16,
            (int)mousePos.Y - 8,
            (int)size.X + 12,
            (int)size.Y + 10);

        if (_gd != null)
        {
            if (rect.Right > GetUiViewportWidth())
                rect.X = Math.Max(0, (int)mousePos.X - rect.Width - 16);
            if (rect.Bottom > GetUiViewportHeight())
                rect.Y = Math.Max(0, GetUiViewportHeight() - rect.Height - 4);
        }

        var tint = AssetManager.ParseHexColor(hovered.Effect.Tint);
        _sb.Draw(_pixel, rect, Color.Black * 0.88f);
        _sb.Draw(_pixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), tint);
        _sb.Draw(_pixel, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), tint * 0.6f);
        _sb.Draw(_pixel, new Rectangle(rect.X, rect.Y, 1, rect.Height), tint);
        _sb.Draw(_pixel, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), tint * 0.6f);
        _sb.DrawString(_font, wrapped, new Vector2(rect.X + 6, rect.Y + 5), Color.White);
    }

    private List<StatusEffectIconInfo> BuildStatusEffectIcons(Entity actor)
    {
        const int iconSize = 18;
        const int gap = 4;
        var effects = BuildStatusEffects(actor);
        if (effects.Count == 0)
            return new List<StatusEffectIconInfo>();

        var area = GetStatusEffectAreaRect();
        var totalWidth = effects.Count * iconSize + Math.Max(0, effects.Count - 1) * gap;
        var startX = area.Center.X - totalWidth / 2;
        var y = area.Y;
        var icons = new List<StatusEffectIconInfo>(effects.Count);

        for (int i = 0; i < effects.Count; i++)
        {
            icons.Add(new StatusEffectIconInfo
            {
                Rect = new Rectangle(startX + i * (iconSize + gap), y, iconSize, iconSize),
                Effect = effects[i]
            });
        }

        return icons;
    }

    private List<StatusEffectDefinition> BuildStatusEffects(Entity actor)
    {
        var effectIds = new List<string>();
        var metabolism = actor.GetComponent<MetabolismComponent>();
        var wounds = actor.GetComponent<WoundComponent>();
        var combatMode = actor.GetComponent<CombatModeComponent>();

        if (combatMode?.CombatEnabled == true)
            effectIds.Add("combat_mode");

        if (wounds?.IsBleeding == true)
            effectIds.Add("bleeding");

        if (wounds is { ExhaustionDamage: > 0.5f })
            effectIds.Add("exhaustion");

        if (metabolism == null)
            return ResolveStatusEffects(effectIds);

        if (metabolism.HungerStatus == NeedStatus.Critical)
            effectIds.Add("starving");
        else if (metabolism.HungerStatus == NeedStatus.Warning)
            effectIds.Add("hungry");

        if (metabolism.ThirstStatus == NeedStatus.Critical)
            effectIds.Add("dehydrated");
        else if (metabolism.ThirstStatus == NeedStatus.Warning)
            effectIds.Add("thirsty");

        if (metabolism.BladderStatus == NeedStatus.Critical)
            effectIds.Add("bladder_critical");
        else if (metabolism.BladderStatus == NeedStatus.Warning)
            effectIds.Add("bladder_warning");

        if (metabolism.BowelStatus == NeedStatus.Critical)
            effectIds.Add("bowel_critical");
        else if (metabolism.BowelStatus == NeedStatus.Warning)
            effectIds.Add("bowel_warning");

        if (CanShowNaturalRegen(metabolism, wounds))
            effectIds.Add("regeneration");

        if (metabolism.SpeedModifier > 1.01f)
            effectIds.Add("vigor");
        else if (metabolism.SpeedModifier < 0.99f)
            effectIds.Add("slowed");

        return ResolveStatusEffects(effectIds);
    }

    private static List<StatusEffectDefinition> ResolveStatusEffects(IEnumerable<string> effectIds)
    {
        return effectIds
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(StatusEffectCatalog.Get)
            .Where(effect => effect != null)
            .Cast<StatusEffectDefinition>()
            .OrderByDescending(effect => effect.Priority)
            .ToList();
    }

    private static bool CanShowNaturalRegen(MetabolismComponent metabolism, WoundComponent? wounds)
    {
        if (wounds == null || wounds.TotalDamage <= 0.01f || wounds.IsBleeding)
            return false;

        return metabolism.Hunger >= metabolism.NaturalRegenThreshold
            && metabolism.Thirst >= metabolism.NaturalRegenThreshold
            && metabolism.BladderStatus != NeedStatus.Critical
            && metabolism.BowelStatus != NeedStatus.Critical;
    }

    private Texture2D GetOrCreateStatusIcon(string id, string pattern, Color tint)
    {
        if (_statusIconTextures.TryGetValue(id, out var cached))
            return cached;

        var texture = CreateStatusIconTexture(pattern, tint);
        _statusIconTextures[id] = texture;
        return texture;
    }

    private Texture2D CreateStatusIconTexture(string pattern, Color tint)
    {
        var rows = pattern.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var width = rows.Length == 0 ? 8 : rows[0].Length;
        var height = Math.Max(1, rows.Length);
        var texture = new Texture2D(_gd!, width, height);
        var pixels = new Color[width * height];
        var shadow = new Color(
            (byte)Math.Max(0, tint.R / 3),
            (byte)Math.Max(0, tint.G / 3),
            (byte)Math.Max(0, tint.B / 3),
            (byte)255);

        for (int y = 0; y < height; y++)
        {
            var row = rows[y];
            for (int x = 0; x < width; x++)
            {
                var ch = x < row.Length ? row[x] : '.';
                pixels[y * width + x] = ch switch
                {
                    '#' => tint,
                    '+' => Color.White,
                    'x' or 'X' => shadow,
                    _ => Color.Transparent
                };
            }
        }

        texture.SetData(pixels);
        return texture;
    }

    private Rectangle GetStatusEffectAreaRect()
    {
        var handsRect = GetHandBarRect();
        var equipmentRect = GetEquipmentGridRect();
        var left = Math.Min(handsRect.Left, equipmentRect.Left);
        var right = Math.Max(handsRect.Right, equipmentRect.Right);
        var y = Math.Min(handsRect.Top, equipmentRect.Top) - 24;
        return new Rectangle(left, y, right - left, 18);
    }

    private void DrawActiveHandCursorIcon(HandsComponent hands)
    {
        if (_input == null || hands.ActiveItem == null || _draggedItem != null)
            return;

        var mouse = GetUiMousePoint();
        var rect = new Rectangle(mouse.X + 14, mouse.Y - 6, 14, 14);
        DrawEntityIcon(hands.ActiveItem, rect, 0.45f);
    }

    private void DrawHoveredSlotTooltip(Vector2 mousePos, HandsComponent? hands, EquipmentComponent? equipment)
    {
        var hoveredItem = GetHoveredSlotItem(mousePos, hands, equipment);
        if (hoveredItem == null)
            return;

        var text = SanitizeTooltipText(GetSlotTooltipText(hoveredItem));
        if (string.IsNullOrWhiteSpace(text))
            return;

        var size = _font!.MeasureString(text);
        var rect = new Rectangle(
            (int)mousePos.X + 16,
            (int)mousePos.Y - 8,
            (int)size.X + 12,
            (int)size.Y + 8);

        if (_gd != null)
        {
            if (rect.Right > GetUiViewportWidth())
                rect.X = Math.Max(0, (int)mousePos.X - rect.Width - 16);
            if (rect.Bottom > GetUiViewportHeight())
                rect.Y = Math.Max(0, GetUiViewportHeight() - rect.Height - 4);
        }

        DrawThemedBackground(rect, Color.Black * 0.82f);
        _sb!.DrawString(_font, text, new Vector2(rect.X + 6, rect.Y + 4), Color.White);
    }

    private Entity? GetHoveredSlotItem(Vector2 mousePos, HandsComponent? hands, EquipmentComponent? equipment)
    {
        if (hands != null)
        {
            foreach (var slot in BuildHandSlots(hands))
            {
                if (slot.Rect.Contains((int)mousePos.X, (int)mousePos.Y) && slot.Hand.HeldItem != null)
                    return slot.Hand.HeldItem;
            }
        }

        if (equipment != null)
        {
            foreach (var slot in BuildEquipmentSlots(equipment))
            {
                if (slot.Rect.Contains((int)mousePos.X, (int)mousePos.Y) && slot.Slot.Item != null)
                    return slot.Slot.Item;
            }
        }

        return null;
    }

    private static string GetSlotTooltipText(Entity itemEntity)
    {
        var lines = new List<string>();

        var liquid = itemEntity.GetComponent<LiquidContainerComponent>();
        if (liquid != null)
            lines.Add($"{liquid.ContainerName} - {liquid.CurrentVolume:0.#}/{liquid.Capacity:0.#} мл");

        var item = itemEntity.GetComponent<ItemComponent>();
        if (lines.Count == 0 && !string.IsNullOrWhiteSpace(item?.ItemName))
        {
            var displayName = LocalizationManager.T(item.ItemName);
            lines.Add(item.Stackable && item.StackCount > 1
                ? $"{displayName} x{item.StackCount}"
                : displayName);
        }

        if (itemEntity.GetComponent<QualityTierComponent>() is { } qualityTier)
            lines.Add($"Тир: {qualityTier.GetDisplayLabel()}");

        if (itemEntity.GetComponent<CraftQualityComponent>() is { } craftQuality)
            lines.Add($"Работа: {craftQuality.Label}");

        if (itemEntity.GetComponent<RecipeNoteComponent>() is { } recipeNote)
        {
            var recipeTitle = !string.IsNullOrWhiteSpace(recipeNote.RecipeTitle)
                ? recipeNote.RecipeTitle
                : recipeNote.RecipeId;
            lines.Add($"Схема: {recipeTitle}");
        }

        if (lines.Count == 0)
            lines.Add(itemEntity.Name);

        return string.Join('\n', lines);
    }

    private static string SanitizeTooltipText(string text)
        => text
            .Replace('—', '-')
            .Replace('–', '-')
            .Replace('…', '.');

    private void DrawStorageWindow()
    {
        if (_openStorageEntity == null || _font == null) return;

        var storage = _openStorageEntity.GetComponent<StorageComponent>();
        if (storage == null) return;

        var hands = _storageActor?.GetComponent<HandsComponent>();
        var rect = GetStorageRect();
        var title = GetInteractName(_openStorageEntity);

        // Background — 9-slice or flat
        DrawThemedBackground(rect, Color.Black * 0.86f);

        // Title text (no separate header bar)
        _sb!.DrawString(_font, $"{title} [{storage.UsedSlots}/{storage.MaxSlots} slots]",
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
                DrawStorageRow(row, LocalizationManager.T(entity.GetComponent<ItemComponent>()!.ItemName), storage.GetSlotSize(entity), false);
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
            DrawStorageRow(row, LocalizationManager.T(item.ItemName), storage.GetSlotSize(entity), true);
            y += StorageRowHeight;
        }

        if (storage.Contents.Count == 0)
        {
            _sb.DrawString(_font, "[empty]", new Vector2(rect.X + StoragePadding, y), Color.Gray);
        }

        var hintText = IsPickpocketStorage(storage)
            ? "LMB/RMB: попытка украсть предмет"
            : "LMB: hand / store   RMB: drop from storage";
        _sb.DrawString(_font, hintText,
            new Vector2(rect.X + StoragePadding, rect.Bottom - 20), Color.Gray);
    }

    private void DrawStorageRow(StorageRowInfo row, string label, int slotSize, bool isStoredItem)
    {
        _sb!.Draw(_pixel!, row.Rect, Color.DarkSlateGray * 0.35f);
        var prefix = isStoredItem ? "[in]" : "[hold]";
        var suffix = slotSize == 1 ? "slot" : "slots";
        var text = FitTextWithEllipsis($"{prefix} {label} ({slotSize} {suffix})", row.Rect.Width - 30);
        DrawEntityIcon(row.Item, new Rectangle(row.Rect.X + 4, row.Rect.Y + 1, ItemIconSize, ItemIconSize));
        _sb.DrawString(_font!, text, new Vector2(row.Rect.X + 24, row.Rect.Y + 2), Color.White);
    }

    private IEnumerable<Entity> GetStoreableHandItems(StorageComponent storage, HandsComponent? hands)
    {
        if (IsPickpocketStorage(storage))
            yield break;

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

    private void DrawHoveredStorageTooltip(Vector2 mousePos, HandsComponent? hands)
    {
        if (_openStorageEntity == null || _font == null || _pixel == null)
            return;

        var storage = _openStorageEntity.GetComponent<StorageComponent>();
        var pickpocket = _openStorageEntity.GetComponent<PickpocketComponent>();
        if (storage == null || pickpocket == null || _storageActor == null)
            return;

        var hoveredRow = BuildStorageRows(storage, hands)
            .FirstOrDefault(row => row.IsStoredItem && row.Rect.Contains((int)mousePos.X, (int)mousePos.Y));
        if (hoveredRow == null)
            return;

        var itemName = LocalizationManager.T(hoveredRow.Item.GetComponent<ItemComponent>()?.ItemName ?? hoveredRow.Item.Name);
        var chance = pickpocket.GetStealChance(_storageActor, hoveredRow.Item);
        var text = SanitizeTooltipText($"{itemName}\nШанс кражи: {chance:0%}");
        var size = _font.MeasureString(text);
        var rect = new Rectangle(
            (int)mousePos.X + 16,
            (int)mousePos.Y - 8,
            (int)size.X + 12,
            (int)size.Y + 8);

        if (_gd != null)
        {
            if (rect.Right > GetUiViewportWidth())
                rect.X = Math.Max(0, (int)mousePos.X - rect.Width - 16);
            if (rect.Bottom > GetUiViewportHeight())
                rect.Y = Math.Max(0, GetUiViewportHeight() - rect.Height - 4);
        }

        var sb = _sb!;
        sb.Draw(_pixel!, rect, Color.Black * 0.86f);
        sb.Draw(_pixel!, new Rectangle(rect.X, rect.Y, rect.Width, 1), new Color(132, 98, 98));
        sb.Draw(_pixel!, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), new Color(132, 98, 98));
        sb.Draw(_pixel!, new Rectangle(rect.X, rect.Y, 1, rect.Height), new Color(132, 98, 98));
        sb.Draw(_pixel!, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), new Color(132, 98, 98));
        sb.DrawString(_font, text, new Vector2(rect.X + 6, rect.Y + 4), Color.White);
    }

    private string FitTextWithEllipsis(string text, int maxWidth)
    {
        if (_font == null || string.IsNullOrEmpty(text) || maxWidth <= 0)
            return string.Empty;

        if (_font.MeasureString(text).X <= maxWidth)
            return text;

        const string ellipsis = "...";
        var trimmed = text;
        while (trimmed.Length > 0)
        {
            trimmed = trimmed[..^1];
            var candidate = trimmed + ellipsis;
            if (_font.MeasureString(candidate).X <= maxWidth)
                return candidate;
        }

        return ellipsis;
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

    private bool IsPickpocketStorage(StorageComponent storage)
        => _storageActor != null
           && _openStorageEntity != null
           && _openStorageEntity != _storageActor
           && _openStorageEntity.GetComponent<PickpocketComponent>() != null;

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
        var mouse = GetUiMousePoint();
        var hovered = rect.Contains(mouse);
        DrawThemedCloseButton(rect, hovered);
    }

    private void MoveStorageWindow(Vector2 mousePos)
    {
        if (_gd == null)
            return;

        var x = (int)mousePos.X - _storageDragOffset.X;
        var y = (int)mousePos.Y - _storageDragOffset.Y;
        var currentRect = GetStorageRect();

        x = Math.Clamp(x, 0, Math.Max(0, GetUiViewportWidth() - currentRect.Width));
        y = Math.Clamp(y, 0, Math.Max(0, GetUiViewportHeight() - currentRect.Height));
        _storageWindowPosition = new Point(x, y);
    }

    private void EnsureStorageInitialContentsReady(Entity storageEntity, StorageComponent storage)
    {
        ReconcileStorageContents(storageEntity, storage);
        if (storage.Contents.Count > 0)
        {
            storage.MarkInitialContentsResolved();
            return;
        }

        var isPickpocketStorage = storageEntity.GetComponent<PickpocketComponent>() != null;
        if (isPickpocketStorage && !storage.RepairAttemptedThisSession)
        {
            storage.RepairAttemptedThisSession = true;

            if (storage.TryRestoreSpawnContentsIfMissing(ignoreResolved: true))
            {
                storage.MarkInitialContentsResolved();
                return;
            }

            if (TryPopulateStorageFromCurrentMap(storageEntity, storage))
            {
                storage.MarkInitialContentsResolved();
                return;
            }
        }

        if (storage.TryRestoreSpawnContentsIfMissing())
        {
            storage.MarkInitialContentsResolved();
            return;
        }

        if (storage.InitialContentsResolved)
            return;

        if (TryPopulateStorageFromCurrentMap(storageEntity, storage))
            storage.MarkInitialContentsResolved();
    }

    private void ReconcileStorageContents(Entity storageEntity, StorageComponent storage)
    {
        foreach (var entity in World.GetEntities())
        {
            var item = entity.GetComponent<ItemComponent>();
            if (item?.ContainedIn != storageEntity)
                continue;

            if (!storage.Contents.Contains(entity))
                storage.Contents.Add(entity);
        }

        storage.Contents.RemoveAll(entity => entity.GetComponent<ItemComponent>()?.ContainedIn != storageEntity);
    }

    private bool TryPopulateStorageFromCurrentMap(Entity storageEntity, StorageComponent storage)
    {
        if (!ServiceLocator.Has<MapManager>() || !ServiceLocator.Has<EntityFactory>() || !ServiceLocator.Has<PrototypeManager>())
            return false;

        var map = ServiceLocator.Get<MapManager>().CurrentMap;
        if (map == null)
            return false;

        var entry = FindMapEntityDataFor(storageEntity, map);
        if (entry == null || entry.ContainedEntities.Count == 0)
            return false;

        var hydrated = false;
        foreach (var containedEntry in entry.ContainedEntities)
        {
            if (TrySpawnContainedMapEntity(storage, containedEntry, map))
                hydrated = true;
        }

        return hydrated;
    }

    private MapEntityData? FindMapEntityDataFor(Entity entity, MapData map)
    {
        var transform = entity.GetComponent<TransformComponent>();
        if (transform == null || string.IsNullOrWhiteSpace(entity.PrototypeId))
            return null;

        foreach (var entry in map.Entities)
        {
            var found = FindMapEntityDataRecursive(entry, entity.PrototypeId, transform.Position, map);
            if (found != null)
                return found;
        }

        return null;
    }

    private MapEntityData? FindMapEntityDataRecursive(MapEntityData entry, string prototypeId, Vector2 position, MapData map)
    {
        if (string.Equals(entry.ProtoId, prototypeId, StringComparison.OrdinalIgnoreCase)
            && Vector2.DistanceSquared(GetMapEntryWorldPosition(entry, map), position) <= 1f)
        {
            return entry;
        }

        foreach (var child in entry.ContainedEntities)
        {
            var found = FindMapEntityDataRecursive(child, prototypeId, position, map);
            if (found != null)
                return found;
        }

        return null;
    }

    private static Vector2 GetMapEntryWorldPosition(MapEntityData entry, MapData map)
    {
        if (entry.WorldSpace)
            return new Vector2(entry.X, entry.Y);

        return new Vector2(
            (entry.X + 0.5f) * map.TileSize,
            (entry.Y + 0.5f) * map.TileSize);
    }

    private bool TrySpawnContainedMapEntity(StorageComponent parentStorage, MapEntityData entry, MapData map)
    {
        var prototype = ServiceLocator.Get<PrototypeManager>().GetEntity(entry.ProtoId);
        if (prototype == null)
            return false;

        var entity = ServiceLocator.Get<EntityFactory>().CreateFromPrototype(prototype, GetMapEntryWorldPosition(entry, map));
        if (entity == null)
            return false;

        ApplyMapComponentOverrides(entity, entry.ComponentOverrides);
        if (!parentStorage.TryInsertInitial(entity))
        {
            entity.World?.DestroyEntity(entity);
            return false;
        }

        if (entity.GetComponent<StorageComponent>() is { } childStorage)
        {
            foreach (var childEntry in entry.ContainedEntities)
                TrySpawnContainedMapEntity(childStorage, childEntry, map);

            if (childStorage.Contents.Count > 0)
                childStorage.MarkInitialContentsResolved();
        }

        return true;
    }

    private static void ApplyMapComponentOverrides(Entity entity, IReadOnlyDictionary<string, JsonObject> overrides)
    {
        foreach (var pair in overrides)
        {
            var componentType = ComponentRegistry.GetComponentType(pair.Key);
            if (componentType == null)
                continue;

            var component = entity.GetComponent(componentType);
            if (component == null)
                continue;

            ComponentPrototypeSerializer.ApplyData(component, pair.Value);
        }
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
            if (IsPickpocketStorage(storage))
                return false;

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
        var name = LocalizationManager.T(_draggedItem.GetComponent<ItemComponent>()?.ItemName ?? GetInteractName(_draggedItem));
        _sb.DrawString(_font!, name, new Vector2(rect.X + 24, rect.Y + 2), Color.White);
    }

    private void DrawEntityIcon(Entity entity, Rectangle rect, float alpha = 1f)
    {
        var item = entity.GetComponent<ItemComponent>();
        var liquid = entity.GetComponent<LiquidContainerComponent>();
        var fillTexture = liquid?.GetFillTexture();
        if (fillTexture != null)
        {
            _sb!.Draw(fillTexture, rect, liquid!.GetFillColor() * alpha);
        }

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
        }
        else
        {
            _sb!.Draw(_pixel!, rect, Color.Gray * alpha);
        }

        if (item is { Stackable: true, StackCount: > 1 } && _font != null)
            DrawStackCount(rect, item.StackCount, alpha);
    }

    private void DrawStackCount(Rectangle rect, int count, float alpha)
    {
        var text = count.ToString();
        var scale = 0.6f;
        var size = _font!.MeasureString(text) * scale;
        var pos = new Vector2(
            rect.Right - size.X - 1f,
            rect.Bottom - size.Y + 1f);

        _sb!.Draw(_pixel!, new Rectangle((int)pos.X - 2, (int)pos.Y - 1, (int)MathF.Ceiling(size.X) + 4, (int)MathF.Ceiling(size.Y) + 2), Color.Black * (0.82f * alpha));
        _sb.DrawString(_font, text, pos, Color.White * alpha, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
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
        return new Rectangle((GetUiViewportWidth() - totalWidth) / 2, GetUiViewportHeight() - HandSlotSize - 18, totalWidth, HandSlotSize);
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
            if (x + MenuWidth > GetUiViewportWidth()) x = GetUiViewportWidth() - MenuWidth - 4;
            if (y + totalH > GetUiViewportHeight()) y = GetUiViewportHeight() - totalH - 4;
            x = Math.Max(0, x);
            y = Math.Max(0, y);
        }

        return new Rectangle(x, y, MenuWidth, totalH);
    }

    private Vector2 GetMouseAnchoredMenuPosition()
    {
        if (_input == null)
            return _menuScreenPos;

        var mouse = GetUiMousePoint();
        return new Vector2(mouse.X + 14, mouse.Y - 8);
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

    private Keys GetKey(string action, Keys fallback)
        => _keys?.GetKey(action) ?? fallback;
}
