using IndoorNav.Models;

namespace IndoorNav.Services;

/// <summary>
/// Manages emergency (ЧС) mode state. Admin activates; all clients subscribe via the event.
/// </summary>
public class EmergencyService
{
    public event EventHandler<bool>? EmergencyChanged;

    public bool IsEmergencyActive { get; private set; }
    public string EmergencyMessage => IsEmergencyActive
        ? "⚠ РЕЖИМ ЧРЕЗВЫЧАЙНОЙ СИТУАЦИИ — следуйте по маршруту до выхода!"
        : string.Empty;

    public void Activate()
    {
        if (IsEmergencyActive) return;
        IsEmergencyActive = true;
        EmergencyChanged?.Invoke(this, true);
    }

    public void Deactivate()
    {
        if (!IsEmergencyActive) return;
        IsEmergencyActive = false;
        EmergencyChanged?.Invoke(this, false);
    }

    /// <summary>
    /// Finds the nearest exit node from a given start node using Dijkstra.
    /// Returns the path as an ordered list of nodes, or empty if no exit found.
    /// </summary>
    public List<NavNode> FindNearestExitRoute(NavNode start, NavGraph graph)
    {
        // Only consider exits in the same building as the start node.
        // Fall back to all exits if the building has none marked.
        var exits = graph.Nodes
            .Where(n => (n.IsExit || n.IsEvacuationExit) && n.BuildingId == start.BuildingId)
            .ToList();
        if (!exits.Any())
            exits = graph.Nodes.Where(n => n.IsExit || n.IsEvacuationExit).ToList();
        if (!exits.Any()) return new();

        // Dijkstra
        var dist  = new Dictionary<string, double>();
        var prev  = new Dictionary<string, string?>();
        var queue = new SortedSet<(double d, string id)>(Comparer<(double d, string id)>.Create(
            (a, b) => a.d != b.d ? a.d.CompareTo(b.d) : string.Compare(a.id, b.id, StringComparison.Ordinal)));

        foreach (var n in graph.Nodes)
        {
            dist[n.Id] = double.MaxValue;
            prev[n.Id] = null;
        }
        dist[start.Id] = 0;
        queue.Add((0, start.Id));

        var nodeMap = graph.Nodes.ToDictionary(n => n.Id);
        var adj     = BuildAdjacency(graph);

        while (queue.Count > 0)
        {
            var (d, uid) = queue.Min;
            queue.Remove(queue.Min);

            if (!adj.TryGetValue(uid, out var neighbours)) continue;
            foreach (var (nid, w) in neighbours)
            {
                var alt = d + w;
                if (alt < dist[nid])
                {
                    queue.Remove((dist[nid], nid));
                    dist[nid] = alt;
                    prev[nid]  = uid;
                    queue.Add((alt, nid));
                }
            }
        }

        // Pick the exit with smallest distance
        var bestExit = exits.OrderBy(e => dist.GetValueOrDefault(e.Id, double.MaxValue)).FirstOrDefault();
        if (bestExit == null || dist[bestExit.Id] == double.MaxValue) return new();

        // Reconstruct path
        var path = new List<NavNode>();
        string? cur = bestExit.Id;
        while (cur != null)
        {
            if (nodeMap.TryGetValue(cur, out var node)) path.Add(node);
            prev.TryGetValue(cur, out cur);
        }
        path.Reverse();
        return path;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static Dictionary<string, List<(string id, double w)>> BuildAdjacency(NavGraph graph)
    {
        var adj = new Dictionary<string, List<(string, double)>>();
        foreach (var n in graph.Nodes)
            adj[n.Id] = new();

        var nodeMap = graph.Nodes.ToDictionary(n => n.Id);
        foreach (var e in graph.Edges)
        {
            if (!nodeMap.TryGetValue(e.FromId, out var a)) continue;
            if (!nodeMap.TryGetValue(e.ToId,   out var b)) continue;
            double w = Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
            adj[e.FromId].Add((e.ToId, w));
            adj[e.ToId].Add((e.FromId, w));
        }
        return adj;
    }
}
