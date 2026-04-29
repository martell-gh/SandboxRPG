using Microsoft.Xna.Framework;
using MTEngine.World;

namespace MTEngine.Npc;

/// <summary>
/// 4-connected A* по TileMap.IsSolid.
/// Для базового AI этого хватает: NPC ходят по сетке, диагонали не нужны.
/// Возвращает список тайловых точек from→to (включая обе крайние).
/// Если пути нет — возвращает пустой список.
/// </summary>
public static class GridPathfinder
{
    private static readonly Point[] Dirs =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1)
    };

    public static List<Point> FindPath(
        TileMap map,
        Point from,
        Point to,
        int maxNodes = 4000,
        Func<Point, bool>? isBlocked = null)
    {
        if (from == to) return new List<Point> { from };
        if (!InBounds(map, to.X, to.Y) || IsBlocked(map, to, isBlocked))
            return new List<Point>();

        var open = new PriorityQueue<Point, int>();
        var came = new Dictionary<Point, Point>();
        var gScore = new Dictionary<Point, int> { [from] = 0 };
        open.Enqueue(from, Heuristic(from, to));

        var visited = 0;
        while (open.Count > 0 && visited < maxNodes)
        {
            var cur = open.Dequeue();
            visited++;
            if (cur == to)
                return Reconstruct(came, cur);

            foreach (var d in Dirs)
            {
                var n = new Point(cur.X + d.X, cur.Y + d.Y);
                if (!InBounds(map, n.X, n.Y)) continue;
                if (n != to && IsBlocked(map, n, isBlocked)) continue;

                var tentative = gScore[cur] + 1;
                if (gScore.TryGetValue(n, out var existing) && tentative >= existing) continue;

                came[n] = cur;
                gScore[n] = tentative;
                open.Enqueue(n, tentative + Heuristic(n, to));
            }
        }
        return new List<Point>();
    }

    private static bool InBounds(TileMap map, int x, int y)
        => x >= 0 && y >= 0 && x < map.Width && y < map.Height;

    private static bool IsBlocked(TileMap map, Point point, Func<Point, bool>? isBlocked)
        => map.IsSolid(point.X, point.Y) || isBlocked?.Invoke(point) == true;

    private static int Heuristic(Point a, Point b)
        => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    private static List<Point> Reconstruct(Dictionary<Point, Point> came, Point end)
    {
        var path = new List<Point> { end };
        while (came.TryGetValue(end, out var prev))
        {
            end = prev;
            path.Add(end);
        }
        path.Reverse();
        return path;
    }
}
