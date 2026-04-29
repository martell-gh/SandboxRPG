using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Items;
using MTEngine.Systems;
using MTEngine.World;

namespace MTEngine.Npc;

public class TradeSystem : GameSystem, ITradeUiService
{
    public override DrawLayer DrawLayer => DrawLayer.Overlay;

    private const float RevengeMerchantPenaltyMultiplier = 1.5f;

    private const int PanelPadding = 14;
    private const int HeaderHeight = 38;
    private const int FooterHeight = 28;
    private const int RowHeight = 30;
    private const int Gap = 12;

    private InputManager _input = null!;
    private SpriteBatch _sb = null!;
    private GraphicsDevice _gd = null!;
    private PrototypeManager _prototypes = null!;
    private EntityFactory _factory = null!;
    private MapManager? _mapManager;
    private WorldRegistry? _registry;
    private ProfessionCatalog? _professionCatalog;
    private SpriteFont? _font;
    private Texture2D? _pixel;
    private Entity? _buyer;
    private Entity? _shopEntity;
    private ShopComponent? _shop;
    private int _buyScroll;
    private int _sellScroll;

    public bool IsTradeOpen => _shop != null && _buyer != null && _shopEntity != null;

    public void SetFont(SpriteFont font) => _font = font;

    public override void OnInitialize()
    {
        _input = ServiceLocator.Get<InputManager>();
        ServiceLocator.Register<ITradeUiService>(this);
    }

    public override void Update(float deltaTime)
    {
        if (!IsTradeOpen)
            return;

        if (DevConsole.IsOpen)
            return;

        if (!EnsureDrawServices() || !EnsureRuntimeServices())
            return;

        if (_input.IsPressed(Keys.Escape) || ShouldCloseByDistance())
        {
            Close();
            return;
        }

        if (_shopEntity != null && !MerchantWorkRules.IsTradeOpenNow(_shopEntity))
        {
            PopupTextSystem.Show(_shopEntity, "Торговля закрыта.", Color.LightGoldenrodYellow, lifetime: 1.2f);
            Close();
            return;
        }

        HandleScroll();

        if (_input.LeftClicked)
            HandleLeftClick(_input.MousePosition);

        if (_input.RightClicked && !GetWindowRect().Contains(_input.MousePosition))
            Close();
    }

    public void OpenTrade(Entity buyer, Entity shopEntity)
    {
        var shop = shopEntity.GetComponent<ShopComponent>();
        if (shop == null)
            return;

        if (!EnsureRuntimeServices())
            return;

        if (!MerchantWorkRules.IsTradeOpenNow(shopEntity))
        {
            PopupTextSystem.Show(shopEntity, "Торговля закрыта.", Color.LightGoldenrodYellow, lifetime: 1.2f);
            return;
        }

        World.GetSystem<ShopRestockSystem>()?.RefreshShop(shopEntity);

        _buyer = buyer;
        _shopEntity = shopEntity;
        _shop = shop;
        _buyScroll = 0;
        _sellScroll = 0;
    }

    public void Close()
    {
        _buyer = null;
        _shopEntity = null;
        _shop = null;
        _buyScroll = 0;
        _sellScroll = 0;
    }

    public override void Draw()
    {
        if (!IsTradeOpen || _font == null)
            return;

        if (!EnsureDrawServices() || !EnsureRuntimeServices())
            return;

        EnsurePixel();
        if (_pixel == null)
            return;

        _sb.Begin(samplerState: SamplerState.PointClamp);

        var rect = GetWindowRect();
        DrawShadowedPanel(rect);
        DrawHeader(rect);

        var closeRect = GetCloseRect(rect);
        var mouse = _input.MousePosition;
        DrawButton(closeRect, "X", closeRect.Contains(mouse));

        var buyRect = GetBuyListRect(rect);
        var sellRect = GetSellListRect(rect);

        DrawSectionTitle(buyRect, "Продаёт");
        DrawSectionTitle(sellRect, "Ты можешь продать");

        DrawBuyRows(buyRect, mouse);
        DrawSellRows(sellRect, mouse);

        DrawFooter(rect);
        _sb.End();
    }

    private void HandleLeftClick(Point mouse)
    {
        var window = GetWindowRect();
        if (GetCloseRect(window).Contains(mouse))
        {
            Close();
            return;
        }

        foreach (var row in BuildBuyRows(GetBuyListRect(window)))
        {
            if (!row.Rect.Contains(mouse))
                continue;

            Buy(row.Entry);
            return;
        }

        foreach (var row in BuildSellRows(GetSellListRect(window)))
        {
            if (!row.Rect.Contains(mouse))
                continue;

            Sell(row.Candidate);
            return;
        }
    }

    private void HandleScroll()
    {
        if (_input.ScrollDelta == 0)
            return;

        var mouse = _input.MousePosition;
        var window = GetWindowRect();
        var buyRect = GetBuyListRect(window);
        var sellRect = GetSellListRect(window);
        var delta = _input.ScrollDelta > 0 ? -1 : 1;

        if (buyRect.Contains(mouse))
        {
            var max = Math.Max(0, GetBuyEntries().Count - GetVisibleRowCount(buyRect));
            _buyScroll = Math.Clamp(_buyScroll + delta, 0, max);
        }
        else if (sellRect.Contains(mouse))
        {
            var max = Math.Max(0, GetSellCandidates().Count - GetVisibleRowCount(sellRect));
            _sellScroll = Math.Clamp(_sellScroll + delta, 0, max);
        }
    }

    private void Buy(ShopStockEntry entry)
    {
        if (_buyer == null || _shopEntity == null || _shop == null || !EnsureRuntimeServices())
            return;

        var proto = _prototypes.GetEntity(entry.PrototypeId);
        if (proto == null)
        {
            PopupTextSystem.Show(_buyer, "Неизвестный товар", Color.IndianRed);
            return;
        }

        var price = GetMerchantBuyPrice(proto);
        var currency = _buyer.GetComponent<CurrencyComponent>() ?? _buyer.AddComponent(new CurrencyComponent());
        if (!currency.CanAfford(price))
        {
            PopupTextSystem.Show(_buyer, "Не хватает денег", Color.IndianRed);
            return;
        }

        var position = _buyer.GetComponent<TransformComponent>()?.Position
                       ?? _shopEntity.GetComponent<TransformComponent>()?.Position
                       ?? Vector2.Zero;
        var itemEntity = _factory.CreateFromPrototype(proto, position);
        if (itemEntity == null)
            return;

        if (!TryGiveItemToBuyer(itemEntity))
        {
            World.DestroyEntity(itemEntity);
            PopupTextSystem.Show(_buyer, "Некуда положить товар", Color.IndianRed);
            return;
        }

        currency.TrySpend(price);
        var sellerCurrency = _shopEntity.GetComponent<CurrencyComponent>()
                             ?? _shopEntity.AddComponent(new CurrencyComponent());
        sellerCurrency.Add(price);
        _shop.TryTakeOne(entry);
        MarkDirty();

        var itemName = itemEntity.GetComponent<ItemComponent>()?.ItemName ?? proto.Name;
        PopupTextSystem.Show(_buyer, $"-{price} {currency.Symbol}: {itemName}", Color.Gold, lifetime: 1.4f);
    }

    private void Sell(SellCandidate candidate)
    {
        if (_buyer == null || _shop == null)
            return;

        if (!DetachSoldItem(candidate))
            return;

        var currency = _buyer.GetComponent<CurrencyComponent>() ?? _buyer.AddComponent(new CurrencyComponent());
        currency.Add(candidate.Price);

        _shop.AddStock(
            candidate.Prototype.Id,
            candidate.Quantity,
            GetMerchantBuyPrice(candidate.Prototype),
            ShopPricing.GetQualityTier(candidate.Prototype),
            string.IsNullOrWhiteSpace(candidate.Prototype.Name) ? candidate.Prototype.Id : candidate.Prototype.Name);

        candidate.Item.Active = false;
        if (candidate.Item.GetComponent<ItemComponent>() is { } item)
            item.ContainedIn = null;
        World.DestroyEntity(candidate.Item);
        MarkDirty();

        PopupTextSystem.Show(_buyer, $"+{candidate.Price} {currency.Symbol}: {FormatCandidateName(candidate)}", Color.Gold, lifetime: 1.4f);
    }

    private bool TryGiveItemToBuyer(Entity itemEntity)
    {
        if (_buyer == null)
            return false;

        var hands = _buyer.GetComponent<HandsComponent>();
        if (hands?.HasFreeHandFor(itemEntity) == true && hands.TryPickUp(itemEntity))
            return true;

        foreach (var storage in EnumerateBuyerStorages())
        {
            if (storage.CanInsert(itemEntity) && storage.TryInsert(itemEntity))
                return true;
        }

        return false;
    }

    private IEnumerable<StorageComponent> EnumerateBuyerStorages()
    {
        if (_buyer == null)
            yield break;

        if (_buyer.GetComponent<StorageComponent>() is { } ownStorage)
            yield return ownStorage;

        var hands = _buyer.GetComponent<HandsComponent>();
        if (hands != null)
        {
            foreach (var hand in hands.Hands)
            {
                if (hand.HeldItem?.GetComponent<StorageComponent>() is { } storage)
                    yield return storage;
            }
        }

        var equipment = _buyer.GetComponent<EquipmentComponent>();
        if (equipment != null)
        {
            foreach (var slot in equipment.Slots)
            {
                if (slot.Item?.GetComponent<StorageComponent>() is { } storage)
                    yield return storage;
            }
        }
    }

    private bool DetachSoldItem(SellCandidate candidate)
    {
        if (_buyer == null)
            return false;

        if (candidate.SourceKind == SellSourceKind.Hand)
            return _buyer.GetComponent<HandsComponent>()?.RemoveFromHand(candidate.Item) == true;

        if (candidate.SourceKind == SellSourceKind.Equipment)
            return _buyer.GetComponent<EquipmentComponent>()?.RemoveEquipped(candidate.Item) == true;

        if (candidate.SourceKind == SellSourceKind.Storage)
        {
            if (candidate.FromStorage == null)
                return false;

            if (!candidate.FromStorage.Contents.Remove(candidate.Item))
                return false;

            if (candidate.Item.GetComponent<ItemComponent>() is { } item)
                item.ContainedIn = null;

            MarkDirty();
            return true;
        }

        return false;
    }

    private List<ShopStockEntry> GetBuyEntries()
    {
        if (_shop == null || !EnsureRuntimeServices())
            return new List<ShopStockEntry>();

        return _shop.Stock
            .Where(entry => entry.Quantity > 0 && _prototypes.GetEntity(entry.PrototypeId) != null)
            .OrderBy(entry => entry.QualityTier)
            .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private List<SellCandidate> GetSellCandidates()
    {
        var result = new List<SellCandidate>();
        if (_buyer == null)
            return result;

        var acceptedTags = ResolveAcceptedSellTags();
        var visited = new HashSet<Entity>();

        if (_buyer.GetComponent<StorageComponent>() is { } ownStorage)
        {
            foreach (var item in ownStorage.Contents.ToList())
                AddSellCandidate(result, item, SellSourceKind.Storage, ownStorage, "Инвентарь", acceptedTags, visited);
        }

        var hands = _buyer.GetComponent<HandsComponent>();
        if (hands != null)
        {
            for (var i = 0; i < hands.Hands.Count; i++)
            {
                var item = hands.Hands[i].HeldItem;
                if (item != null)
                    AddSellCandidate(result, item, SellSourceKind.Hand, null, $"Рука {i + 1}", acceptedTags, visited);
            }
        }

        var equipment = _buyer.GetComponent<EquipmentComponent>();
        if (equipment != null)
        {
            foreach (var slot in equipment.Slots)
            {
                if (slot.Item != null)
                    AddSellCandidate(result, slot.Item, SellSourceKind.Equipment, null, slot.DisplayName, acceptedTags, visited);
            }
        }

        return result
            .OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void AddSellCandidate(
        List<SellCandidate> result,
        Entity item,
        SellSourceKind sourceKind,
        StorageComponent? fromStorage,
        string sourceLabel,
        HashSet<string>? acceptedTags,
        HashSet<Entity> visited)
    {
        if (!visited.Add(item))
            return;

        var itemComponent = item.GetComponent<ItemComponent>();
        if (itemComponent == null)
            return;

        if (item.GetComponent<StorageComponent>() is { } storage && storage.Contents.Count > 0)
        {
            var nestedSource = CombineSourceLabel(sourceLabel, itemComponent.ItemName);
            foreach (var nestedItem in storage.Contents.ToList())
                AddSellCandidate(result, nestedItem, SellSourceKind.Storage, storage, nestedSource, acceptedTags, visited);
            return;
        }

        if (!EnsureRuntimeServices())
            return;

        if (item.GetComponent<CurrencyComponent>() != null)
            return;

        var proto = _prototypes.GetEntity(item.PrototypeId);
        if (proto == null)
            return;

        if (!MerchantBuys(proto, acceptedTags))
            return;

        var quantity = Math.Max(1, itemComponent.StackCount);
        var name = itemComponent.ItemName;
        result.Add(new SellCandidate(
            item,
            proto,
            sourceKind,
            fromStorage,
            sourceLabel,
            string.IsNullOrWhiteSpace(name) ? proto.Name : name,
            quantity,
            GetMerchantSellPrice(proto) * quantity));
    }

    private IEnumerable<BuyRow> BuildBuyRows(Rectangle listRect)
    {
        var entries = GetBuyEntries();
        var count = GetVisibleRowCount(listRect);
        var start = Math.Clamp(_buyScroll, 0, Math.Max(0, entries.Count - count));
        var y = listRect.Y + 28;

        foreach (var entry in entries.Skip(start).Take(count))
        {
            yield return new BuyRow(
                new Rectangle(listRect.X + 4, y, listRect.Width - 8, RowHeight - 2),
                entry);
            y += RowHeight;
        }
    }

    private IEnumerable<SellRow> BuildSellRows(Rectangle listRect)
    {
        var candidates = GetSellCandidates();
        var count = GetVisibleRowCount(listRect);
        var start = Math.Clamp(_sellScroll, 0, Math.Max(0, candidates.Count - count));
        var y = listRect.Y + 28;

        foreach (var candidate in candidates.Skip(start).Take(count))
        {
            yield return new SellRow(
                new Rectangle(listRect.X + 4, y, listRect.Width - 8, RowHeight - 2),
                candidate);
            y += RowHeight;
        }
    }

    private void DrawHeader(Rectangle rect)
    {
        var merchant = _shopEntity != null ? ResolveEntityName(_shopEntity) : "Trader";
        var currency = _buyer?.GetComponent<CurrencyComponent>();
        var money = currency?.GetDisplayText() ?? "0 мон.";

        DrawStringWithShadow(merchant, new Vector2(rect.X + PanelPadding, rect.Y + 10), Color.LimeGreen);
        var moneySize = _font!.MeasureString(money);
        DrawStringWithShadow(money, new Vector2(rect.Right - PanelPadding - 28 - moneySize.X, rect.Y + 10), Color.Gold);
    }

    private void DrawSectionTitle(Rectangle rect, string title)
    {
        _sb.Draw(_pixel!, rect, new Color(12, 16, 20, 220));
        DrawBorder(rect, Color.White * 0.18f);
        DrawStringWithShadow(title, new Vector2(rect.X + 8, rect.Y + 6), Color.Cyan);
    }

    private void DrawBuyRows(Rectangle listRect, Point mouse)
    {
        var rows = BuildBuyRows(listRect).ToList();
        if (rows.Count == 0)
        {
            DrawStringWithShadow("[пусто]", new Vector2(listRect.X + 10, listRect.Y + 38), Color.Gray);
            return;
        }

        foreach (var row in rows)
        {
            var hovered = row.Rect.Contains(mouse);
            DrawRowBackground(row.Rect, hovered);
            var entry = row.Entry;
            var proto = _prototypes.GetEntity(entry.PrototypeId);
            var title = string.IsNullOrWhiteSpace(entry.DisplayName)
                ? proto?.Name ?? entry.PrototypeId
                : entry.DisplayName;
            var left = $"{title} x{entry.Quantity}";
            var tier = entry.QualityTier is >= 1 and <= 4 ? $"T{entry.QualityTier}" : "";
            var right = $"{tier}  {GetMerchantBuyPrice(proto!)} мон.";
            DrawRowText(row.Rect, left, right, hovered ? Color.White : new Color(220, 230, 220));
        }
    }

    private void DrawSellRows(Rectangle listRect, Point mouse)
    {
        var rows = BuildSellRows(listRect).ToList();
        if (rows.Count == 0)
        {
            DrawStringWithShadow("[нечего продать]", new Vector2(listRect.X + 10, listRect.Y + 38), Color.Gray);
            return;
        }

        foreach (var row in rows)
        {
            var hovered = row.Rect.Contains(mouse);
            DrawRowBackground(row.Rect, hovered);
            var candidate = row.Candidate;
            var left = $"{FormatCandidateName(candidate)} ({candidate.SourceLabel})";
            var right = $"{candidate.Price} мон.";
            DrawRowText(row.Rect, left, right, hovered ? Color.White : new Color(220, 230, 220));
        }
    }

    private void DrawFooter(Rectangle rect)
    {
        var hint = "ЛКМ: купить / продать  |  колесо: список  |  Esc: закрыть";
        DrawStringWithShadow(hint, new Vector2(rect.X + PanelPadding, rect.Bottom - FooterHeight + 6), Color.Gray);
    }

    private void DrawRowBackground(Rectangle rect, bool hovered)
    {
        _sb.Draw(_pixel!, rect, hovered ? new Color(54, 86, 62, 230) : new Color(26, 34, 36, 210));
        _sb.Draw(_pixel!, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), Color.White * 0.08f);
    }

    private void DrawRowText(Rectangle rect, string left, string right, Color color)
    {
        var rightSize = _font!.MeasureString(right);
        DrawStringWithShadow(Fit(left, rect.Width - (int)rightSize.X - 28), new Vector2(rect.X + 8, rect.Y + 7), color);
        DrawStringWithShadow(right, new Vector2(rect.Right - rightSize.X - 8, rect.Y + 7), Color.Gold);
    }

    private void DrawButton(Rectangle rect, string text, bool hovered)
    {
        _sb.Draw(_pixel!, rect, hovered ? new Color(150, 60, 60) : new Color(82, 36, 36));
        DrawBorder(rect, Color.White * 0.28f);
        var size = _font!.MeasureString(text);
        DrawStringWithShadow(text, new Vector2(rect.Center.X - size.X * 0.5f, rect.Center.Y - size.Y * 0.5f), Color.White);
    }

    private void DrawShadowedPanel(Rectangle rect)
    {
        _sb.Draw(_pixel!, new Rectangle(rect.X + 5, rect.Y + 5, rect.Width, rect.Height), Color.Black * 0.45f);
        _sb.Draw(_pixel!, rect, new Color(17, 22, 26, 244));
        DrawBorder(rect, new Color(84, 130, 92));
        _sb.Draw(_pixel!, new Rectangle(rect.X + 1, rect.Y + HeaderHeight, rect.Width - 2, 1), Color.White * 0.12f);
    }

    private void DrawBorder(Rectangle rect, Color color)
    {
        _sb.Draw(_pixel!, new Rectangle(rect.X, rect.Y, rect.Width, 1), color);
        _sb.Draw(_pixel!, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), color);
        _sb.Draw(_pixel!, new Rectangle(rect.X, rect.Y, 1, rect.Height), color);
        _sb.Draw(_pixel!, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), color);
    }

    private void DrawStringWithShadow(string text, Vector2 pos, Color color)
    {
        if (string.IsNullOrEmpty(text))
            return;

        _sb.DrawString(_font!, text, pos + new Vector2(1f, 1f), Color.Black * 0.75f);
        _sb.DrawString(_font!, text, pos, color);
    }

    private string Fit(string text, int maxWidth)
    {
        if (_font == null || maxWidth <= 0)
            return "";

        if (_font.MeasureString(text).X <= maxWidth)
            return text;

        const string ellipsis = "...";
        var trimmed = text;
        while (trimmed.Length > 0)
        {
            trimmed = trimmed[..^1];
            if (_font.MeasureString(trimmed + ellipsis).X <= maxWidth)
                return trimmed + ellipsis;
        }

        return ellipsis;
    }

    private Rectangle GetWindowRect()
    {
        var vp = _gd.Viewport.Bounds;
        var width = Math.Clamp(vp.Width - 32, 560, 820);
        var height = Math.Clamp(vp.Height - 48, 360, 540);
        return new Rectangle((vp.Width - width) / 2, (vp.Height - height) / 2, width, height);
    }

    private Rectangle GetCloseRect(Rectangle window)
        => new(window.Right - PanelPadding - 22, window.Y + 9, 22, 22);

    private Rectangle GetBuyListRect(Rectangle window)
    {
        var contentTop = window.Y + HeaderHeight + PanelPadding;
        var contentHeight = window.Height - HeaderHeight - FooterHeight - PanelPadding * 2;
        var columnWidth = (window.Width - PanelPadding * 2 - Gap) / 2;
        return new Rectangle(window.X + PanelPadding, contentTop, columnWidth, contentHeight);
    }

    private Rectangle GetSellListRect(Rectangle window)
    {
        var buy = GetBuyListRect(window);
        return new Rectangle(buy.Right + Gap, buy.Y, buy.Width, buy.Height);
    }

    private static int GetVisibleRowCount(Rectangle listRect)
        => Math.Max(1, (listRect.Height - 32) / RowHeight);

    private bool ShouldCloseByDistance()
    {
        if (_buyer == null || _shopEntity == null)
            return true;

        var buyerTf = _buyer.GetComponent<TransformComponent>();
        var shopTf = _shopEntity.GetComponent<TransformComponent>();
        if (buyerTf == null || shopTf == null)
            return false;

        return Vector2.Distance(buyerTf.Position, shopTf.Position) > 96f;
    }

    private static string ResolveEntityName(Entity entity)
    {
        var identity = entity.GetComponent<IdentityComponent>();
        if (identity != null && !string.IsNullOrWhiteSpace(identity.FullName))
            return identity.FullName;

        var interactable = entity.GetComponent<InteractableComponent>();
        if (!string.IsNullOrWhiteSpace(interactable?.DisplayName))
            return interactable.DisplayName;

        return entity.GetComponent<ItemComponent>()?.ItemName ?? entity.Name;
    }

    private void EnsurePixel()
    {
        if (_pixel != null)
            return;

        _pixel = new Texture2D(_gd, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }

    private bool EnsureDrawServices()
    {
        if (_sb == null && ServiceLocator.Has<SpriteBatch>())
            _sb = ServiceLocator.Get<SpriteBatch>();
        if (_gd == null && ServiceLocator.Has<GraphicsDevice>())
            _gd = ServiceLocator.Get<GraphicsDevice>();

        return _sb != null && _gd != null;
    }

    private bool EnsureRuntimeServices()
    {
        if (_prototypes == null && ServiceLocator.Has<PrototypeManager>())
            _prototypes = ServiceLocator.Get<PrototypeManager>();
        if (_factory == null && ServiceLocator.Has<EntityFactory>())
            _factory = ServiceLocator.Get<EntityFactory>();
        if (_mapManager == null && ServiceLocator.Has<MapManager>())
            _mapManager = ServiceLocator.Get<MapManager>();
        if (_registry == null && ServiceLocator.Has<WorldRegistry>())
            _registry = ServiceLocator.Get<WorldRegistry>();
        if (_professionCatalog == null && ServiceLocator.Has<ProfessionCatalog>())
            _professionCatalog = ServiceLocator.Get<ProfessionCatalog>();

        return _prototypes != null && _factory != null;
    }

    private MapData? ResolveMarketMap()
        => _mapManager?.CurrentMap;

    private int GetMerchantBuyPrice(EntityPrototype proto)
    {
        var price = ShopPricing.GetBuyPrice(proto, ResolveMarketMap());
        return Math.Max(1, (int)MathF.Ceiling(price * ResolveMerchantRevengeMultiplier()));
    }

    private int GetMerchantSellPrice(EntityPrototype proto)
    {
        var price = ShopPricing.GetSellPrice(proto, ResolveMarketMap());
        return Math.Max(1, (int)MathF.Floor(price / ResolveMerchantRevengeMultiplier()));
    }

    private float ResolveMerchantRevengeMultiplier()
    {
        if (_buyer?.HasComponent<PlayerTagComponent>() != true)
            return 1f;

        var merchant = ResolveMerchantOwner();
        var revenge = merchant?.GetComponent<RevengeTriggerComponent>();
        if (revenge == null)
            return 1f;

        return revenge.Triggers.Any(IsReadyMerchantPenaltyAgainstBuyer)
            ? RevengeMerchantPenaltyMultiplier
            : 1f;
    }

    private Entity? ResolveMerchantOwner()
    {
        if (_shopEntity == null)
            return null;

        if (_shopEntity.HasComponent<NpcTagComponent>())
            return _shopEntity;

        if (_shop == null || string.IsNullOrWhiteSpace(_shop.OwnerNpcSaveId))
            return _shopEntity;

        return World.GetEntitiesWith<SaveEntityIdComponent>()
                   .FirstOrDefault(entity => string.Equals(
                       entity.GetComponent<SaveEntityIdComponent>()?.SaveId,
                       _shop.OwnerNpcSaveId,
                       StringComparison.OrdinalIgnoreCase))
               ?? _shopEntity;
    }

    private bool IsReadyMerchantPenaltyAgainstBuyer(RevengeTrigger trigger)
    {
        if (!trigger.Ready || trigger.Behavior != RevengeBehavior.MerchantPenalty)
            return false;

        var buyerSaveId = _buyer?.GetComponent<SaveEntityIdComponent>()?.SaveId ?? "";
        return string.IsNullOrWhiteSpace(trigger.KillerSaveId)
               || string.Equals(trigger.KillerSaveId, buyerSaveId, StringComparison.OrdinalIgnoreCase);
    }

    private HashSet<string>? ResolveAcceptedSellTags()
    {
        if (!EnsureRuntimeServices())
            return null;

        var profession = ResolveMerchantProfession();
        if (profession?.TradeTags == null || profession.TradeTags.Count == 0)
            return null;

        return new HashSet<string>(profession.TradeTags, StringComparer.OrdinalIgnoreCase);
    }

    private ProfessionDefinition? ResolveMerchantProfession()
    {
        if (_shopEntity == null || _shop == null || _professionCatalog == null)
            return null;

        var professionId = _shopEntity.GetComponent<ProfessionComponent>()?.ProfessionId;
        if (string.IsNullOrWhiteSpace(professionId)
            && !string.IsNullOrWhiteSpace(_shop.ProfessionSlotId)
            && _registry?.Professions.TryGetValue(_shop.ProfessionSlotId, out var slot) == true)
        {
            professionId = slot.ProfessionId;
        }

        return _professionCatalog.Get(professionId);
    }

    private static bool MerchantBuys(EntityPrototype proto, HashSet<string>? acceptedTags)
    {
        if (acceptedTags == null || acceptedTags.Count == 0)
            return true;

        return ShopPricing.GetTags(proto).Any(acceptedTags.Contains);
    }

    private static string CombineSourceLabel(string sourceLabel, string containerName)
    {
        if (string.IsNullOrWhiteSpace(containerName))
            return sourceLabel;

        return string.IsNullOrWhiteSpace(sourceLabel)
            ? containerName
            : $"{sourceLabel} / {containerName}";
    }

    private static string FormatCandidateName(SellCandidate candidate)
        => candidate.Quantity > 1 ? $"{candidate.Name} x{candidate.Quantity}" : candidate.Name;

    private static void MarkDirty()
    {
        if (ServiceLocator.Has<IWorldStateTracker>())
            ServiceLocator.Get<IWorldStateTracker>().MarkDirty();
    }

    private sealed record BuyRow(Rectangle Rect, ShopStockEntry Entry);
    private sealed record SellRow(Rectangle Rect, SellCandidate Candidate);
    private sealed record SellCandidate(Entity Item, EntityPrototype Prototype, SellSourceKind SourceKind, StorageComponent? FromStorage, string SourceLabel, string Name, int Quantity, int Price);

    private enum SellSourceKind
    {
        Hand,
        Equipment,
        Storage
    }
}
