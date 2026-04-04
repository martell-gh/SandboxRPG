using Microsoft.Xna.Framework;
using MTEngine.World;

namespace MTEngine.Rendering;

/// <summary>
/// Collects external (silhouette) edges from opaque tiles.
/// An edge is external if the neighbouring tile on that side is NOT opaque.
/// Edges facing the origin are culled (back-face culling).
/// </summary>
public static class OccluderEdgeCollector
{
    public struct Edge
    {
        public Vector2 A;
        public Vector2 B;
    }

    /// <summary>
    /// Collects visible silhouette edges for shadow casting from a given origin.
    /// </summary>
    public static void Collect(
        TileMap map,
        Vector2 origin,
        int startX, int startY,
        int endX, int endY,
        List<Edge> edges)
    {
        edges.Clear();
        int ts = map.TileSize;

        for (int y = startY; y <= endY; y++)
        {
            for (int x = startX; x <= endX; x++)
            {
                if (!map.IsOpaque(x, y))
                    continue;

                float left = x * ts;
                float top = y * ts;
                float right = (x + 1) * ts;
                float bottom = (y + 1) * ts;

                // Winding: normal = (-edgeDir.Y, edgeDir.X) must point OUTWARD from tile.
                //
                // Top edge: normal (0,-1) = up  → go right-to-left
                if (!map.IsOpaque(x, y - 1))
                    TryAddEdge(edges, origin, new Vector2(right, top), new Vector2(left, top));

                // Bottom edge: normal (0,+1) = down → go left-to-right
                if (!map.IsOpaque(x, y + 1))
                    TryAddEdge(edges, origin, new Vector2(left, bottom), new Vector2(right, bottom));

                // Left edge: normal (-1,0) = left → go top-to-bottom
                if (!map.IsOpaque(x - 1, y))
                    TryAddEdge(edges, origin, new Vector2(left, top), new Vector2(left, bottom));

                // Right edge: normal (+1,0) = right → go bottom-to-top
                if (!map.IsOpaque(x + 1, y))
                    TryAddEdge(edges, origin, new Vector2(right, bottom), new Vector2(right, top));
            }
        }

        // Merge collinear adjacent edges to reduce draw calls
        MergeCollinearEdges(edges);
    }

    private static void TryAddEdge(List<Edge> edges, Vector2 origin, Vector2 a, Vector2 b)
    {
        // Back-face culling: only keep edges whose outward normal faces the origin
        var edgeMid = (a + b) * 0.5f;
        var edgeDir = b - a;
        // Outward normal (pointing away from the solid tile)
        var normal = new Vector2(-edgeDir.Y, edgeDir.X);
        var toOrigin = origin - edgeMid;

        // If origin is on the front side of this edge (dot > 0), keep it
        if (Vector2.Dot(normal, toOrigin) > 0f)
            edges.Add(new Edge { A = a, B = b });
    }

    private static void MergeCollinearEdges(List<Edge> edges)
    {
        // Merge horizontal edges (same Y, contiguous X)
        // and vertical edges (same X, contiguous Y)
        // Simple O(n²) merge — edge count is small for visible area

        for (int i = 0; i < edges.Count; i++)
        {
            var ei = edges[i];
            bool merged = true;

            while (merged)
            {
                merged = false;
                for (int j = i + 1; j < edges.Count; j++)
                {
                    var ej = edges[j];

                    // Can merge if they share an endpoint and are collinear
                    if (ei.B == ej.A && IsCollinear(ei.A, ei.B, ej.B))
                    {
                        ei.B = ej.B;
                        edges.RemoveAt(j);
                        merged = true;
                        break;
                    }

                    if (ej.B == ei.A && IsCollinear(ej.A, ej.B, ei.B))
                    {
                        ei.A = ej.A;
                        edges.RemoveAt(j);
                        merged = true;
                        break;
                    }
                }
            }

            edges[i] = ei;
        }
    }

    private static bool IsCollinear(Vector2 a, Vector2 b, Vector2 c)
    {
        // Cross product of (b-a) and (c-a) should be ~0
        var cross = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        return MathF.Abs(cross) < 0.01f;
    }
}
