using MTEngine.Core;
using MTEngine.ECS;

namespace MTEngine.Npc;

/// <summary>
/// Раз в игровой день пересчитывает возраст всех entity с AgeComponent.
/// Подписан на DayChanged. На текущем этапе только обновляет Years; смерти/пенсии нет.
/// </summary>
public class AgingSystem : GameSystem
{
    private GameClock _clock = null!;
    private EventBus _bus = null!;

    public override void OnInitialize()
    {
        _clock = ServiceLocator.Get<GameClock>();
        _bus = ServiceLocator.Get<EventBus>();
        _bus.Subscribe<DayChanged>(OnDayChanged);
    }

    public override void Update(float deltaTime)
    {
        // Лениво проставляем BirthDayIndex для тех, у кого только InitialAgeYears.
        // Делаем это в Update, а не в OnInitialize, чтобы покрывать вновь спавнящихся.
        var calendar = GetCalendar();
        var daysPerYear = calendar.DaysPerYear;
        if (daysPerYear <= 0) return;

        foreach (var e in World.GetEntitiesWith<AgeComponent>())
        {
            if (e.HasComponent<NpcTagComponent>() && !NpcLod.IsActiveOrBackground(e))
                continue;

            var age = e.GetComponent<AgeComponent>()!;
            if (age.BirthDayIndex < 0L)
            {
                var initial = age.InitialAgeYears >= 0 ? age.InitialAgeYears : 0;
                age.BirthDayIndex = Math.Max(0L, _clock.DayIndex - (long)initial * daysPerYear);
                age.Years = ComputeYears(age.BirthDayIndex);
            }
        }
    }

    private void OnDayChanged(DayChanged e)
    {
        foreach (var entity in World.GetEntitiesWith<AgeComponent>())
        {
            if (entity.HasComponent<NpcTagComponent>() && !NpcLod.IsActiveOrBackground(entity))
                continue;

            var age = entity.GetComponent<AgeComponent>()!;
            if (age.BirthDayIndex < 0L) continue;
            age.Years = ComputeYears(age.BirthDayIndex);
        }
    }

    private int ComputeYears(long birthDayIndex)
    {
        var calendar = GetCalendar();
        var birth = calendar.FromDayIndex(birthDayIndex);
        var today = calendar.FromDayIndex(_clock.DayIndex);
        return calendar.YearsBetween(birth, today);
    }

    private static Calendar GetCalendar()
        => ServiceLocator.Has<Calendar>() ? ServiceLocator.Get<Calendar>() : new Calendar();

    public override void OnDestroy() => _bus.Unsubscribe<DayChanged>(OnDayChanged);
}
