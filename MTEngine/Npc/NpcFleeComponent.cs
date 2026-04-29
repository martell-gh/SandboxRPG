using Microsoft.Xna.Framework;
using MTEngine.ECS;

namespace MTEngine.Npc;

[RegisterComponent("npcFlee")]
public class NpcFleeComponent : Component
{
    [SaveField("threatEntityId")]
    public int ThreatEntityId { get; set; }

    [SaveField("threatSaveId")]
    public string ThreatSaveId { get; set; } = "";

    [SaveField("reason")]
    public string Reason { get; set; } = "";

    [SaveField("lastThreatPosition")]
    public Vector2 LastThreatPosition { get; set; }

    [SaveField("escapeTarget")]
    public Vector2 EscapeTarget { get; set; }

    [SaveField("startedAt")]
    public double StartedAt { get; set; }

    [SaveField("lastSawThreatAt")]
    public double LastSawThreatAt { get; set; }

    [SaveField("safeForSeconds")]
    public float SafeForSeconds { get; set; }

    public float RepathCooldownSeconds { get; set; }
}
