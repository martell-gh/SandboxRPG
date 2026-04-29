using Microsoft.Xna.Framework;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.World;

namespace MTEngine.Npc;

public class ShopRestockSystem : GameSystem
{
    private readonly Random _rng = new();
    private EventBus _bus = null!;
    private GameClock? _clock;
    private PrototypeManager? _prototypes;
    private MapManager? _mapManager;
    private WorldRegistry? _registry;
    private ProfessionCatalog? _catalog;
    private bool _refreshRequested = true;

    public override void OnInitialize()
    {
        _bus = ServiceLocator.Get<EventBus>();
        _bus.Subscribe<MapLoadedEvent>(OnMapLoaded);
        _bus.Subscribe<DayChanged>(OnDayChanged);
    }

    public override void OnDestroy()
    {
        _bus.Unsubscribe<MapLoadedEvent>(OnMapLoaded);
        _bus.Unsubscribe<DayChanged>(OnDayChanged);
    }

    public override void Update(float deltaTime)
    {
        if (!_refreshRequested)
            return;

        _refreshRequested = false;
        RefreshActiveShops(forceRestock: false);
    }

    public void RestockNow(Entity shopEntity)
    {
        if (!EnsureServices())
            return;

        var shop = shopEntity.GetComponent<ShopComponent>();
        if (shop == null)
            return;

        Restock(shopEntity, shop, force: true);
    }

    public void RefreshShop(Entity shopEntity)
    {
        if (!EnsureServices())
            return;

        var shop = shopEntity.GetComponent<ShopComponent>();
        if (shop == null)
            return;

        Restock(shopEntity, shop, force: false);
    }

    private void OnMapLoaded(MapLoadedEvent _) => _refreshRequested = true;

    private void OnDayChanged(DayChanged _)
        => RefreshActiveShops(forceRestock: false);

    private void RefreshActiveShops(bool forceRestock)
    {
        if (!EnsureServices())
            return;

        EnsureTraderNpcShops();

        foreach (var entity in World.GetEntitiesWith<ShopComponent>())
        {
            if (entity.HasComponent<NpcTagComponent>() && !NpcLod.IsActiveOrBackground(entity))
                continue;

            var shop = entity.GetComponent<ShopComponent>()!;
            Restock(entity, shop, forceRestock);
        }
    }

    private void EnsureTraderNpcShops()
    {
        foreach (var entity in World.GetEntitiesWith<NpcTagComponent, ProfessionComponent>())
        {
            if (!NpcLod.IsActiveOrBackground(entity))
                continue;

            if (entity.GetComponent<MTEngine.Components.HealthComponent>()?.IsDead == true)
                continue;

            var profession = entity.GetComponent<ProfessionComponent>()!;
            if (string.IsNullOrWhiteSpace(profession.ProfessionId))
                continue;

            var definition = _catalog!.Get(profession.ProfessionId);
            if (definition?.IsTrader != true)
                continue;

            var shop = entity.GetComponent<ShopComponent>() ?? entity.AddComponent(new ShopComponent());
            if (string.IsNullOrWhiteSpace(shop.ProfessionSlotId))
                shop.ProfessionSlotId = profession.SlotId;
            if (string.IsNullOrWhiteSpace(shop.OwnerNpcSaveId))
                shop.OwnerNpcSaveId = entity.GetComponent<SaveEntityIdComponent>()?.SaveId ?? "";
        }
    }

    private void Restock(Entity shopEntity, ShopComponent shop, bool force)
    {
        var today = _clock!.DayIndex;
        var profession = ResolveProfession(shopEntity, shop);
        if (profession == null || !profession.IsTrader)
            return;

        if (!force && shop.Stock.Count > 0 && shop.NextRestockDayIndex > today)
            return;

        var owner = ResolveOwner(shopEntity, shop);
        var skill = owner?.GetComponent<SkillsComponent>()?.Get(profession.PrimarySkill) ?? 0f;
        var candidates = BuildCandidates(profession, skill);
        if (candidates.Count == 0)
            return;

        var amount = ResolveStockAmount(profession, skill);
        var nextStock = new Dictionary<(string ProtoId, int Price), ShopStockEntry>();

        for (var i = 0; i < amount; i++)
        {
            var candidate = candidates[_rng.Next(candidates.Count)];
            var price = ShopPricing.GetBuyPrice(candidate.Prototype, _mapManager?.CurrentMap);
            var key = (candidate.Prototype.Id, price);

            if (!nextStock.TryGetValue(key, out var entry))
            {
                entry = new ShopStockEntry
                {
                    PrototypeId = candidate.Prototype.Id,
                    DisplayName = string.IsNullOrWhiteSpace(candidate.Prototype.Name)
                        ? candidate.Prototype.Id
                        : candidate.Prototype.Name,
                    QualityTier = candidate.QualityTier,
                    UnitPrice = price
                };
                nextStock[key] = entry;
            }

            entry.Quantity++;
        }

        shop.Stock = nextStock.Values
            .OrderBy(entry => entry.QualityTier)
            .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.UnitPrice)
            .ToList();
        shop.NextRestockDayIndex = today + Math.Max(1, profession.RestockEveryDays);

        if (ServiceLocator.Has<IWorldStateTracker>())
            ServiceLocator.Get<IWorldStateTracker>().MarkDirty();
    }

    private ProfessionDefinition? ResolveProfession(Entity shopEntity, ShopComponent shop)
    {
        var professionId = shopEntity.GetComponent<ProfessionComponent>()?.ProfessionId;
        if (string.IsNullOrWhiteSpace(professionId)
            && !string.IsNullOrWhiteSpace(shop.ProfessionSlotId)
            && _registry!.Professions.TryGetValue(shop.ProfessionSlotId, out var slot))
        {
            professionId = slot.ProfessionId;
        }

        return _catalog!.Get(professionId);
    }

    private Entity? ResolveOwner(Entity shopEntity, ShopComponent shop)
    {
        if (shopEntity.HasComponent<NpcTagComponent>())
            return shopEntity;

        var ownerSaveId = shop.OwnerNpcSaveId;
        if (string.IsNullOrWhiteSpace(ownerSaveId)
            && !string.IsNullOrWhiteSpace(shop.ProfessionSlotId)
            && _registry!.Professions.TryGetValue(shop.ProfessionSlotId, out var slot))
        {
            ownerSaveId = slot.OccupiedNpcSaveId ?? "";
        }

        if (string.IsNullOrWhiteSpace(ownerSaveId))
            return null;

        return World.GetEntitiesWith<SaveEntityIdComponent>()
            .FirstOrDefault(entity => string.Equals(
                entity.GetComponent<SaveEntityIdComponent>()?.SaveId,
                ownerSaveId,
                StringComparison.OrdinalIgnoreCase));
    }

    private List<StockCandidate> BuildCandidates(ProfessionDefinition profession, float skill)
    {
        var tags = new HashSet<string>(profession.TradeTags, StringComparer.OrdinalIgnoreCase);
        if (tags.Count == 0)
            return new List<StockCandidate>();

        var bestAllowedTier = ResolveBestAllowedTier(skill);
        return _prototypes!.GetAllEntities()
            .Where(proto => proto.Components?["item"] != null)
            .Select(proto => new StockCandidate(proto, ShopPricing.GetQualityTier(proto), ShopPricing.GetTags(proto).ToList()))
            .Where(candidate => candidate.QualityTier >= bestAllowedTier)
            .Where(candidate => candidate.Tags.Any(tags.Contains))
            .ToList();
    }

    private static int ResolveBestAllowedTier(float skill)
    {
        skill = Math.Clamp(skill, 0f, 10f);
        if (skill >= 8f) return 1;
        if (skill >= 5f) return 2;
        if (skill >= 2.5f) return 3;
        return 4;
    }

    private static int ResolveStockAmount(ProfessionDefinition profession, float skill)
    {
        var min = Math.Max(1, Math.Min(profession.StockSizeMin, profession.StockSizeMax));
        var max = Math.Max(min, Math.Max(profession.StockSizeMin, profession.StockSizeMax));
        var t = Math.Clamp(skill / 10f, 0f, 1f);
        return Math.Clamp((int)MathF.Round(MathHelper.Lerp(min, max, t)), min, max);
    }

    private bool EnsureServices()
    {
        _clock ??= ServiceLocator.Has<GameClock>() ? ServiceLocator.Get<GameClock>() : null;
        _prototypes ??= ServiceLocator.Has<PrototypeManager>() ? ServiceLocator.Get<PrototypeManager>() : null;
        _mapManager ??= ServiceLocator.Has<MapManager>() ? ServiceLocator.Get<MapManager>() : null;
        _registry ??= ServiceLocator.Has<WorldRegistry>() ? ServiceLocator.Get<WorldRegistry>() : null;
        _catalog ??= ServiceLocator.Has<ProfessionCatalog>() ? ServiceLocator.Get<ProfessionCatalog>() : null;
        return _clock != null && _prototypes != null && _registry != null && _catalog != null;
    }

    private sealed record StockCandidate(EntityPrototype Prototype, int QualityTier, List<string> Tags);
}
