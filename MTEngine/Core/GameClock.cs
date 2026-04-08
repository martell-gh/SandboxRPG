namespace MTEngine.Core;

[SaveObject("gameClock")]
public class GameClock
{
    private float _totalSeconds;

    [SaveField("timeScale")]
    public float TimeScale { get; set; } = 72f; // 1 игровые сутки = 20 реальных минут

    [SaveField("totalSeconds")]
    public float SavedTotalSeconds
    {
        get => _totalSeconds;
        set => _totalSeconds = Math.Clamp(value, 0f, 86400f);
    }

    public GameClock(float startHour = 8f)
    {
        _totalSeconds = startHour * 3600f;
    }

    public void Update(float deltaTime)
    {
        _totalSeconds += deltaTime * TimeScale;
        if (_totalSeconds >= 86400f)
            _totalSeconds -= 86400f;
    }

    public float TotalSeconds => _totalSeconds;
    public float Hour => _totalSeconds / 3600f;
    public int HourInt => (int)Hour;
    public int MinuteInt => (int)((Hour % 1f) * 60f);
    public string TimeString => $"{HourInt:D2}:{MinuteInt:D2}";
    public bool IsDay => Hour >= 6f && Hour < 20f;

    public void SetTime(float hour)
    {
        _totalSeconds = Math.Clamp(hour, 0f, 24f) * 3600f;
    }
}
