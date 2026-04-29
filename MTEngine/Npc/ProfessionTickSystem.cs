using MTEngine.Core;
using MTEngine.ECS;

namespace MTEngine.Npc;

/// <summary>
/// §7.5: раз в игровой день каждый работающий NPC получает прибавку к primarySkill
/// своей профессии. Размер прибавки берётся из <see cref="ProfessionDefinition.SkillGainPerDay"/>.
/// Distant-NPC в этой версии тикаются так же — catch-up по зонам отложен на P6.5.
/// </summary>
public class ProfessionTickSystem : GameSystem
{
    private EventBus _bus = null!;
    private ProfessionCatalog? _catalog;

    public override void OnInitialize()
    {
        _bus = ServiceLocator.Get<EventBus>();
        if (ServiceLocator.Has<ProfessionCatalog>())
            _catalog = ServiceLocator.Get<ProfessionCatalog>();
        _bus.Subscribe<DayChanged>(OnDayChanged);
    }

    public override void Update(float deltaTime) { }

    public override void OnDestroy() => _bus.Unsubscribe<DayChanged>(OnDayChanged);

    private void OnDayChanged(DayChanged evt)
    {
        if (_catalog == null && ServiceLocator.Has<ProfessionCatalog>())
            _catalog = ServiceLocator.Get<ProfessionCatalog>();
        if (_catalog == null) return;

        foreach (var entity in World.GetEntitiesWith<NpcTagComponent, ProfessionComponent>())
        {
            if (!entity.Active) continue;
            if (!NpcLod.IsActiveOrBackground(entity)) continue;

            var profession = entity.GetComponent<ProfessionComponent>()!;
            if (string.IsNullOrWhiteSpace(profession.ProfessionId))
                continue;

            var def = _catalog.Get(profession.ProfessionId);
            if (def == null) continue;
            if (string.IsNullOrWhiteSpace(def.PrimarySkill)) continue;
            if (def.SkillGainPerDay <= 0f) continue;

            // Дети (есть ChildGrowthComponent) не качаются здесь — у них линейный рост по §6.4.
            if (entity.HasComponent<ChildGrowthComponent>()) continue;

            // Пенсионеры тоже не растут.
            var age = entity.GetComponent<AgeComponent>();
            if (age?.IsPensioner == true) continue;

            var skills = entity.GetComponent<SkillsComponent>() ?? entity.AddComponent(new SkillsComponent());
            skills.Add(def.PrimarySkill, def.SkillGainPerDay);
        }
    }
}
