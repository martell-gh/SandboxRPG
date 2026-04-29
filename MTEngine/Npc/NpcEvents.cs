using Microsoft.Xna.Framework;
using MTEngine.ECS;

namespace MTEngine.Npc;

public readonly struct NpcArrivedAtArea
{
    public Entity Npc { get; }
    public ScheduleAction Action { get; }
    public string MapId { get; }
    public string AreaId { get; }
    public string PointId { get; }
    public Vector2 Position { get; }

    public NpcArrivedAtArea(Entity npc, ScheduleAction action, string mapId, string areaId, string pointId, Vector2 position)
    {
        Npc = npc;
        Action = action;
        MapId = mapId;
        AreaId = areaId;
        PointId = pointId;
        Position = position;
    }
}
