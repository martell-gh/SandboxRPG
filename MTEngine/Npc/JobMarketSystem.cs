using MTEngine.Core;
using MTEngine.ECS;

namespace MTEngine.Npc;

/// <summary>
/// §7.3: подбор работы. Раз в день и по сигналам (новый совершеннолетний / освобождение
/// слота) перебирает свободные `ProfessionSlotDef` в каждом поселении и закрывает их
/// лучшим по primarySkill кандидатом из числа взрослых безработных НЕ-пенсионеров.
/// </summary>
public class JobMarketSystem : GameSystem
{
    private const int MinWorkingAge = 18;
    private const string AdultDefaultTemplate = "default_unemployed";
    private const string WorkerTemplate = "default_worker";

    private EventBus _bus = null!;
    private GameClock? _clock;
    private WorldRegistry? _registry;
    private ProfessionCatalog? _catalog;
    private ScheduleTemplates? _templates;
    private bool _scheduledRetry;

    public override void OnInitialize()
    {
        _bus = ServiceLocator.Get<EventBus>();
        _bus.Subscribe<DayChanged>(OnDayChanged);
        // Когда NPC погиб, вакансия может освободиться — пробуем подобрать заново.
        _bus.Subscribe<EntityDied>(OnEntityDied);
    }

    public override void Update(float deltaTime)
    {
        if (!_scheduledRetry) return;
        _scheduledRetry = false;
        TryFillVacancies();
    }

    public override void OnDestroy()
    {
        _bus.Unsubscribe<DayChanged>(OnDayChanged);
        _bus.Unsubscribe<EntityDied>(OnEntityDied);
    }

    private void OnDayChanged(DayChanged evt) => TryFillVacancies();

    private void OnEntityDied(EntityDied ev)
    {
        // Освободим занятую вакансию умершего, если знали про него.
        if (!EnsureServices()) return;

        var marker = ev.Victim.GetComponent<SaveEntityIdComponent>();
        var profession = ev.Victim.GetComponent<ProfessionComponent>();
        if (profession != null
            && !string.IsNullOrEmpty(profession.SlotId)
            && _registry!.Professions.TryGetValue(profession.SlotId, out var slot)
            && marker != null
            && string.Equals(slot.OccupiedNpcSaveId, marker.SaveId, StringComparison.OrdinalIgnoreCase))
        {
            slot.OccupiedNpcSaveId = null;
            slot.OccupiedSinceDayIndex = null;
        }

        _scheduledRetry = true;
    }

    /// <summary>
    /// Внешний хук — например, ChildGrowthSystem может позвать после совершеннолетия,
    /// чтобы не ждать DayChanged.
    /// </summary>
    public void RequestFillNow() => _scheduledRetry = true;

    private void TryFillVacancies()
    {
        if (!EnsureServices()) return;

        var today = _clock!.DayIndex;
        var seekers = CollectSeekers();
        if (seekers.Count == 0) return;

        // Группируем вакансии по поселению; ищем кандидатов в том же поселении.
        var vacanciesBySettlement = _registry!.Professions.Values
            .Where(slot => slot.IsVacant && !string.IsNullOrWhiteSpace(slot.SettlementId))
            .GroupBy(slot => slot.SettlementId, StringComparer.OrdinalIgnoreCase);

        foreach (var group in vacanciesBySettlement)
            FillSettlement(group.Key, group.ToList(), seekers, today);
    }

    private void FillSettlement(string settlementId, List<ProfessionSlotDef> vacancies, List<Seeker> seekers, long today)
    {
        var local = seekers.Where(s => string.Equals(s.SettlementId, settlementId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (local.Count == 0)
            return;

        // Сначала закрываем самые "квалифицированные" вакансии — где у лучшего кандидата выше скилл.
        foreach (var vacancy in vacancies)
        {
            if (local.Count == 0) break;

            var primary = _catalog!.Get(vacancy.ProfessionId)?.PrimarySkill ?? "";
            var best = local
                .OrderByDescending(s => string.IsNullOrEmpty(primary) ? 0f : s.Skills?.Get(primary) ?? 0f)
                .ThenBy(s => s.SaveId, StringComparer.OrdinalIgnoreCase)
                .First();

            AssignProfession(best, vacancy, today);
            local.Remove(best);
            seekers.Remove(best);
        }
    }

    private void AssignProfession(Seeker seeker, ProfessionSlotDef vacancy, long today)
    {
        vacancy.OccupiedNpcSaveId = seeker.SaveId;
        vacancy.OccupiedSinceDayIndex = today;

        var profession = seeker.Entity.GetComponent<ProfessionComponent>()
            ?? seeker.Entity.AddComponent(new ProfessionComponent());
        profession.ProfessionId = vacancy.ProfessionId;
        profession.SlotId = vacancy.Id;
        profession.JoinedDayIndex = today;

        // Шаблон расписания.
        var schedule = seeker.Entity.GetComponent<ScheduleComponent>()
            ?? seeker.Entity.AddComponent(new ScheduleComponent());
        if (!string.Equals(schedule.TemplateId, WorkerTemplate, StringComparison.OrdinalIgnoreCase))
        {
            schedule.TemplateId = WorkerTemplate;
            schedule.Slots.Clear();
            schedule.Freetime.Clear();
            _templates?.Apply(schedule, schedule.TemplateId);
        }

        Console.WriteLine($"[JobMarket] Assigned {seeker.SaveId} to {vacancy.Id} ({vacancy.ProfessionId}).");
    }

    private List<Seeker> CollectSeekers()
    {
        var list = new List<Seeker>();
        foreach (var entity in World.GetEntitiesWith<NpcTagComponent, IdentityComponent>())
        {
            if (!entity.Active) continue;
            if (!NpcLod.IsActiveOrBackground(entity)) continue;

            var profession = entity.GetComponent<ProfessionComponent>();
            if (profession != null && !string.IsNullOrWhiteSpace(profession.SlotId))
                continue;

            var age = entity.GetComponent<AgeComponent>();
            if (age == null || age.Years < MinWorkingAge || age.IsPensioner)
                continue;

            var growth = entity.GetComponent<ChildGrowthComponent>();
            if (growth != null) continue;

            var marker = entity.GetComponent<SaveEntityIdComponent>();
            if (marker == null || string.IsNullOrWhiteSpace(marker.SaveId))
                continue;

            var identity = entity.GetComponent<IdentityComponent>()!;
            if (string.IsNullOrWhiteSpace(identity.SettlementId))
                continue;

            list.Add(new Seeker
            {
                Entity = entity,
                SaveId = marker.SaveId,
                SettlementId = identity.SettlementId,
                Skills = entity.GetComponent<SkillsComponent>()
            });
        }
        return list;
    }

    private bool EnsureServices()
    {
        _clock ??= ServiceLocator.Has<GameClock>() ? ServiceLocator.Get<GameClock>() : null;
        _registry ??= ServiceLocator.Has<WorldRegistry>() ? ServiceLocator.Get<WorldRegistry>() : null;
        _catalog ??= ServiceLocator.Has<ProfessionCatalog>() ? ServiceLocator.Get<ProfessionCatalog>() : null;
        _templates ??= ServiceLocator.Has<ScheduleTemplates>() ? ServiceLocator.Get<ScheduleTemplates>() : null;
        return _clock != null && _registry != null && _catalog != null;
    }

    private sealed class Seeker
    {
        public required Entity Entity { get; init; }
        public required string SaveId { get; init; }
        public required string SettlementId { get; init; }
        public SkillsComponent? Skills { get; init; }
    }
}
