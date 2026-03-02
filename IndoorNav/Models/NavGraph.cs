namespace IndoorNav.Models;

public class NavGraph
{
    public List<NavNode> Nodes { get; set; } = new();
    public List<NavEdge> Edges { get; set; } = new();



    // ---------------------------------------------------------------
    // Вспомогательные методы
    // ---------------------------------------------------------------

    public NavNode? GetNode(string id) => Nodes.FirstOrDefault(n => n.Id == id);

    public IEnumerable<NavNode> GetNodesForFloor(string buildingId, int floor) =>
        Nodes.Where(n => n.BuildingId == buildingId && n.FloorNumber == floor);

    public IEnumerable<NavEdge> GetEdgesForFloor(string buildingId, int floor) =>
        Edges.Where(e =>
        {
            var from = GetNode(e.FromId);
            var to   = GetNode(e.ToId);
            return from != null && to != null
                && from.BuildingId == buildingId && from.FloorNumber == floor
                && to.BuildingId   == buildingId && to.FloorNumber   == floor;
        });

    /// <summary>
    /// Для панели администратора — включает также межэтажные рёбра,
    /// у которых хотя бы один конец находится на данном этаже.
    /// </summary>
    public IEnumerable<NavEdge> GetEdgesForFloorAdmin(string buildingId, int floor) =>
        Edges.Where(e =>
        {
            var from = GetNode(e.FromId);
            var to   = GetNode(e.ToId);
            if (from == null || to == null) return false;
            if (from.BuildingId != buildingId && to.BuildingId != buildingId) return false;
            return from.FloorNumber == floor || to.FloorNumber == floor;
        });

    /// <summary>
    /// Добавляет ребро с автоматическим весом (евклидово расстояние).
    /// Ориентированный граф — добавляем оба направления.
    /// </summary>
    public void AddEdge(string fromId, string toId, bool crossFloor = false)
    {
        var from = GetNode(fromId);
        var to   = GetNode(toId);
        if (from == null || to == null) return;

        float weight = crossFloor ? 150f   // штраф за смену этажа
            : MathF.Sqrt(MathF.Pow(to.X - from.X, 2) + MathF.Pow(to.Y - from.Y, 2));

        if (!Edges.Any(e => e.FromId == fromId && e.ToId == toId))
            Edges.Add(new NavEdge { FromId = fromId, ToId = toId, Weight = weight, IsCrossFloor = crossFloor });

        if (!Edges.Any(e => e.FromId == toId && e.ToId == fromId))
            Edges.Add(new NavEdge { FromId = toId, ToId = fromId, Weight = weight, IsCrossFloor = crossFloor });
    }

    public void RemoveEdge(string fromId, string toId)
    {
        Edges.RemoveAll(e =>
            (e.FromId == fromId && e.ToId == toId) ||
            (e.FromId == toId   && e.ToId == fromId));
    }

    public bool EdgeExists(string fromId, string toId) =>
        Edges.Any(e => (e.FromId == fromId && e.ToId == toId) ||
                       (e.FromId == toId   && e.ToId == fromId));

    // ---------------------------------------------------------------
    // Алгоритм Дейкстры
    // ---------------------------------------------------------------

    /// <summary>
    /// Возвращает список узлов-маршрута от startId до endId,
    /// или пустой список если маршрут не найден.
    /// </summary>
    public List<NavNode> FindPath(string startId, string endId)
        => FindPath(startId, endId, null);

    /// <summary>
    /// Дейкстра с возможностью исключить узлы из рассмотрения.
    /// </summary>
    public List<NavNode> FindPath(string startId, string endId, ISet<string>? excludeIds)
    {
        if (startId == endId)
            return GetNode(startId) is { } n ? [n] : [];

        var dist   = new Dictionary<string, float>();
        var prev   = new Dictionary<string, string?>();
        var queue  = new SortedSet<(float dist, string id)>(
                         Comparer<(float, string)>.Create((a, b) =>
                             a.Item1 != b.Item1 ? a.Item1.CompareTo(b.Item1)
                                                : string.Compare(a.Item2, b.Item2, StringComparison.Ordinal)));

        foreach (var node in Nodes)
        {
            // Исключённые узлы получают бесконечное расстояние — непроходимы
            dist[node.Id] = (excludeIds != null && excludeIds.Contains(node.Id)) ? float.MaxValue : float.MaxValue;
            prev[node.Id] = null;
        }
        dist[startId] = 0;
        queue.Add((0, startId));

        // Строим словарь смежности для быстрого поиска
        var adj = Nodes.ToDictionary(n => n.Id, _ => new List<(string neighbor, float weight)>());
        foreach (var edge in Edges)
        {
            if (adj.ContainsKey(edge.FromId))
                adj[edge.FromId].Add((edge.ToId, edge.Weight));
        }

        while (queue.Count > 0)
        {
            var (d, u) = queue.Min;
            queue.Remove(queue.Min);

            if (u == endId) break;
            if (d > dist[u]) continue;

            foreach (var (v, w) in adj[u])
            {
                // Пропускаем исключённые узлы (кроме конца маршрута)
                if (excludeIds != null && excludeIds.Contains(v) && v != endId) continue;
                float alt = dist[u] + w;
                if (alt < dist[v])
                {
                    queue.Remove((dist[v], v));
                    dist[v] = alt;
                    prev[v] = u;
                    queue.Add((alt, v));
                }
            }
        }

        // Восстанавливаем путь
        if (dist[endId] == float.MaxValue) return [];

        var path = new List<NavNode>();
        var current = endId;
        while (current != null)
        {
            var node = GetNode(current);
            if (node != null) path.Insert(0, node);
            prev.TryGetValue(current, out var p);
            current = p!;
        }
        return path;
    }
}
