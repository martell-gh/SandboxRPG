using System.Text.Json.Nodes;

namespace MTEngine.Core;

/// <summary>
/// Игровая дата. Лёгкий value-объект, считается из <see cref="GameClock.TotalSecondsAbsolute"/>.
/// </summary>
public readonly struct GameDate
{
    public int Year { get; }
    public int Month { get; }      // 1-based
    public int Day { get; }        // 1-based, день месяца
    public int Weekday { get; }    // 0-based индекс в Calendar.WeekdayNames
    public int Hour { get; }
    public int Minute { get; }
    public long DayIndex { get; }
    public double TotalSeconds { get; }

    public GameDate(int year, int month, int day, int weekday, int hour, int minute, long dayIndex, double totalSeconds)
    {
        Year = year;
        Month = month;
        Day = day;
        Weekday = weekday;
        Hour = hour;
        Minute = minute;
        DayIndex = dayIndex;
        TotalSeconds = totalSeconds;
    }
}

/// <summary>
/// Конфигурация календаря: длина суток, недели, месяца, года и подписи.
/// Загружается из <c>Data/calendar.json</c>, регистрируется в <see cref="ServiceLocator"/>.
/// Все системы старения, расписания, отношений работают через эту конфигурацию.
/// </summary>
[SaveObject("calendar")]
public class Calendar
{
    [SaveField("secondsPerDay")] public int SecondsPerDay { get; set; } = 86400;
    [SaveField("daysPerWeek")]   public int DaysPerWeek   { get; set; } = 7;
    [SaveField("daysPerMonth")]  public int DaysPerMonth  { get; set; } = 30;
    [SaveField("monthsPerYear")] public int MonthsPerYear { get; set; } = 12;
    [SaveField("epochYear")]     public int EpochYear     { get; set; } = 1000;

    public List<string> MonthNames { get; set; } = new();
    public List<string> WeekdayNames { get; set; } = new();

    public int DaysPerYear => DaysPerMonth * MonthsPerYear;

    public static Calendar LoadFromFile(string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"[Calendar] Not found: {path}, using defaults.");
            return new Calendar();
        }

        try
        {
            var node = JsonNode.Parse(File.ReadAllText(path))?.AsObject();
            if (node == null) return new Calendar();

            var cal = new Calendar
            {
                SecondsPerDay = node["secondsPerDay"]?.GetValue<int>() ?? 86400,
                DaysPerWeek   = node["daysPerWeek"]?.GetValue<int>() ?? 7,
                DaysPerMonth  = node["daysPerMonth"]?.GetValue<int>() ?? 30,
                MonthsPerYear = node["monthsPerYear"]?.GetValue<int>() ?? 12,
                EpochYear     = node["epochYear"]?.GetValue<int>() ?? 1000
            };

            if (node["monthNames"] is JsonArray months)
                foreach (var n in months) cal.MonthNames.Add(n?.GetValue<string>() ?? "");
            if (node["weekdayNames"] is JsonArray days)
                foreach (var n in days) cal.WeekdayNames.Add(n?.GetValue<string>() ?? "");

            return cal;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Calendar] Error: {e.Message}");
            return new Calendar();
        }
    }

    public GameDate FromTotalSeconds(double totalSeconds)
    {
        if (totalSeconds < 0d) totalSeconds = 0d;
        var dayIndex = (long)(totalSeconds / SecondsPerDay);
        var secondsInDay = totalSeconds - dayIndex * (double)SecondsPerDay;
        var hour = (int)(secondsInDay / 3600d);
        var minute = (int)((secondsInDay % 3600d) / 60d);
        return FromDayIndex(dayIndex, hour, minute, totalSeconds);
    }

    public GameDate FromDayIndex(long dayIndex, int hour = 0, int minute = 0, double? totalSeconds = null)
    {
        if (dayIndex < 0L) dayIndex = 0L;
        var year = EpochYear + (int)(dayIndex / DaysPerYear);
        var dayInYear = (int)(dayIndex % DaysPerYear);
        var month = dayInYear / DaysPerMonth + 1;
        var day = dayInYear % DaysPerMonth + 1;
        var weekday = (int)(dayIndex % DaysPerWeek);
        var ts = totalSeconds ?? (dayIndex * (double)SecondsPerDay + hour * 3600d + minute * 60d);
        return new GameDate(year, month, day, weekday, hour, minute, dayIndex, ts);
    }

    public long DaysBetween(GameDate from, GameDate to) => to.DayIndex - from.DayIndex;

    public int YearsBetween(GameDate from, GameDate to)
    {
        var years = to.Year - from.Year;
        if (to.Month < from.Month || (to.Month == from.Month && to.Day < from.Day))
            years--;
        return Math.Max(0, years);
    }

    /// <summary>"1004 г., Травень 12, Чт".</summary>
    public string Format(GameDate date)
    {
        var m = (date.Month >= 1 && date.Month <= MonthNames.Count) ? MonthNames[date.Month - 1] : date.Month.ToString();
        var w = (date.Weekday >= 0 && date.Weekday < WeekdayNames.Count) ? WeekdayNames[date.Weekday] : "";
        return $"{date.Year} г., {m} {date.Day}, {w}";
    }
}
