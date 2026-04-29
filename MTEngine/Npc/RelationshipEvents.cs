namespace MTEngine.Npc;

// MTLiving — события социального слоя.

/// <summary>Свидание и свадьба запланированы между двумя NPC. Публикует MatchmakingSystem.</summary>
public readonly struct RelationshipDateScheduled
{
    public string NpcASaveId { get; }
    public string NpcBSaveId { get; }
    public long DateDayIndex { get; }
    public long WeddingDayIndex { get; }

    public RelationshipDateScheduled(string a, string b, long dateDay, long weddingDay)
    {
        NpcASaveId = a;
        NpcBSaveId = b;
        DateDayIndex = dateDay;
        WeddingDayIndex = weddingDay;
    }
}

/// <summary>Запланированное свидание наступило: пара перешла из Single в Dating.</summary>
public readonly struct RelationshipStarted
{
    public string NpcASaveId { get; }
    public string NpcBSaveId { get; }
    public long DayIndex { get; }

    public RelationshipStarted(string a, string b, long dayIndex)
    {
        NpcASaveId = a;
        NpcBSaveId = b;
        DayIndex = dayIndex;
    }
}

/// <summary>Запланированная свадьба наступила: пара перешла в Married.</summary>
public readonly struct RelationshipMarried
{
    public string NpcASaveId { get; }
    public string NpcBSaveId { get; }
    public long DayIndex { get; }

    public RelationshipMarried(string a, string b, long dayIndex)
    {
        NpcASaveId = a;
        NpcBSaveId = b;
        DayIndex = dayIndex;
    }
}

/// <summary>Беременность запланирована — задана дата рождения.</summary>
public readonly struct PregnancyScheduled
{
    public string MotherSaveId { get; }
    public string FatherSaveId { get; }
    public long BirthDayIndex { get; }

    public PregnancyScheduled(string motherSaveId, string fatherSaveId, long birthDayIndex)
    {
        MotherSaveId = motherSaveId;
        FatherSaveId = fatherSaveId;
        BirthDayIndex = birthDayIndex;
    }
}

/// <summary>Родился ребёнок.</summary>
public readonly struct NpcBorn
{
    public string ChildSaveId { get; }
    public string FatherSaveId { get; }
    public string MotherSaveId { get; }
    public string HouseId { get; }
    public long DayIndex { get; }

    public NpcBorn(string childSaveId, string fatherSaveId, string motherSaveId, string houseId, long dayIndex)
    {
        ChildSaveId = childSaveId;
        FatherSaveId = fatherSaveId;
        MotherSaveId = motherSaveId;
        HouseId = houseId;
        DayIndex = dayIndex;
    }
}

/// <summary>Пара рассталась. Публикуется, когда брак/dating обрывается (смерть, измена партнёра-NPC игроку и т.п.).</summary>
public readonly struct RelationshipSeparated
{
    public string NpcSaveId { get; }
    /// <summary>SaveId второй стороны, либо "player" если партнёром был игрок.</summary>
    public string OtherSaveId { get; }
    public long DayIndex { get; }
    /// <summary>Свободная строковая причина: "cohabitation_neglect", "death", "infidelity_npc" и т.д.</summary>
    public string Cause { get; }

    public RelationshipSeparated(string npcSaveId, string otherSaveId, long dayIndex, string cause)
    {
        NpcSaveId = npcSaveId;
        OtherSaveId = otherSaveId;
        DayIndex = dayIndex;
        Cause = cause;
    }
}

/// <summary>NPC сменил дом проживания.</summary>
public readonly struct NpcMovedHouse
{
    public string NpcSaveId { get; }
    public string OldHouseId { get; }
    public string NewHouseId { get; }
    public long DayIndex { get; }

    public NpcMovedHouse(string npcSaveId, string oldHouseId, string newHouseId, long dayIndex)
    {
        NpcSaveId = npcSaveId;
        OldHouseId = oldHouseId;
        NewHouseId = newHouseId;
        DayIndex = dayIndex;
    }
}
