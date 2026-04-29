using MTEngine.ECS;

namespace MTEngine.Npc;

public enum NpcHealingMode
{
    SelfTreatment,
    SeekingHealer,
    ReceivingHealer,
    ProvidingHealer
}

/// <summary>
/// Runtime-only state for short NPC healing routines.
/// </summary>
[RegisterComponent("npcHealing")]
public class NpcHealingComponent : Component
{
    public NpcHealingMode Mode { get; set; }
    public int HealerEntityId { get; set; }
    public int PatientEntityId { get; set; }
    public float ProgressSeconds { get; set; }
    public bool StartedSpeech { get; set; }
}
