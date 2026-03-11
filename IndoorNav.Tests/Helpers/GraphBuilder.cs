using IndoorNav.Models;

namespace IndoorNav.Tests.Helpers;

/// <summary>
/// Фабричные методы для быстрого построения тестовых графов.
/// </summary>
public static class GB
{
    // ── Создание узлов ───────────────────────────────────────────────────────

    public static NavNode Node(
        string id,
        string name       = "",
        float  x          = 0f,
        float  y          = 0f,
        string building   = "B",
        int    floor      = 1,
        bool   isExit     = false,
        bool   isEvacExit = false,
        bool   isTrans    = false,
        bool   isWaypoint = false)
        => new()
        {
            Id               = id,
            Name             = name,
            X                = x,
            Y                = y,
            BuildingId       = building,
            FloorNumber      = floor,
            IsExit           = isExit,
            IsEvacuationExit = isEvacExit,
            IsTransition     = isTrans,
            IsWaypoint       = isWaypoint,
        };

    // ── Создание рёбер ───────────────────────────────────────────────────────

    /// <summary>Направленное ребро A→B.</summary>
    public static NavEdge Edge(string from, string to, float weight)
        => new() { FromId = from, ToId = to, Weight = weight };

    /// <summary>Пара рёбер A→B и B→A (двунаправленное).</summary>
    public static IEnumerable<NavEdge> BiEdge(string a, string b, float w)
        => new[] { Edge(a, b, w), Edge(b, a, w) };

    // ── Построение графов ────────────────────────────────────────────────────

    /// <summary>
    /// Линейная цепочка A₀→A₁→…→Aₙ с заданным весом каждого ребра.
    /// Возвращает граф и список ID узлов в порядке "старт → финиш".
    /// </summary>
    public static (NavGraph graph, List<string> ids) LinearChain(int count, float edgeWeight = 1f)
    {
        var g   = new NavGraph();
        var ids = Enumerable.Range(0, count).Select(i => $"n{i}").ToList();

        foreach (var id in ids)
            g.Nodes.Add(Node(id, id, x: ids.IndexOf(id) * 10f));

        for (int i = 0; i < count - 1; i++)
            foreach (var e in BiEdge(ids[i], ids[i + 1], edgeWeight))
                g.Edges.Add(e);

        return (g, ids);
    }

    /// <summary>
    /// Прямоугольная сетка rows×cols; каждый узел связан с 4 соседями.
    /// ID узла: "r{row}c{col}", координаты кратны spacing.
    /// </summary>
    public static (NavGraph graph, string topLeft, string bottomRight) Grid(
        int rows, int cols, float spacing = 10f)
    {
        var g = new NavGraph();

        string Id(int r, int c) => $"r{r}c{c}";

        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
            g.Nodes.Add(Node(Id(r, c), Id(r, c), x: c * spacing, y: r * spacing));

        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            if (c + 1 < cols) foreach (var e in BiEdge(Id(r, c), Id(r, c + 1), spacing)) g.Edges.Add(e);
            if (r + 1 < rows) foreach (var e in BiEdge(Id(r, c), Id(r + 1, c), spacing)) g.Edges.Add(e);
        }

        return (g, Id(0, 0), Id(rows - 1, cols - 1));
    }
}
