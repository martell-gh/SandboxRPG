using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.World;

namespace MTEngine.Npc;

/// <summary>
/// §10.3: при возвращении игрока в карту, которой не было в Active дольше дня, прокатываем
/// "тихие" дневные тики, пропущенные пока NPC были вне зоны симуляции.
///
/// Покрытие первой итерации:
///   • прибавка <c>SkillGainPerDay × daysElapsed</c> по primarySkill каждой профессии
///     (то, что обычно делал бы <see cref="ProfessionTickSystem"/> в Distant).
///   • публикация события <see cref="MapCatchUpRan"/> для подписчиков (ShopRestock, будущие
///     системы экономики/мести).
///
/// Что осознанно не делаем здесь и оставляем под автоматическую LOD-промоцию NPC:
///   распределение свадеб/рождений в Distant-batch, телепорт переездов между поселениями.
///   `WorldPopulationStore.Live()/Snapshot()` уже готовы и могут использоваться сверху.
/// </summary>
[SaveObject("worldCatchup")]
public class WorldCatchupSystem : GameSystem
{
    [SaveField("lastSeenByMap")]
    public Dictionary<string, long> LastSeenByMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    private EventBus _bus = null!;
    private GameClock? _clock;
    private ProfessionCatalog? _catalog;
    private bool _subscribed;

    // MapLoadedEvent доходит до нас раньше, чем MapEntitySpawner/NpcRosterSpawner спаунят
    // entities (мы подписываемся в OnInitialize, они — позже в Game1.LoadContent).
    // Поэтому при загрузке карты копим pending-задачу и проигрываем её в следующем Update,
    // когда сущности уже в мире.
    private string _pendingMapId = "";
    private long _pendingDays;
    private bool _pendingFlag;

    public override void OnInitialize()
    {
        _bus = ServiceLocator.Get<EventBus>();
        if (!_subscribed)
        {
            _bus.Subscribe<MapLoadedEvent>(OnMapLoaded);
            _subscribed = true;
        }
    }

    public override void Update(float deltaTime)
    {
        if (!_pendingFlag) return;
        _pendingFlag = false;

        if (string.IsNullOrWhiteSpace(_pendingMapId)) return;
        var mapId = _pendingMapId;
        var days = _pendingDays;
        var today = _clock?.DayIndex ?? 0L;

        if (days > 0)
        {
            ApplyProfessionSkillCatchUp(days);
            _bus.Publish(new MapCatchUpRan(mapId, days, today));
        }
    }

    public override void OnDestroy()
    {
        if (_subscribed)
        {
            _bus.Unsubscribe<MapLoadedEvent>(OnMapLoaded);
            _subscribed = false;
        }
    }

    private void OnMapLoaded(MapLoadedEvent ev)
    {
        if (!EnsureServices()) return;
        var mapId = ev.Map?.Id ?? "";
        if (string.IsNullOrWhiteSpace(mapId)) return;

        var today = _clock!.DayIndex;

        if (!LastSeenByMap.TryGetValue(mapId, out var lastSeen))
        {
            // Первая встреча с картой — фиксируем точку отсчёта без catch-up
            // (NPC появились "сейчас", не имеет смысла фантомно их прокачивать).
            LastSeenByMap[mapId] = today;
            return;
        }

        var days = today - lastSeen;
        LastSeenByMap[mapId] = today;
        if (days <= 0) return;

        // Отложенный запуск: NPC будут заспаунены MapEntitySpawner-ом позже в этом же
        // событийном проходе, а ApplyProfessionSkillCatchUp бежит уже на оживших NPC.
        _pendingMapId = mapId;
        _pendingDays = days;
        _pendingFlag = true;
    }

    private void ApplyProfessionSkillCatchUp(long days)
    {
        if (_catalog == null) return;

        foreach (var entity in World.GetEntitiesWith<NpcTagComponent, ProfessionComponent>())
        {
            if (!entity.Active) continue;

            var profession = entity.GetComponent<ProfessionComponent>()!;
            if (string.IsNullOrWhiteSpace(profession.ProfessionId)) continue;

            // Не догоняем детей и пенсионеров — они не качаются ProfessionTickSystem-ом.
            if (entity.HasComponent<ChildGrowthComponent>()) continue;
            var age = entity.GetComponent<AgeComponent>();
            if (age?.IsPensioner == true) continue;

            var def = _catalog.Get(profession.ProfessionId);
            if (def == null || string.IsNullOrWhiteSpace(def.PrimarySkill) || def.SkillGainPerDay <= 0f)
                continue;

            var skills = entity.GetComponent<SkillsComponent>() ?? entity.AddComponent(new SkillsComponent());
            skills.Add(def.PrimarySkill, def.SkillGainPerDay * days);
        }
    }

    private bool EnsureServices()
    {
        _clock ??= ServiceLocator.Has<GameClock>() ? ServiceLocator.Get<GameClock>() : null;
        _catalog ??= ServiceLocator.Has<ProfessionCatalog>() ? ServiceLocator.Get<ProfessionCatalog>() : null;
        return _clock != null;
    }
}
