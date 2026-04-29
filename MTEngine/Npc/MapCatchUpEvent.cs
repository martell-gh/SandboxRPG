namespace MTEngine.Npc;

/// <summary>
/// §10.3: после материализации карты, бывшей в Distant/Background, сюда падает
/// событие с количеством игровых дней, прошедших с последнего «личного» посещения.
/// Системы могут подписаться, чтобы догнать упущенные тики.
/// </summary>
public readonly struct MapCatchUpRan
{
    public string MapId { get; }
    public long DaysElapsed { get; }
    public long TodayDayIndex { get; }

    public MapCatchUpRan(string mapId, long daysElapsed, long todayDayIndex)
    {
        MapId = mapId;
        DaysElapsed = daysElapsed;
        TodayDayIndex = todayDayIndex;
    }
}
