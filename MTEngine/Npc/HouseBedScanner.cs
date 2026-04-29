using System.Collections.Generic;
using System.Globalization;
using MTEngine.Core;
using MTEngine.World;

namespace MTEngine.Npc;

/// <summary>
/// Находит "кровати-сущности" (entities с компонентом bed), размещённые внутри тайлов area-зоны дома.
/// Используется чтобы дом мог автоматически "видеть" свои кровати без необходимости вручную ставить
/// именованные точки bed_slot_*.
/// </summary>
public static class HouseBedScanner
{
    /// <summary>
    /// Перечислить кровати-сущности внутри area, которые НЕ покрыты ни одной именованной точкой bed_slot_*/child_bed_*.
    /// Возвращает синтетические точки со стабильным id вида "bed@x,y".
    /// </summary>
    public static IEnumerable<AreaPointData> EnumerateAutoBedPoints(
        AreaZoneData area, MapData map, PrototypeManager prototypes)
    {
        if (map.Entities.Count == 0)
            yield break;

        var ts = map.TileSize > 0 ? map.TileSize : 32;
        var tiles = new HashSet<(int, int)>();
        foreach (var t in area.Tiles)
            tiles.Add((t.X, t.Y));
        if (tiles.Count == 0)
            yield break;

        var occupiedTiles = new HashSet<(int, int)>();
        foreach (var p in area.Points)
        {
            if (p.Id.StartsWith("bed_slot_", System.StringComparison.OrdinalIgnoreCase)
                || p.Id.StartsWith("child_bed_", System.StringComparison.OrdinalIgnoreCase)
                || p.Id.StartsWith("inn_bed_", System.StringComparison.OrdinalIgnoreCase))
            {
                occupiedTiles.Add((p.X, p.Y));
            }
        }

        var emitted = new HashSet<(int, int)>();
        foreach (var entity in map.Entities)
        {
            if (string.IsNullOrWhiteSpace(entity.ProtoId))
                continue;

            var proto = prototypes.GetEntity(entity.ProtoId);
            if (proto?.Components?["bed"] == null)
                continue;

            int tx, ty;
            if (entity.WorldSpace)
            {
                tx = (int)(entity.X / ts);
                ty = (int)(entity.Y / ts);
            }
            else
            {
                tx = (int)entity.X;
                ty = (int)entity.Y;
            }

            if (!tiles.Contains((tx, ty)))
                continue;
            if (occupiedTiles.Contains((tx, ty)))
                continue;
            if (!emitted.Add((tx, ty)))
                continue;

            yield return new AreaPointData
            {
                Id = MakeAutoBedId(tx, ty),
                X = tx,
                Y = ty
            };
        }
    }

    public static string MakeAutoBedId(int tileX, int tileY)
        => string.Format(CultureInfo.InvariantCulture, "bed@{0},{1}", tileX, tileY);

    public static bool HasAnyBedInsideArea(AreaZoneData area, MapData map, PrototypeManager prototypes)
    {
        foreach (var _ in EnumerateAutoBedPoints(area, map, prototypes))
            return true;
        return false;
    }
}
