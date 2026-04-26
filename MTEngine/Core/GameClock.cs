namespace MTEngine.Core;

/// <summary>
/// Эвент, который кидается в EventBus в момент смены игрового дня.
/// Все системы, тикающие "раз в день" (matchmaking, aging, рост ребёнка и т.д.),
/// подписываются именно на него, а не считают своими таймерами.
/// </summary>
public readonly struct DayChanged
{
    public long PreviousDayIndex { get; }
    public long NewDayIndex { get; }
    public DayChanged(long previousDayIndex, long newDayIndex)
    {
        PreviousDayIndex = previousDayIndex;
        NewDayIndex = newDayIndex;
    }
}

[SaveObject("gameClock")]
public class GameClock
{
    public const float SecondsPerDay = 86400f;

    private double _totalSeconds;
    private long _dayIndex;

    [SaveField("timeScale")]
    public float TimeScale { get; set; } = 72f; // 1 игровые сутки = 20 реальных минут

    /// <summary>
    /// Монотонная шкала "сколько игровых секунд прошло с начала мира".
    /// Никогда не клампится, никогда не сбрасывается. На ней основан календарь и старение.
    /// </summary>
    [SaveField("totalSecondsAbsolute")]
    public double TotalSecondsAbsolute
    {
        get => _totalSeconds;
        set
        {
            _totalSeconds = Math.Max(0d, value);
            _dayIndex = (long)(_totalSeconds / SecondsPerDay);
        }
    }

    /// <summary>
    /// Абсолютный индекс текущего игрового дня.
    /// </summary>
    [SaveField("dayIndex")]
    public long DayIndex
    {
        get => _dayIndex;
        set => _dayIndex = Math.Max(0L, value);
    }

    public GameClock(float startHour = 8f)
    {
        _totalSeconds = startHour * 3600d;
        _dayIndex = 0;
    }

    public void Update(float deltaTime)
    {
        var prevDay = _dayIndex;
        _totalSeconds += deltaTime * TimeScale;
        _dayIndex = (long)(_totalSeconds / SecondsPerDay);
        if (_dayIndex != prevDay && ServiceLocator.Has<EventBus>())
            ServiceLocator.Get<EventBus>().Publish(new DayChanged(prevDay, _dayIndex));
    }

    /// <summary>Сколько секунд прошло с полуночи текущего дня (0..86400).</summary>
    public float TotalSeconds => (float)(_totalSeconds - _dayIndex * (double)SecondsPerDay);

    public float Hour => TotalSeconds / 3600f;
    public int HourInt => (int)Hour;
    public int MinuteInt => (int)((Hour % 1f) * 60f);
    public string TimeString => $"{HourInt:D2}:{MinuteInt:D2}";
    public bool IsDay => Hour >= 6f && Hour < 20f;

    /// <summary>Установить время суток в часах [0..24), не меняя текущий день.</summary>
    public void SetTime(float hour)
    {
        var clamped = Math.Clamp(hour, 0f, 24f);
        _totalSeconds = _dayIndex * (double)SecondsPerDay + clamped * 3600d;
        _dayIndex = (long)(_totalSeconds / SecondsPerDay);
    }

    /// <summary>
    /// Прокрутить часы вперёд до ближайшего момента, когда наступит указанный час.
    /// Используется системой сна: проматывает день, если нужно.
    /// </summary>
    public void AdvanceToHour(float hour)
    {
        var prevDay = _dayIndex;
        var clamped = Math.Clamp(hour, 0f, 23.99f);
        var currentHour = Hour;
        var newAbsolute = currentHour <= clamped
            ? _dayIndex * (double)SecondsPerDay + clamped * 3600d
            : (_dayIndex + 1) * (double)SecondsPerDay + clamped * 3600d;

        _totalSeconds = newAbsolute;
        _dayIndex = (long)(_totalSeconds / SecondsPerDay);

        if (_dayIndex != prevDay && ServiceLocator.Has<EventBus>())
            ServiceLocator.Get<EventBus>().Publish(new DayChanged(prevDay, _dayIndex));
    }
}
