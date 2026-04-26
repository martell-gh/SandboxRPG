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
    private Calendar _calendar = null!;
    private EventBus _bus = null!;

    public override void OnInitialize()
    {
        _clock = ServiceLocator.Get<GameClock>();
        _calendar = ServiceLocator.Get<Calendar>();
        _bus = ServiceLocator.Get<EventBus>();
        _bus.Subscribe<DayChanged>(OnDayChanged);
    }

    public override void Update(float deltaTime)
    {
        // Лениво проставляем BirthDayIndex для тех, у кого только InitialAgeYears.
        // Делаем это в Update, а не в OnInitialize, чтобы покрывать вновь спавнящихся.
        var daysPerYear = _calendar.DaysPerYear;
        if (daysPerYear <= 0) return;

        foreach (var e in World.GetEntitiesWith<AgeComponent>())
        {
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
            var age = entity.GetComponent<AgeComponent>()!;
            if (age.BirthDayIndex < 0L) continue;
            age.Years = ComputeYears(age.BirthDayIndex);
        }
    }

    private int ComputeYears(long birthDayIndex)
    {
        var birth = _calendar.FromDayIndex(birthDayIndex);
        var today = _calendar.FromDayIndex(_clock.DayIndex);
        return _calendar.YearsBetween(birth, today);
    }

    public override void OnDestroy() => _bus.Unsubscribe<DayChanged>(OnDayChanged);
}
