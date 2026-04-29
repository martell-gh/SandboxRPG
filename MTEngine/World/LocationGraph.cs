namespace MTEngine.World;

/// <summary>
/// Undirected graph of map-to-map location transitions for NPC AI navigation.
/// Edges are authored manually in the editor (Global Settings → Location Graph)
/// and stored in <see cref="WorldData.LocationGraph"/>. The runtime adds both
/// directions for each authored edge — direction in which the wire was drawn
/// is irrelevant.
/// </summary>
public class LocationGraph
{
    private readonly Dictionary<string, HashSet<string>> _edges = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, HashSet<string>> Edges => _edges;

    public void Rebuild(MapManager mapManager)
    {
        _edges.Clear();

        var worldData = mapManager.GetWorldData();
        var graph = worldData?.LocationGraph;
        if (graph == null)
            return;

        foreach (var node in graph.Nodes)
        {
            if (!string.IsNullOrWhiteSpace(node.MapId))
                EnsureNode(node.MapId);
        }

        foreach (var edge in graph.Edges)
        {
            if (string.IsNullOrWhiteSpace(edge.A) || string.IsNullOrWhiteSpace(edge.B)) continue;
            if (string.Equals(edge.A, edge.B, StringComparison.OrdinalIgnoreCase)) continue;
            AddEdge(edge.A, edge.B);
            AddEdge(edge.B, edge.A);
        }
    }

    public int Distance(string? fromMapId, string? toMapId)
    {
        if (string.IsNullOrWhiteSpace(fromMapId) || string.IsNullOrWhiteSpace(toMapId))
            return int.MaxValue;

        if (string.Equals(fromMapId, toMapId, StringComparison.OrdinalIgnoreCase))
            return 0;

        if (!_edges.ContainsKey(fromMapId) || !_edges.ContainsKey(toMapId))
            return int.MaxValue;

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { fromMapId };
        var queue = new Queue<(string MapId, int Distance)>();
        queue.Enqueue((fromMapId, 0));

        while (queue.Count > 0)
        {
            var (current, distance) = queue.Dequeue();
            if (!_edges.TryGetValue(current, out var outgoing))
                continue;

            foreach (var next in outgoing)
            {
                if (!visited.Add(next))
                    continue;

                var nextDistance = distance + 1;
                if (string.Equals(next, toMapId, StringComparison.OrdinalIgnoreCase))
                    return nextDistance;

                queue.Enqueue((next, nextDistance));
            }
        }

        return int.MaxValue;
    }

    public bool IsReachable(string? fromMapId, string? toMapId)
        => Distance(fromMapId, toMapId) != int.MaxValue;

    public bool TryGetNextHop(string? fromMapId, string? toMapId, out string nextHop)
    {
        nextHop = "";
        if (string.IsNullOrWhiteSpace(fromMapId) || string.IsNullOrWhiteSpace(toMapId))
            return false;

        if (string.Equals(fromMapId, toMapId, StringComparison.OrdinalIgnoreCase))
        {
            nextHop = toMapId;
            return true;
        }

        if (!_edges.ContainsKey(fromMapId) || !_edges.ContainsKey(toMapId))
            return false;

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { fromMapId };
        var queue = new Queue<(string MapId, string FirstHop)>();

        foreach (var outgoing in _edges[fromMapId])
        {
            if (!visited.Add(outgoing))
                continue;

            queue.Enqueue((outgoing, outgoing));
        }

        while (queue.Count > 0)
        {
            var (current, firstHop) = queue.Dequeue();
            if (string.Equals(current, toMapId, StringComparison.OrdinalIgnoreCase))
            {
                nextHop = firstHop;
                return true;
            }

            if (!_edges.TryGetValue(current, out var outgoing))
                continue;

            foreach (var next in outgoing)
            {
                if (visited.Add(next))
                    queue.Enqueue((next, firstHop));
            }
        }

        return false;
    }

    public IReadOnlyCollection<string> GetOutgoing(string mapId)
        => _edges.TryGetValue(mapId, out var outgoing)
            ? outgoing
            : Array.Empty<string>();

    public IEnumerable<string> MapsWithin(string fromMapId, int maxDistance)
    {
        if (string.IsNullOrWhiteSpace(fromMapId) || maxDistance < 0 || !_edges.ContainsKey(fromMapId))
            yield break;

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { fromMapId };
        var queue = new Queue<(string MapId, int Distance)>();
        queue.Enqueue((fromMapId, 0));

        while (queue.Count > 0)
        {
            var (current, distance) = queue.Dequeue();
            yield return current;

            if (distance >= maxDistance || !_edges.TryGetValue(current, out var outgoing))
                continue;

            foreach (var next in outgoing)
            {
                if (visited.Add(next))
                    queue.Enqueue((next, distance + 1));
            }
        }
    }

    private void AddEdge(string fromMapId, string toMapId)
    {
        EnsureNode(fromMapId);
        EnsureNode(toMapId);
        _edges[fromMapId].Add(toMapId);
    }

    private void EnsureNode(string mapId)
    {
        if (!_edges.ContainsKey(mapId))
            _edges[mapId] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }
}
