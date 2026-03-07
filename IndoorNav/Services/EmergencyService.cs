using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using IndoorNav.Models;

namespace IndoorNav.Services;

/// <summary>Event args for per-building emergency state change.</summary>
public class EmergencyChangedArgs : EventArgs
{
    /// <summary>Affected building ID. <c>null</c> means all buildings were affected.</summary>
    public string? BuildingId { get; init; }
    public bool IsActive { get; init; }
}

/// <summary>
/// Manages emergency (ЧС) mode state per-building. Admin activates; all clients subscribe via the event.
/// State is persisted to disk so it survives app restarts.
/// </summary>
public class EmergencyService
{
    public event EventHandler<EmergencyChangedArgs>? EmergencyChanged;

    private readonly HashSet<string> _activeBuildings = new();

    private static string GetProjectRootPath()
    {
        var basePath = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(basePath);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "IndoorNav.csproj")))
            dir = dir.Parent;
        return dir?.FullName ?? basePath;
    }

    private static string StatePath =>
        Path.Combine(GetProjectRootPath(), "Resources", "Raw", "emergency_state.json");

    /// <summary>True if ANY building is in emergency mode.</summary>
    public bool IsEmergencyActive => _activeBuildings.Count > 0;

    public bool IsActiveForBuilding(string? buildingId) =>
        !string.IsNullOrEmpty(buildingId) && _activeBuildings.Contains(buildingId);

    public string EmergencyMessage => IsEmergencyActive
        ? "⚠ РЕЖИМ ЧРЕЗВЫЧАЙНОЙ СИТУАЦИИ — следуйте по маршруту до выхода!"
        : string.Empty;

    // ── Persistence ──────────────────────────────────────────────────────────

    /// <summary>Load persisted emergency state. Call once on app startup.</summary>
    public async Task LoadAsync()
    {
        try
        {
            if (!File.Exists(StatePath)) return;
            var json = await File.ReadAllTextAsync(StatePath);
            var ids  = JsonSerializer.Deserialize<List<string>>(json);
            if (ids == null || ids.Count == 0) return;
            foreach (var id in ids)
                _activeBuildings.Add(id);
            EmergencyChanged?.Invoke(this, new EmergencyChangedArgs { BuildingId = null, IsActive = true });
        }
        catch { /* best-effort */ }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_activeBuildings.ToList());
            File.WriteAllText(StatePath, json);
        }
        catch { /* best-effort */ }
    }

    // ── Activation ───────────────────────────────────────────────────────────

    /// <summary>Activate emergency for a specific building.</summary>
    public void Activate(string buildingId)
    {
        if (_activeBuildings.Add(buildingId))
        {
            Save();
            EmergencyChanged?.Invoke(this, new EmergencyChangedArgs { BuildingId = buildingId, IsActive = true });
        }
    }

    /// <summary>Deactivate emergency for a specific building.</summary>
    public void Deactivate(string buildingId)
    {
        if (_activeBuildings.Remove(buildingId))
        {
            Save();
            EmergencyChanged?.Invoke(this, new EmergencyChangedArgs { BuildingId = buildingId, IsActive = false });
        }
    }

    /// <summary>Activate emergency for all given building IDs.</summary>
    public void ActivateAll(IEnumerable<string> buildingIds)
    {
        bool any = false;
        foreach (var id in buildingIds)
            if (_activeBuildings.Add(id)) any = true;
        if (any)
        {
            Save();
            EmergencyChanged?.Invoke(this, new EmergencyChangedArgs { BuildingId = null, IsActive = true });
        }
    }

    /// <summary>Deactivate emergency for all buildings.</summary>
    public void DeactivateAll()
    {
        if (_activeBuildings.Count == 0) return;
        _activeBuildings.Clear();
        Save();
        EmergencyChanged?.Invoke(this, new EmergencyChangedArgs { BuildingId = null, IsActive = false });
    }

    /// <summary>
    /// Finds the nearest exit node from a given start node using Dijkstra.
    /// Returns the path as an ordered list of nodes, or empty if no exit found.
    /// </summary>
    public List<NavNode> FindNearestExitRoute(NavNode start, NavGraph graph)
        => FindNearestExitRoute(start, graph, null);

    /// <summary>
    /// Finds the nearest exit node from a given start node using Dijkstra,
    /// optionally bypassing a set of blocked node IDs.
    /// </summary>
    public List<NavNode> FindNearestExitRoute(NavNode start, NavGraph graph, ISet<string>? excludeIds)
    {
        // Only consider exits in the same building as the start node.
        // Fall back to all exits if the building has none marked.
        // Also exclude blocked exits.
        var exits = graph.Nodes
            .Where(n => (n.IsExit || n.IsEvacuationExit)
                        && n.BuildingId == start.BuildingId
                        && excludeIds?.Contains(n.Id) != true)
            .ToList();
        if (!exits.Any())
            exits = graph.Nodes
                .Where(n => (n.IsExit || n.IsEvacuationExit) && excludeIds?.Contains(n.Id) != true)
                .ToList();
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
                // Skip blocked nodes (but always allow the start node through)
                if (nid != start.Id && excludeIds?.Contains(nid) == true) continue;

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

    // ── Server polling ────────────────────────────────────────────────────────────

    private record EmergencyStatusResponse(string[] ActiveBuildingIds);

    /// <summary>
    /// Polls GET /emergency/status every 30 s and syncs local state.
    /// Call once on app startup; pass a CancellationToken to stop.
    /// </summary>
    public async Task StartServerPollingAsync(CancellationToken ct)
    {
        if (!AppConfig.CanUseServer) return;

        using var http  = new HttpClient { BaseAddress = new Uri(AppConfig.ServerBaseUrl), Timeout = TimeSpan.FromSeconds(8) };
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));

        // Immediate first check
        await SyncWithServerAsync(http);

        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                await SyncWithServerAsync(http);
        }
        catch (OperationCanceledException) { }
    }

    private async Task SyncWithServerAsync(HttpClient http)
    {
        try
        {
            var dto = await http.GetFromJsonAsync<EmergencyStatusResponse>("/emergency/status");
            if (dto == null) return;

            var serverActive = new HashSet<string>(dto.ActiveBuildingIds, StringComparer.OrdinalIgnoreCase);

            // Activate buildings the server says are active but local doesn't know yet
            foreach (var id in serverActive.Except(_activeBuildings.ToList(), StringComparer.OrdinalIgnoreCase))
                Activate(id);

            // Deactivate buildings the server says are no longer active
            foreach (var id in _activeBuildings.Except(serverActive, StringComparer.OrdinalIgnoreCase).ToList())
                Deactivate(id);
        }
        catch { /* network error — keep current local state */ }
    }

    /// <summary>
    /// Calls POST /emergency/activate/{buildingId} on the server (fire-and-forget).
    /// Used by AdminViewModel so other clients learn about the change within 30 s.
    /// </summary>
    public async Task NotifyServerActivateAsync(string buildingId)
    {
        if (!AppConfig.CanUseServer) return;
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(AppConfig.ServerBaseUrl), Timeout = TimeSpan.FromSeconds(5) };
            await http.PostAsync($"/emergency/activate/{Uri.EscapeDataString(buildingId)}", null);
        }
        catch { /* best-effort */ }
    }

    /// <summary>Calls POST /emergency/deactivate/{buildingId} on the server (fire-and-forget).</summary>
    public async Task NotifyServerDeactivateAsync(string buildingId)
    {
        if (!AppConfig.CanUseServer) return;
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(AppConfig.ServerBaseUrl), Timeout = TimeSpan.FromSeconds(5) };
            await http.PostAsync($"/emergency/deactivate/{Uri.EscapeDataString(buildingId)}", null);
        }
        catch { /* best-effort */ }
    }

    /// <summary>Calls POST /emergency/deactivate-all on the server (fire-and-forget).</summary>
    public async Task NotifyServerDeactivateAllAsync()
    {
        if (!AppConfig.CanUseServer) return;
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(AppConfig.ServerBaseUrl), Timeout = TimeSpan.FromSeconds(5) };
            await http.PostAsync("/emergency/deactivate-all", null);
        }
        catch { /* best-effort */ }
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
