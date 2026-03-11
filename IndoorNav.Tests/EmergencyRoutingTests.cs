using IndoorNav.Models;
using IndoorNav.Services;
using IndoorNav.Tests.Helpers;
using Xunit;

namespace IndoorNav.Tests;

/// <summary>
/// Тесты алгоритма поиска ближайшего выхода (EmergencyService.FindNearestExitRoute).
/// </summary>
public class EmergencyRoutingTests
{
    private static EmergencyService Svc() => new();

    // ════════════════════════════════════════════════════════════════════════
    // ① Базовые сценарии
    // ════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Простой граф — находит путь до единственного выхода")]
    public void FindNearestExit_SimpleCase_ReturnsPath()
    {
        // A --5-- B --5-- EXIT
        var g = new NavGraph();
        g.Nodes.Add(GB.Node("A",    building: "B1"));
        g.Nodes.Add(GB.Node("B",    building: "B1"));
        g.Nodes.Add(GB.Node("EXIT", building: "B1", isExit: true));
        foreach (var e in GB.BiEdge("A", "B", 5f).Concat(GB.BiEdge("B", "EXIT", 5f)))
            g.Edges.Add(e);

        var path = Svc().FindNearestExitRoute(g.Nodes.First(n => n.Id == "A"), g);

        Assert.Equal(3, path.Count);
        Assert.Equal("A",    path[0].Id);
        Assert.Equal("EXIT", path[2].Id);
    }

    [Fact(DisplayName = "Старт уже является выходом → возвращает один узел")]
    public void FindNearestExit_StartIsExit_ReturnsSingleNode()
    {
        var g = new NavGraph();
        var exit = GB.Node("EXIT", building: "B1", isExit: true);
        g.Nodes.Add(exit);

        var path = Svc().FindNearestExitRoute(exit, g);

        Assert.Single(path);
        Assert.Equal("EXIT", path[0].Id);
    }

    [Fact(DisplayName = "Нет выходов в графе → возвращает пустой список")]
    public void FindNearestExit_NoExitsAtAll_ReturnsEmpty()
    {
        var g = new NavGraph();
        g.Nodes.Add(GB.Node("A", building: "B1"));
        g.Nodes.Add(GB.Node("B", building: "B1"));
        foreach (var e in GB.BiEdge("A", "B", 1f)) g.Edges.Add(e);

        var path = Svc().FindNearestExitRoute(g.Nodes.First(), g);

        Assert.Empty(path);
    }

    // ════════════════════════════════════════════════════════════════════════
    // ② Выбор ближайшего из нескольких выходов
    // ════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Два выхода — выбирается ближний (меньшая суммарная дистанция)")]
    public void FindNearestExit_TwoExits_PicksCloser()
    {
        // START --2-- EXIT_NEAR (dist=2)
        // START --20-- EXIT_FAR (dist=20)
        var g = new NavGraph();
        var start = GB.Node("START", building: "B1");
        g.Nodes.Add(start);
        g.Nodes.Add(GB.Node("EXIT_NEAR", building: "B1", isExit: true));
        g.Nodes.Add(GB.Node("EXIT_FAR",  building: "B1", isExit: true));
        foreach (var e in GB.BiEdge("START", "EXIT_NEAR", 2f).Concat(GB.BiEdge("START", "EXIT_FAR", 20f)))
            g.Edges.Add(e);

        var path = Svc().FindNearestExitRoute(start, g);

        Assert.Equal("EXIT_NEAR", path.Last().Id);
    }

    [Fact(DisplayName = "Три выхода — выбирается с минимальной суммой рёбер")]
    public void FindNearestExit_ThreeExits_PicksMinCostExit()
    {
        // Расстояния считаются по координатам (Евклид):
        // S(0,0) → MID(5,0) → EXIT1(10,0)  итого 10
        // S(0,0) → EXIT2(3,0)              итого  3  ← ближайший
        // S(0,0) → EXIT3(20,0)             итого 20
        var g = new NavGraph();
        var start = GB.Node("S",     x:  0, building: "B1");
        g.Nodes.Add(start);
        g.Nodes.Add(GB.Node("MID",   x:  5, building: "B1"));
        g.Nodes.Add(GB.Node("EXIT1", x: 10, building: "B1", isExit: true));
        g.Nodes.Add(GB.Node("EXIT2", x:  3, building: "B1", isExit: true));
        g.Nodes.Add(GB.Node("EXIT3", x: 20, building: "B1", isExit: true));
        foreach (var e in GB.BiEdge("S", "MID", 5f).Concat(GB.BiEdge("MID", "EXIT1", 5f))
                           .Concat(GB.BiEdge("S", "EXIT2", 3f))
                           .Concat(GB.BiEdge("S", "EXIT3", 20f)))
            g.Edges.Add(e);

        var path = Svc().FindNearestExitRoute(start, g);

        Assert.Equal("EXIT2", path.Last().Id);
    }

    // ════════════════════════════════════════════════════════════════════════
    // ③ Исключения (заблокированные выходы и промежуточные узлы)
    // ════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Ближний выход заблокирован → идёт к дальнему")]
    public void FindNearestExit_NearExitExcluded_UsesNextExit()
    {
        var g = new NavGraph();
        var start = GB.Node("S", building: "B1");
        g.Nodes.Add(start);
        g.Nodes.Add(GB.Node("EXIT_NEAR", building: "B1", isExit: true));
        g.Nodes.Add(GB.Node("EXIT_FAR",  building: "B1", isExit: true));
        foreach (var e in GB.BiEdge("S", "EXIT_NEAR", 1f).Concat(GB.BiEdge("S", "EXIT_FAR", 50f)))
            g.Edges.Add(e);

        var path = Svc().FindNearestExitRoute(start, g, new HashSet<string> { "EXIT_NEAR" });

        Assert.NotEmpty(path);
        Assert.Equal("EXIT_FAR", path.Last().Id);
    }

    [Fact(DisplayName = "Все выходы заблокированы → пустой список")]
    public void FindNearestExit_AllExitsExcluded_ReturnsEmpty()
    {
        var g = new NavGraph();
        var start = GB.Node("S", building: "B1");
        g.Nodes.Add(start);
        g.Nodes.Add(GB.Node("E1", building: "B1", isExit: true));
        g.Nodes.Add(GB.Node("E2", building: "B1", isExit: true));
        foreach (var e in GB.BiEdge("S", "E1", 1f).Concat(GB.BiEdge("S", "E2", 1f)))
            g.Edges.Add(e);

        var path = Svc().FindNearestExitRoute(start, g, new HashSet<string> { "E1", "E2" });

        Assert.Empty(path);
    }

    [Fact(DisplayName = "Промежуточный узел заблокирован → находит обход к выходу")]
    public void FindNearestExit_BlockedIntermediary_FindsDetour()
    {
        //  S --1-- BLOCK --1-- EXIT
        //  S --20-- DETOUR --20-- EXIT
        var g = new NavGraph();
        var start = GB.Node("S", building: "B1");
        g.Nodes.Add(start);
        g.Nodes.Add(GB.Node("BLOCK",  building: "B1"));
        g.Nodes.Add(GB.Node("DETOUR", building: "B1"));
        g.Nodes.Add(GB.Node("EXIT",   building: "B1", isExit: true));
        foreach (var e in GB.BiEdge("S", "BLOCK", 1f).Concat(GB.BiEdge("BLOCK", "EXIT", 1f))
                           .Concat(GB.BiEdge("S", "DETOUR", 20f)).Concat(GB.BiEdge("DETOUR", "EXIT", 20f)))
            g.Edges.Add(e);

        var path = Svc().FindNearestExitRoute(start, g, new HashSet<string> { "BLOCK" });

        Assert.Equal("EXIT", path.Last().Id);
        Assert.DoesNotContain(path, n => n.Id == "BLOCK");
    }

    // ════════════════════════════════════════════════════════════════════════
    // ④ Строгость по зданию
    // ════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Выход в другом здании игнорируется — используется локальный")]
    public void FindNearestExit_OtherBuildingExitIgnored()
    {
        var g = new NavGraph();
        var start = GB.Node("S", building: "Building1");
        g.Nodes.Add(start);
        // Выход в том же здании (дальше)
        g.Nodes.Add(GB.Node("EXIT_LOCAL",  building: "Building1", isExit: true));
        // Выход в другом здании (ближе по весу)
        g.Nodes.Add(GB.Node("EXIT_OTHER", building: "Building2", isExit: true));

        foreach (var e in GB.BiEdge("S", "EXIT_LOCAL", 100f).Concat(GB.BiEdge("S", "EXIT_OTHER", 1f)))
            g.Edges.Add(e);

        var path = Svc().FindNearestExitRoute(start, g);

        Assert.Equal("EXIT_LOCAL", path.Last().Id);
    }

    [Fact(DisplayName = "Нет локальных выходов → fallback на все выходы в графе")]
    public void FindNearestExit_NoLocalExits_FallbackToAllBuildings()
    {
        var g = new NavGraph();
        var start = GB.Node("S", building: "B1");
        g.Nodes.Add(start);
        // Выход только в другом здании
        g.Nodes.Add(GB.Node("EXIT_B2", building: "B2", isExit: true));
        foreach (var e in GB.BiEdge("S", "EXIT_B2", 10f)) g.Edges.Add(e);

        var path = Svc().FindNearestExitRoute(start, g);

        // Fallback должен найти выход из другого здания
        Assert.NotEmpty(path);
        Assert.Equal("EXIT_B2", path.Last().Id);
    }

    // ════════════════════════════════════════════════════════════════════════
    // ⑤ EvacuationExit (запасной выход) работает как обычный
    // ════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Эвакуационный выход принимается алгоритмом как допустимый финиш")]
    public void FindNearestExit_EvacuationExitAccepted()
    {
        var g = new NavGraph();
        var start = GB.Node("S", building: "B1");
        g.Nodes.Add(start);
        g.Nodes.Add(GB.Node("EVAC", building: "B1", isEvacExit: true));
        foreach (var e in GB.BiEdge("S", "EVAC", 5f)) g.Edges.Add(e);

        var path = Svc().FindNearestExitRoute(start, g);

        Assert.Equal("EVAC", path.Last().Id);
    }

    [Fact(DisplayName = "Обычный и эвакуационный выход — выбирается ближний из обоих")]
    public void FindNearestExit_MixedExitTypes_ChoosesCloser()
    {
        // Расстояния по координатам (Евклид).
        // REGULAR_FAR далеко, EVAC_NEAR близко → должен выбраться EVAC_NEAR.
        var g = new NavGraph();
        var start = GB.Node("S",            x:  0, building: "B1");
        g.Nodes.Add(start);
        g.Nodes.Add(GB.Node("REGULAR_FAR",  x: 50, building: "B1", isExit: true));
        g.Nodes.Add(GB.Node("EVAC_NEAR",    x:  1, building: "B1", isEvacExit: true));
        foreach (var e in GB.BiEdge("S", "REGULAR_FAR", 50f).Concat(GB.BiEdge("S", "EVAC_NEAR", 1f)))
            g.Edges.Add(e);

        var path = Svc().FindNearestExitRoute(start, g);

        Assert.Equal("EVAC_NEAR", path.Last().Id);
    }

    // ════════════════════════════════════════════════════════════════════════
    // ⑥ Корректность пути (начало и конец)
    // ════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Возвращаемый путь начинается с стартового узла")]
    public void FindNearestExit_ReturnedPath_StartsWithStartNode()
    {
        var g = new NavGraph();
        var start = GB.Node("START", building: "B1");
        g.Nodes.Add(start);
        g.Nodes.Add(GB.Node("MID",  building: "B1"));
        g.Nodes.Add(GB.Node("EXIT", building: "B1", isExit: true));
        foreach (var e in GB.BiEdge("START", "MID", 5f).Concat(GB.BiEdge("MID", "EXIT", 5f)))
            g.Edges.Add(e);

        var path = Svc().FindNearestExitRoute(start, g);

        Assert.Equal("START", path.First().Id);
        Assert.Equal("EXIT",  path.Last().Id);
    }

    [Fact(DisplayName = "Путь — непрерывная цепочка смежных узлов")]
    public void FindNearestExit_ReturnedPath_IsContiguousChain()
    {
        var g = new NavGraph();
        var start = GB.Node("A", building: "B1");
        g.Nodes.Add(start);
        g.Nodes.Add(GB.Node("B",    building: "B1"));
        g.Nodes.Add(GB.Node("C",    building: "B1"));
        g.Nodes.Add(GB.Node("EXIT", building: "B1", isExit: true));
        foreach (var e in GB.BiEdge("A", "B", 1f).Concat(GB.BiEdge("B", "C", 1f))
                           .Concat(GB.BiEdge("C", "EXIT", 1f)))
            g.Edges.Add(e);

        var path = Svc().FindNearestExitRoute(start, g);

        Assert.Equal(4, path.Count);
        // Каждый узел должен быть связан рёбром с предыдущим
        for (int i = 1; i < path.Count; i++)
            Assert.True(g.EdgeExists(path[i - 1].Id, path[i].Id),
                $"Нет ребра между {path[i-1].Id} и {path[i].Id}");
    }
}
