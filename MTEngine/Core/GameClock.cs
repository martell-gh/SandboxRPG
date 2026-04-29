namespace MTEngine.Core;

/// <summary>
/// Published when the in-game day changes.
/// Daily systems subscribe to this instead of polling their own calendar counters.
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
    /// Monotonic world time in game seconds since the start of the world.
    /// Time of day is derived from it instead of clamping to a single day.
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

    [SaveField("dayIndex")]
    public long DayIndex
    {
        get => _dayIndex;
        set
        {
            var timeOfDay = TotalSeconds;
            _dayIndex = Math.Max(0L, value);
            _totalSeconds = _dayIndex * (double)SecondsPerDay + timeOfDay;
        }
    }

    [SaveField("totalSeconds")]
    public float SavedTotalSeconds
    {
        get => TotalSeconds;
        set => SetTime(value / 3600f);
    }

    public GameClock(float startHour = 8f)
    {
        _totalSeconds = startHour * 3600d;
        _dayIndex = 0;
    }

    public void Update(float deltaTime)
    {
        var previousDay = _dayIndex;
        _totalSeconds += deltaTime * TimeScale;
        _dayIndex = (long)(_totalSeconds / SecondsPerDay);

        if (_dayIndex != previousDay && ServiceLocator.Has<EventBus>())
            ServiceLocator.Get<EventBus>().Publish(new DayChanged(previousDay, _dayIndex));
    }

    public float TotalSeconds => (float)(_totalSeconds - _dayIndex * (double)SecondsPerDay);
    public float Hour => TotalSeconds / 3600f;
    public float TimeOfDayHour => Hour;
    public int HourInt => (int)Hour;
    public int TimeOfDayHourInt => (int)TimeOfDayHour;
    public int MinuteInt => (int)((TimeOfDayHour % 1f) * 60f);
    public string TimeString => $"{TimeOfDayHourInt:D2}:{MinuteInt:D2}";
    public bool IsDay => TimeOfDayHour >= 6f && TimeOfDayHour < 20f;

    public void SetTime(float hour)
    {
        var clamped = Math.Clamp(hour, 0f, 23.99f);
        _totalSeconds = _dayIndex * (double)SecondsPerDay + clamped * 3600d;
    }

    public void SetAbsoluteSeconds(double absoluteSeconds)
    {
        var previousDay = _dayIndex;
        _totalSeconds = Math.Max(0d, absoluteSeconds);
        _dayIndex = (long)(_totalSeconds / SecondsPerDay);

        if (_dayIndex != previousDay && ServiceLocator.Has<EventBus>())
            ServiceLocator.Get<EventBus>().Publish(new DayChanged(previousDay, _dayIndex));
    }

    public void AdvanceToHour(float hour)
    {
        var previousDay = _dayIndex;
        var clamped = Math.Clamp(hour, 0f, 23.99f);
        var currentHour = TimeOfDayHour;
        var newAbsolute = currentHour <= clamped
            ? _dayIndex * (double)SecondsPerDay + clamped * 3600d
            : (_dayIndex + 1) * (double)SecondsPerDay + clamped * 3600d;

        _totalSeconds = newAbsolute;
        _dayIndex = (long)(_totalSeconds / SecondsPerDay);

        if (_dayIndex != previousDay && ServiceLocator.Has<EventBus>())
            ServiceLocator.Get<EventBus>().Publish(new DayChanged(previousDay, _dayIndex));
    }
}
