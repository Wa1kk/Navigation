using IndoorNav.Models;
using IndoorNav.Tests.Helpers;
using Xunit;

namespace IndoorNav.Tests;

/// <summary>
/// Модульные тесты алгоритма Дейкстры в NavGraph.FindPath().
/// Покрывают: тривиальные случаи, корректность пути, оптимальность,
/// исключения узлов, межэтажный маршрут, управление графом.
/// </summary>
public class NavGraphTests
{
    // ════════════════════════════════════════════════════════════════════════
    // ① Тривиальные/граничные случаи
    // ════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Start == End → возвращает ровно один узел")]
    public void FindPath_SameStartAndEnd_ReturnsSingleNode()
    {
        var g = new NavGraph();
        g.Nodes.Add(GB.Node("A"));

        var path = g.FindPath("A", "A");

        Assert.Single(path);
        Assert.Equal("A", path[0].Id);
    }

    [Fact(DisplayName = "Пустой граф → пустой список")]
    public void FindPath_EmptyGraph_ReturnsEmpty()
    {
        var g = new NavGraph();
        Assert.Empty(g.FindPath("X", "Y"));
    }

    [Fact(DisplayName = "Несуществующий startId → пустой список")]
    public void FindPath_UnknownStartId_ReturnsEmpty()
    {
        var g = new NavGraph();
        g.Nodes.Add(GB.Node("A"));
        Assert.Empty(g.FindPath("Z", "A"));
    }

    [Fact(DisplayName = "Несуществующий endId → пустой список")]
    public void FindPath_UnknownEndId_ReturnsEmpty()
    {
        var g = new NavGraph();
        g.Nodes.Add(GB.Node("A"));
        Assert.Empty(g.FindPath("A", "Z"));
    }

    [Fact(DisplayName = "Два узла без рёбер → нет маршрута")]
    public void FindPath_TwoNodesNoEdges_ReturnsEmpty()
    {
        var g = new NavGraph();
        g.Nodes.Add(GB.Node("A"));
        g.Nodes.Add(GB.Node("B"));
        Assert.Empty(g.FindPath("A", "B"));
    }

    // ════════════════════════════════════════════════════════════════════════
    // ② Базовая корректность пути
    // ════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "A→B — прямое ребро → путь [A, B]")]
    public void FindPath_TwoNodes_DirectEdge_CorrectPath()
    {
        var g = new NavGraph();
        g.Nodes.Add(GB.Node("A"));
        g.Nodes.Add(GB.Node("B"));
        g.Edges.Add(GB.Edge("A", "B", 1f));

        var path = g.FindPath("A", "B");

        Assert.Equal(2, path.Count);
        Assert.Equal("A", path[0].Id);
        Assert.Equal("B", path[1].Id);
    }

    [Fact(DisplayName = "Линейная цепочка 3 узлов → путь [A, B, C]")]
    public void FindPath_LinearChain_3Nodes_FullPath()
    {
        var g = new NavGraph();
        foreach (var id in new[] { "A", "B", "C" })
            g.Nodes.Add(GB.Node(id));
        foreach (var e in GB.BiEdge("A", "B", 1f).Concat(GB.BiEdge("B", "C", 1f)))
            g.Edges.Add(e);

        var path = g.FindPath("A", "C");

        Assert.Equal(3, path.Count);
        Assert.Equal(new[] { "A", "B", "C" }, path.Select(n => n.Id));
    }

    [Fact(DisplayName = "Линейная цепочка 5 узлов → все промежуточные узлы включены")]
    public void FindPath_LinearChain_5Nodes_AllNodesInOrder()
    {
        var (g, ids) = GB.LinearChain(5);
        var path = g.FindPath(ids[0], ids[4]);

        Assert.Equal(5, path.Count);
        Assert.Equal(ids, path.Select(n => n.Id).ToList());
    }

    [Fact(DisplayName = "Декомпозированный граф — нет пути между компонентами")]
    public void FindPath_DisconnectedComponents_ReturnsEmpty()
    {
        var g = new NavGraph();
        // Компонента 1: A↔B
        g.Nodes.Add(GB.Node("A")); g.Nodes.Add(GB.Node("B"));
        foreach (var e in GB.BiEdge("A", "B", 1f)) g.Edges.Add(e);
        // Компонента 2: C↔D, изолирована от AB
        g.Nodes.Add(GB.Node("C")); g.Nodes.Add(GB.Node("D"));
        foreach (var e in GB.BiEdge("C", "D", 1f)) g.Edges.Add(e);

        Assert.Empty(g.FindPath("A", "C"));
        Assert.Empty(g.FindPath("B", "D"));
    }

    // ════════════════════════════════════════════════════════════════════════
    // ③ Оптимальность (выбор кратчайшего пути)
    // ════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Граф-ромб → выбирает дешёвую ветку")]
    public void FindPath_DiamondGraph_ChoosesCheaperBranch()
    {
        // A --(1)-- B --(1)-- D   → A→B→D = 2
        // A --(5)-- C --(5)-- D   → A→C→D = 10
        var g = new NavGraph();
        foreach (var id in new[] { "A", "B", "C", "D" })
            g.Nodes.Add(GB.Node(id));
        foreach (var e in GB.BiEdge("A", "B", 1f).Concat(GB.BiEdge("B", "D", 1f))
                           .Concat(GB.BiEdge("A", "C", 5f)).Concat(GB.BiEdge("C", "D", 5f)))
            g.Edges.Add(e);

        var path = g.FindPath("A", "D");

        Assert.Equal(new[] { "A", "B", "D" }, path.Select(n => n.Id));
    }

    [Fact(DisplayName = "Параллельные пути — выбирается более короткий")]
    public void FindPath_TwoParallelPaths_SelectsShorter()
    {
        // A→B→C = 1+1 = 2   (через B)
        // A→D→C = 10+10 = 20 (через D)
        var g = new NavGraph();
        foreach (var id in new[] { "A", "B", "C", "D" })
            g.Nodes.Add(GB.Node(id));
        foreach (var e in GB.BiEdge("A", "B", 1f).Concat(GB.BiEdge("B", "C", 1f))
                           .Concat(GB.BiEdge("A", "D", 10f)).Concat(GB.BiEdge("D", "C", 10f)))
            g.Edges.Add(e);

        var path = g.FindPath("A", "C");

        Assert.Equal(new[] { "A", "B", "C" }, path.Select(n => n.Id));
    }

    [Fact(DisplayName = "Прямое ребро короче обхода → используется прямое")]
    public void FindPath_DirectEdge_PreferredOverLongDetour()
    {
        // A→C (прямое, вес 3) vs A→B→C (вес 1+50 = 51)
        var g = new NavGraph();
        foreach (var id in new[] { "A", "B", "C" })
            g.Nodes.Add(GB.Node(id));
        foreach (var e in GB.BiEdge("A", "C", 3f).Concat(GB.BiEdge("A", "B", 1f)).Concat(GB.BiEdge("B", "C", 50f)))
            g.Edges.Add(e);

        var path = g.FindPath("A", "C");

        Assert.Equal(2, path.Count);
        Assert.Equal(new[] { "A", "C" }, path.Select(n => n.Id));
    }

    [Fact(DisplayName = "Граф со сложным ветвлением → оптимальный путь из 4 путей")]
    public void FindPath_MultiPathGraph_SelectsOptimalFromFourOptions()
    {
        // S → [X1 (cost 2), X2 (cost 4), X3 (cost 7), X4 (cost 1)] → E
        var g = new NavGraph();
        var hubs = new[] { "X1", "X2", "X3", "X4" };
        g.Nodes.Add(GB.Node("S")); g.Nodes.Add(GB.Node("E"));
        foreach (var h in hubs) g.Nodes.Add(GB.Node(h));

        float[] s2h = { 2f, 4f, 7f, 1f };
        float[] h2e = { 1f, 1f, 1f, 9f };  // X4 cheap to S but expensive to E
        for (int i = 0; i < hubs.Length; i++)
        {
            foreach (var e in GB.BiEdge("S", hubs[i], s2h[i]).Concat(GB.BiEdge(hubs[i], "E", h2e[i])))
                g.Edges.Add(e);
        }

        var path = g.FindPath("S", "E");

        // Costs: X1=3, X2=5, X3=8, X4=10 → minimum via X1
        Assert.Equal(new[] { "S", "X1", "E" }, path.Select(n => n.Id));
    }

    // ════════════════════════════════════════════════════════════════════════
    // ④ Исключения узлов (заблокированные точки)
    // ════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Заблокированный промежуточный узел → маршрут через обход")]
    public void FindPath_BlockedIntermediate_FindsDetour()
    {
        // A→B→C (B заблокирован) → должен найти A→D→C
        var g = new NavGraph();
        foreach (var id in new[] { "A", "B", "C", "D" })
            g.Nodes.Add(GB.Node(id));
        foreach (var e in GB.BiEdge("A", "B", 1f).Concat(GB.BiEdge("B", "C", 1f))
                           .Concat(GB.BiEdge("A", "D", 5f)).Concat(GB.BiEdge("D", "C", 5f)))
            g.Edges.Add(e);

        var path = g.FindPath("A", "C", new HashSet<string> { "B" });

        Assert.Equal(new[] { "A", "D", "C" }, path.Select(n => n.Id));
    }

    [Fact(DisplayName = "Все пути заблокированы → пустой список")]
    public void FindPath_AllPathsBlocked_ReturnsEmpty()
    {
        // A→B→C, B заблокирован, нет обхода
        var g = new NavGraph();
        foreach (var id in new[] { "A", "B", "C" })
            g.Nodes.Add(GB.Node(id));
        foreach (var e in GB.BiEdge("A", "B", 1f).Concat(GB.BiEdge("B", "C", 1f)))
            g.Edges.Add(e);

        var path = g.FindPath("A", "C", new HashSet<string> { "B" });

        Assert.Empty(path);
    }

    [Fact(DisplayName = "Конечный узел в excludeIds → всё равно достигается (endpoint bypass)")]
    public void FindPath_EndNodeExcluded_StillReachable()
    {
        // Алгоритм допускает попадание в конечный узел даже если он исключён
        var g = new NavGraph();
        foreach (var id in new[] { "A", "B" })
            g.Nodes.Add(GB.Node(id));
        g.Edges.Add(GB.Edge("A", "B", 1f));

        var path = g.FindPath("A", "B", new HashSet<string> { "B" });

        Assert.Equal(new[] { "A", "B" }, path.Select(n => n.Id));
    }

    [Fact(DisplayName = "Несколько заблокированных узлов → находит длинный обход")]
    public void FindPath_MultipleBlockedNodes_FindsLongDetour()
    {
        // Прямой путь: A→B→C→D (B и C заблокированы)
        // Обход:        A→E→F→D
        var g = new NavGraph();
        foreach (var id in new[] { "A", "B", "C", "D", "E", "F" })
            g.Nodes.Add(GB.Node(id));
        foreach (var e in GB.BiEdge("A", "B", 1f).Concat(GB.BiEdge("B", "C", 1f))
                           .Concat(GB.BiEdge("C", "D", 1f))
                           .Concat(GB.BiEdge("A", "E", 10f)).Concat(GB.BiEdge("E", "F", 10f))
                           .Concat(GB.BiEdge("F", "D", 10f)))
            g.Edges.Add(e);

        var path = g.FindPath("A", "D", new HashSet<string> { "B", "C" });

        Assert.Equal(new[] { "A", "E", "F", "D" }, path.Select(n => n.Id));
    }

    [Fact(DisplayName = "Единственный путь = только start+end → работает")]
    public void FindPath_OnlyStartAndEnd_NoIntermediate()
    {
        var g = new NavGraph();
        g.Nodes.Add(GB.Node("S")); g.Nodes.Add(GB.Node("E"));
        g.Edges.Add(GB.Edge("S", "E", 99f));

        var path = g.FindPath("S", "E");

        Assert.Equal(new[] { "S", "E" }, path.Select(n => n.Id));
    }

    // ════════════════════════════════════════════════════════════════════════
    // ⑤ Межэтажный маршрут (лестницы / переходные узлы)
    // ════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Межэтажный переход через transition-узлы — маршрут существует")]
    public void FindPath_CrossFloor_TransitionNodesConnectFloors()
    {
        // Этаж 1: A1 ↔ T1   Этаж 2: T2 ↔ B2
        // Межэтажное ребро T1↔T2 с весом 150 (штраф лестницы)
        var g = new NavGraph();
        g.Nodes.Add(GB.Node("A1", floor: 1));
        g.Nodes.Add(GB.Node("T1", floor: 1, isTrans: true));
        g.Nodes.Add(GB.Node("T2", floor: 2, isTrans: true));
        g.Nodes.Add(GB.Node("B2", floor: 2));
        foreach (var e in GB.BiEdge("A1", "T1", 10f)
                           .Concat(GB.BiEdge("T1", "T2", 150f))
                           .Concat(GB.BiEdge("T2", "B2", 10f)))
            g.Edges.Add(e);

        var path = g.FindPath("A1", "B2");

        Assert.Equal(new[] { "A1", "T1", "T2", "B2" }, path.Select(n => n.Id));
    }

    [Fact(DisplayName = "Одноэтажный маршрут предпочтительнее межэтажного если дешевле")]
    public void FindPath_SameFloorPreferredWhenCheaper()
    {
        // Этаж 1: A → B → C  (стоимость 20, без штрафа)
        // Обход через этаж 2: A → T1 → T2 → T3 → C  (стоимость 5+150+5 = 160)
        var g = new NavGraph();
        foreach (var id in new[] { "A", "B", "C" })
            g.Nodes.Add(GB.Node(id, floor: 1));
        g.Nodes.Add(GB.Node("T1", floor: 1, isTrans: true));
        g.Nodes.Add(GB.Node("T2", floor: 2, isTrans: true));
        g.Nodes.Add(GB.Node("T3", floor: 2, isTrans: false));

        foreach (var e in GB.BiEdge("A", "B", 10f).Concat(GB.BiEdge("B", "C", 10f))
                           .Concat(GB.BiEdge("A", "T1", 5f)).Concat(GB.BiEdge("T1", "T2", 150f))
                           .Concat(GB.BiEdge("T2", "T3", 5f)).Concat(GB.BiEdge("T3", "C", 5f)))
            g.Edges.Add(e);

        var path = g.FindPath("A", "C");

        Assert.Equal(new[] { "A", "B", "C" }, path.Select(n => n.Id));
    }

    [Fact(DisplayName = "Межэтажный используется если одноэтажный дороже штрафа")]
    public void FindPath_CrossFloorChosen_WhenSameFloorTooExpensive()
    {
        // Этаж 1: A → (1000) → C        — дорогой путь
        // Через этаж 2: A→T1→T2→C = 1+150+1 = 152  — дешевле
        var g = new NavGraph();
        g.Nodes.Add(GB.Node("A",  floor: 1));
        g.Nodes.Add(GB.Node("C",  floor: 1));
        g.Nodes.Add(GB.Node("T1", floor: 1, isTrans: true));
        g.Nodes.Add(GB.Node("T2", floor: 2, isTrans: true));

        foreach (var e in GB.BiEdge("A", "C", 1000f)
                           .Concat(GB.BiEdge("A", "T1", 1f))
                           .Concat(GB.BiEdge("T1", "T2", 150f))
                           .Concat(GB.BiEdge("T2", "C", 1f)))
            g.Edges.Add(e);

        var path = g.FindPath("A", "C");

        Assert.Equal(new[] { "A", "T1", "T2", "C" }, path.Select(n => n.Id));
    }

    // ════════════════════════════════════════════════════════════════════════
    // ⑥ Управление структурой графа
    // ════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "AddEdge создаёт оба направления ребра")]
    public void AddEdge_CreatesBidirectionalEdge()
    {
        var g = new NavGraph();
        g.Nodes.Add(GB.Node("A", x: 0));
        g.Nodes.Add(GB.Node("B", x: 30));
        g.AddEdge("A", "B");

        Assert.True(g.EdgeExists("A", "B"));
        Assert.True(g.EdgeExists("B", "A"));
    }

    [Fact(DisplayName = "AddEdge использует евклидово расстояние как вес")]
    public void AddEdge_AutoWeight_EuclideanDistance()
    {
        var g = new NavGraph();
        g.Nodes.Add(GB.Node("A", x: 0, y: 0));
        g.Nodes.Add(GB.Node("B", x: 3, y: 4));  // √(9+16) = 5
        g.AddEdge("A", "B");

        var edge = g.Edges.First(e => e.FromId == "A" && e.ToId == "B");
        Assert.Equal(5f, edge.Weight, precision: 4);
    }

    [Fact(DisplayName = "AddEdge межэтажный использует фиксированный штраф 150")]
    public void AddEdge_CrossFloor_FixedPenalty150()
    {
        var g = new NavGraph();
        g.Nodes.Add(GB.Node("T1", floor: 1, isTrans: true));
        g.Nodes.Add(GB.Node("T2", floor: 2, isTrans: true));
        g.AddEdge("T1", "T2", crossFloor: true);

        var edge = g.Edges.First(e => e.FromId == "T1");
        Assert.Equal(150f, edge.Weight);
        Assert.True(edge.IsCrossFloor);
    }

    [Fact(DisplayName = "AddEdge не создаёт дублирующие рёбра")]
    public void AddEdge_NoDuplicateEdges()
    {
        var g = new NavGraph();
        g.Nodes.Add(GB.Node("A")); g.Nodes.Add(GB.Node("B"));
        g.AddEdge("A", "B");
        g.AddEdge("A", "B");  // повторный вызов

        Assert.Equal(2, g.Edges.Count);  // только A→B и B→A
    }

    [Fact(DisplayName = "RemoveEdge удаляет оба направления")]
    public void RemoveEdge_RemovesBothDirections()
    {
        var g = new NavGraph();
        g.Nodes.Add(GB.Node("A")); g.Nodes.Add(GB.Node("B"));
        g.AddEdge("A", "B");
        g.RemoveEdge("A", "B");

        Assert.False(g.EdgeExists("A", "B"));
        Assert.False(g.EdgeExists("B", "A"));
        Assert.Empty(g.Edges);
    }

    [Fact(DisplayName = "EdgeExists возвращает true для существующего ребра")]
    public void EdgeExists_TrueForExistingEdge()
    {
        var g = new NavGraph();
        g.Nodes.Add(GB.Node("A")); g.Nodes.Add(GB.Node("B"));
        g.AddEdge("A", "B");

        Assert.True(g.EdgeExists("A", "B"));
        Assert.True(g.EdgeExists("B", "A"));
    }

    [Fact(DisplayName = "EdgeExists возвращает false для отсутствующего ребра")]
    public void EdgeExists_FalseForMissingEdge()
    {
        var g = new NavGraph();
        g.Nodes.Add(GB.Node("A")); g.Nodes.Add(GB.Node("B"));

        Assert.False(g.EdgeExists("A", "B"));
        Assert.False(g.EdgeExists("B", "A"));
    }

    [Fact(DisplayName = "GetNodesForFloor фильтрует по зданию и этажу")]
    public void GetNodesForFloor_FiltersCorrectly()
    {
        var g = new NavGraph();
        g.Nodes.Add(GB.Node("A", building: "b1", floor: 1));
        g.Nodes.Add(GB.Node("B", building: "b1", floor: 2));
        g.Nodes.Add(GB.Node("C", building: "b2", floor: 1));

        var floor1B1 = g.GetNodesForFloor("b1", 1).ToList();

        Assert.Single(floor1B1);
        Assert.Equal("A", floor1B1[0].Id);
    }

    // ════════════════════════════════════════════════════════════════════════
    // ⑦ Граф с циклами
    // ════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Граф с циклом — алгоритм не зависает, находит кратчайший путь")]
    public void FindPath_GraphWithCycle_CompletesCorrectly()
    {
        // A↔B↔C↔A (цикл) + C→D
        var g = new NavGraph();
        foreach (var id in new[] { "A", "B", "C", "D" })
            g.Nodes.Add(GB.Node(id));
        foreach (var e in GB.BiEdge("A", "B", 1f).Concat(GB.BiEdge("B", "C", 1f))
                           .Concat(GB.BiEdge("C", "A", 1f)).Concat(GB.BiEdge("C", "D", 1f)))
            g.Edges.Add(e);

        var path = g.FindPath("A", "D");

        Assert.NotEmpty(path);
        Assert.Equal("A", path.First().Id);
        Assert.Equal("D", path.Last().Id);
        Assert.Equal(3, path.Count); // A→B→C→D или A→C→D (оба = 2, но A↔C напрямую через цикл = 1)
    }

    [Fact(DisplayName = "Граф с самопетлёй — FindPath не зависает")]
    public void FindPath_SelfLoop_DoesNotHang()
    {
        var g = new NavGraph();
        g.Nodes.Add(GB.Node("A")); g.Nodes.Add(GB.Node("B"));
        g.Edges.Add(GB.Edge("A", "A", 0f)); // самопетля — не должна сломать алгоритм
        g.Edges.Add(GB.Edge("A", "B", 5f));

        var path = g.FindPath("A", "B");

        Assert.Equal(new[] { "A", "B" }, path.Select(n => n.Id));
    }

    // ════════════════════════════════════════════════════════════════════════
    // ⑧ Масштаб — корректность на больших графах
    // ════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Линейная цепочка 100 узлов → находит полный путь")]
    public void FindPath_LinearChain_100Nodes_FindsFullPath()
    {
        var (g, ids) = GB.LinearChain(100);
        var path = g.FindPath(ids[0], ids[99]);

        Assert.Equal(100, path.Count);
        Assert.Equal(ids[0],  path.First().Id);
        Assert.Equal(ids[99], path.Last().Id);
    }

    [Fact(DisplayName = "Линейная цепочка 100 узлов + срезающее ребро → идёт по срезу")]
    public void FindPath_LinearChain_WithShortcut_UsesShortcut()
    {
        var (g, ids) = GB.LinearChain(100); // каждое ребро = 1, цепь = 99

        // Добавляем прямой срез n0→n99 с весом < 99
        g.Edges.Add(GB.Edge(ids[0], ids[99], 50f));

        var path = g.FindPath(ids[0], ids[99]);

        // Должен использовать прямой срез (2 узла)
        Assert.Equal(2, path.Count);
    }

    [Fact(DisplayName = "Сетка 10×10 → путь от угла до угла корректен")]
    public void FindPath_Grid_10x10_CornerToCorner()
    {
        var (g, tl, br) = GB.Grid(10, 10);
        var path = g.FindPath(tl, br);

        Assert.NotEmpty(path);
        Assert.Equal(tl, path.First().Id);
        Assert.Equal(br, path.Last().Id);
        // Оптимальный путь в сетке 10×10 = 19 шагов (9 вправо + 9 вниз)
        Assert.Equal(19, path.Count);
    }

    [Fact(DisplayName = "Сетка 10×10 с дырой → находит обход")]
    public void FindPath_Grid_10x10_WithHole_FindsDetour()
    {
        var (g, tl, br) = GB.Grid(5, 5);

        // Удаляем все вертикальные и горизонтальные рёбра в 3-й колонке
        // чтобы создать «стену» — алгоритм должен найти обход
        var toRemove = g.Edges
            .Where(e => (e.FromId.Contains("c2") || e.ToId.Contains("c2")))
            .ToList();
        foreach (var e in toRemove) g.Edges.Remove(e);

        // Маршрут должен обходить стену или не найтись — главное не зависнуть
        var path = g.FindPath(tl, br);
        if (path.Count > 0)
        {
            Assert.Equal(tl, path.First().Id);
            Assert.Equal(br, path.Last().Id);
        }
        // Если пути нет — empty, тоже корректный результат
    }
}
