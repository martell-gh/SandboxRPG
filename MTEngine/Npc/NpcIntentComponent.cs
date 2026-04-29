using Microsoft.Xna.Framework;
using MTEngine.ECS;

namespace MTEngine.Npc;

/// <summary>
/// "Что NPC должен делать прямо сейчас".
/// Заполняется ScheduleSystem, читается NpcMovementSystem (и в будущем — другими).
/// </summary>
[RegisterComponent("npcIntent")]
public class NpcIntentComponent : Component
{
    [SaveField("action")]      public ScheduleAction Action { get; set; } = ScheduleAction.Wander;
    [SaveField("targetAreaId")] public string TargetAreaId { get; set; } = "";
    [SaveField("targetPointId")] public string TargetPointId { get; set; } = "";
    [SaveField("targetMapId")] public string TargetMapId { get; set; } = "";
    [SaveField("targetX")]     public float TargetX { get; set; }
    [SaveField("targetY")]     public float TargetY { get; set; }
    [SaveField("hasTarget")]   public bool HasTarget { get; set; }
    [SaveField("arrived")]     public bool Arrived { get; set; }

    public Vector2 TargetPosition => new(TargetX, TargetY);

    public void SetTarget(string mapId, Vector2 position, string areaId = "", string pointId = "")
    {
        var changed = !HasTarget
            || !string.Equals(TargetMapId, mapId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(TargetAreaId, areaId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(TargetPointId, pointId, StringComparison.OrdinalIgnoreCase)
            || TargetPosition != position;

        TargetMapId = mapId;
        TargetAreaId = areaId;
        TargetPointId = pointId;
        TargetX = position.X;
        TargetY = position.Y;
        HasTarget = true;
        if (changed)
            Arrived = false;
    }

    public void MarkArrived()
    {
        if (HasTarget)
            Arrived = true;
    }

    public void ClearTarget()
    {
        TargetMapId = "";
        TargetAreaId = "";
        TargetPointId = "";
        TargetX = 0; TargetY = 0;
        HasTarget = false;
        Arrived = false;
    }
}
