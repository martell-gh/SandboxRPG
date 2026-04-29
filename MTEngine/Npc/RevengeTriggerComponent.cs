using MTEngine.ECS;

namespace MTEngine.Npc;

public enum RevengeBehavior
{
    None,
    MerchantPenalty,
    HostileOnSight,
    OpportunisticHunter,
    Avenger
}

public class RevengeTrigger
{
    [DataField("victimSaveId")]
    [SaveField("victimSaveId")]
    public string VictimSaveId { get; set; } = "";

    [DataField("killerSaveId")]
    [SaveField("killerSaveId")]
    public string KillerSaveId { get; set; } = "";

    [DataField("causeKin")]
    [SaveField("causeKin")]
    public KinKind CauseKin { get; set; }

    [DataField("createdDayIndex")]
    [SaveField("createdDayIndex")]
    public long CreatedDayIndex { get; set; }

    [DataField("triggerAfterDayIndex")]
    [SaveField("triggerAfterDayIndex")]
    public long TriggerAfterDayIndex { get; set; }

    [DataField("behavior")]
    [SaveField("behavior")]
    public RevengeBehavior Behavior { get; set; } = RevengeBehavior.None;

    [DataField("ready")]
    [SaveField("ready")]
    public bool Ready { get; set; }

    [DataField("readyDayIndex")]
    [SaveField("readyDayIndex")]
    public long ReadyDayIndex { get; set; } = -1L;
}

[RegisterComponent("revengeTrigger")]
public class RevengeTriggerComponent : Component
{
    [DataField("triggers")]
    [SaveField("triggers")]
    public List<RevengeTrigger> Triggers { get; set; } = new();

    public bool HasTriggerFor(string victimSaveId, string killerSaveId)
        => Triggers.Any(trigger =>
            string.Equals(trigger.VictimSaveId, victimSaveId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(trigger.KillerSaveId, killerSaveId, StringComparison.OrdinalIgnoreCase));
}
