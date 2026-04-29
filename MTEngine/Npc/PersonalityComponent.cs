using MTEngine.ECS;

namespace MTEngine.Npc;

/// <summary>
/// Случайные черты NPC, влияющие на социальный слой.
/// Все шкалы 0..10. Если поле в прототипе не задано (-1), спаунер катает случайно.
/// Если Pacifist=true — Vengefulness принудительно = 0.
/// </summary>
[RegisterComponent("personality")]
public class PersonalityComponent : Component
{
    [DataField("infidelity")]   [SaveField("infidelity")]   public int Infidelity { get; set; } = -1;
    [DataField("vengefulness")] [SaveField("vengefulness")] public int Vengefulness { get; set; } = -1;
    [DataField("childWish")]    [SaveField("childWish")]    public int ChildWish { get; set; } = -1;
    [DataField("marriageWish")] [SaveField("marriageWish")] public int MarriageWish { get; set; } = -1;
    [DataField("sociability")]  [SaveField("sociability")]  public int Sociability { get; set; } = -1;
    [DataField("pacifist")]     [SaveField("pacifist")]     public bool Pacifist { get; set; }

    /// <summary>Прокатать незаполненные шкалы (значения -1) случайно [0..10].</summary>
    public void RollMissing(Random rng)
    {
        if (Infidelity   < 0) Infidelity   = rng.Next(0, 11);
        if (Vengefulness < 0) Vengefulness = rng.Next(0, 11);
        if (ChildWish    < 0) ChildWish    = rng.Next(0, 11);
        if (MarriageWish < 0) MarriageWish = rng.Next(0, 11);
        if (Sociability  < 0) Sociability  = rng.Next(0, 11);
        if (Pacifist) Vengefulness = 0;
    }
}
