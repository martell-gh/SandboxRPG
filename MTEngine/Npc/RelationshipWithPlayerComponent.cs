using System;
using MTEngine.ECS;

namespace MTEngine.Npc;

/// <summary>
/// Категории «фактов» о NPC, которые игрок может постепенно узнать (см. §9.2 NpcAlifeSystem).
/// Сериализуется как bitmask в <see cref="RelationshipWithPlayerComponent.FactsRevealed"/>.
/// </summary>
[Flags]
public enum PlayerKnownFact
{
    None = 0,
    BestSkill = 1 << 0,
    RandomKin = 1 << 1,
    Profession = 1 << 2,
    Residence = 1 << 3,
    FamilyStatus = 1 << 4,
}

/// <summary>
/// Дружба и романтика конкретного NPC по отношению к игроку, плюс накопленные факты,
/// которые игрок уже знает про него. Заполняется при первом разговоре игрока с NPC,
/// если компонента ещё нет (см. §9 NpcAlifeSystem).
///
/// Отличие от <see cref="RelationshipsComponent.PlayerOpinion"/>: PlayerOpinion — это
/// общий уровень враждебности/симпатии (-100..100, падает от агрессии). Friendship/Romance
/// здесь растут только от позитивных взаимодействий и используются для квестов, диалогов
/// и романтической линии.
/// </summary>
[RegisterComponent("playerRel")]
public class RelationshipWithPlayerComponent : Component
{
    /// <summary>0..100, растёт от приятных взаимодействий с игроком.</summary>
    [DataField("friendship")]
    [SaveField("friendship")]
    public int Friendship { get; set; }

    /// <summary>0..100, заблокирован при одинаковом поле игрока и NPC (проверяется в момент роста).</summary>
    [DataField("romance")]
    [SaveField("romance")]
    public int Romance { get; set; }

    /// <summary>Bitmask <see cref="PlayerKnownFact"/>: какие факты об NPC игрок уже узнал.</summary>
    [DataField("factsRevealed")]
    [SaveField("factsRevealed")]
    public int FactsRevealed { get; set; }

    /// <summary>Время последнего «Поговорить» в <see cref="MTEngine.Core.GameClock.TotalSecondsAbsolute"/>.
    /// Используется кулдауном Talk-интеракции (см. §9.3).</summary>
    [DataField("lastTalkAtSeconds")]
    [SaveField("lastTalkAtSeconds")]
    public double LastTalkAtSeconds { get; set; } = double.MinValue;

    public bool HasFact(PlayerKnownFact fact) => (FactsRevealed & (int)fact) != 0;

    public void RevealFact(PlayerKnownFact fact) => FactsRevealed |= (int)fact;
}
