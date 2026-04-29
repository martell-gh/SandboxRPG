using MTEngine.ECS;

namespace MTEngine.Npc;

[RegisterComponent("avenger")]
public class AvengerComponent : Component
{
    [DataField("targetSaveId")]
    [SaveField("targetSaveId")]
    public string TargetSaveId { get; set; } = "";

    [DataField("targetIsPlayer")]
    [SaveField("targetIsPlayer")]
    public bool TargetIsPlayer { get; set; } = true;

    [DataField("victimSaveId")]
    [SaveField("victimSaveId")]
    public string VictimSaveId { get; set; } = "";

    [DataField("startedDayIndex")]
    [SaveField("startedDayIndex")]
    public long StartedDayIndex { get; set; }

    [DataField("lastKnownMapId")]
    [SaveField("lastKnownMapId")]
    public string LastKnownMapId { get; set; } = "";

    [DataField("lastKnownX")]
    [SaveField("lastKnownX")]
    public float LastKnownX { get; set; }

    [DataField("lastKnownY")]
    [SaveField("lastKnownY")]
    public float LastKnownY { get; set; }

    [DataField("weaponIssued")]
    [SaveField("weaponIssued")]
    public bool WeaponIssued { get; set; }

    [DataField("speedBoostApplied")]
    [SaveField("speedBoostApplied")]
    public bool SpeedBoostApplied { get; set; }

    [DataField("combatSkillId")]
    [SaveField("combatSkillId")]
    public string CombatSkillId { get; set; } = "";

    [DataField("accusationSaid")]
    [SaveField("accusationSaid")]
    public bool AccusationSaid { get; set; }
}
