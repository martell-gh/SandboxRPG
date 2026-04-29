using MTEngine.Core;
using MTEngine.ECS;

namespace MTEngine.Npc;

/// <summary>
/// §6.4: раз в день для каждого ребёнка пересчитывает фактические скиллы
/// как target * (years / 18). При совершеннолетии удаляет ChildGrowthComponent
/// и переключает шаблон расписания на default_unemployed (JobMarketSystem ещё нет — TODO P5.2).
/// </summary>
public class ChildGrowthSystem : GameSystem
{
    private const int AdultAge = 18;
    private const string AdultDefaultTemplate = "default_unemployed";

    private EventBus _bus = null!;
    private ScheduleTemplates? _templates;

    public override void OnInitialize()
    {
        _bus = ServiceLocator.Get<EventBus>();
        if (ServiceLocator.Has<ScheduleTemplates>())
            _templates = ServiceLocator.Get<ScheduleTemplates>();
        _bus.Subscribe<DayChanged>(OnDayChanged);
    }

    public override void Update(float deltaTime) { }

    public override void OnDestroy() => _bus.Unsubscribe<DayChanged>(OnDayChanged);

    private void OnDayChanged(DayChanged evt)
    {
        foreach (var entity in World.GetEntitiesWith<NpcTagComponent, ChildGrowthComponent>())
        {
            if (!entity.Active) continue;
            if (!NpcLod.IsActiveOrBackground(entity)) continue;
            var growth = entity.GetComponent<ChildGrowthComponent>()!;
            var age = entity.GetComponent<AgeComponent>();
            var skills = entity.GetComponent<SkillsComponent>();
            if (age == null || skills == null)
                continue;

            if (age.Years < AdultAge)
            {
                var progress = Math.Clamp(age.Years / (float)AdultAge, 0f, 1f);
                foreach (var (id, target) in growth.TargetSkills)
                    skills.Set(id, target * progress);
                continue;
            }

            // Совершеннолетие.
            foreach (var (id, target) in growth.TargetSkills)
                skills.Set(id, target);

            entity.RemoveComponent<ChildGrowthComponent>();

            var schedule = entity.GetComponent<ScheduleComponent>();
            if (schedule != null && string.Equals(schedule.TemplateId, "default_child", StringComparison.OrdinalIgnoreCase))
            {
                schedule.TemplateId = AdultDefaultTemplate;
                schedule.Slots.Clear();
                schedule.Freetime.Clear();
                _templates?.Apply(schedule, schedule.TemplateId);
            }

            // Свежий совершеннолетний — попросим JobMarket попробовать выдать работу.
            World.GetSystem<JobMarketSystem>()?.RequestFillNow();
        }
    }
}
