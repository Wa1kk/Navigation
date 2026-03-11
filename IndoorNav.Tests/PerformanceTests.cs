using System.Diagnostics;
using IndoorNav.Models;
using IndoorNav.Services;
using IndoorNav.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace IndoorNav.Tests;

/// <summary>
/// Тесты производительности алгоритмов навигации.
/// Проверяют, что алгоритм укладывается в приемлемые временные рамки
/// на графах разного масштаба и сложности.
/// </summary>
public class PerformanceTests(ITestOutputHelper output)
{
    // ════════════════════════════════════════════════════════════════════════
    // ① Тривиальные случаи — мгновенный ответ
    // ════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Старт == конец → завершается менее чем за 1 мс")]
    public void FindPath_StartEqualsEnd_Under1ms()
    {
        var (graph, ids) = GB.LinearChain(50);
        var sw = Stopwatch.StartNew();
        graph.FindPath(ids[0], ids[0]);
        sw.Stop();
        output.WriteLine($"StartEqualsEnd: {sw.Elapsed.TotalMilliseconds:F3} ms");
        Assert.True(sw.Elapsed.TotalMilliseconds < 1.0, $"Ожидалось < 1 мс, получено {sw.Elapsed.TotalMilliseconds:F2} мс");
    }

    [Fact(DisplayName = "Два узла без рёбер → возврат null/пустой путь за < 1 мс")]
    public void FindPath_Disconnected2Nodes_Under1ms()
    {
        var g = new NavGraph();
        g.Nodes.Add(GB.Node("A"));
        g.Nodes.Add(GB.Node("B"));
        var sw = Stopwatch.StartNew();
        g.FindPath("A", "B");
        sw.Stop();
        output.WriteLine($"Disconnected 2 nodes: {sw.Elapsed.TotalMilliseconds:F3} ms");
        Assert.True(sw.Elapsed.TotalMilliseconds < 1.0);
    }

    // ════════════════════════════════════════════════════════════════════════
    // ② Линейные цепочки — малые и средние
    // ════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Линейная цепочка 100 узлов → < 10 мс")]
    public void FindPath_LinearChain100_Under10ms()
    {
        var (graph, ids) = GB.LinearChain(100);
        var sw = Stopwatch.StartNew();
        graph.FindPath(ids.First(), ids.Last());
        sw.Stop();
        output.WriteLine($"LinearChain 100: {sw.Elapsed.TotalMilliseconds:F3} ms");
        Assert.True(sw.Elapsed.TotalMilliseconds < 10.0);
    }

    [Fact(DisplayName = "Линейная цепочка 500 узлов → < 50 мс")]
    public void FindPath_LinearChain500_Under50ms()
    {
        var (graph, ids) = GB.LinearChain(500);
        var sw = Stopwatch.StartNew();
        graph.FindPath(ids.First(), ids.Last());
        sw.Stop();
        output.WriteLine($"LinearChain 500: {sw.Elapsed.TotalMilliseconds:F3} ms");
        Assert.True(sw.Elapsed.TotalMilliseconds < 50.0);
    }

    [Fact(DisplayName = "Линейная цепочка 1000 узлов → < 100 мс")]
    public void FindPath_LinearChain1000_Under100ms()
    {
        var (graph, ids) = GB.LinearChain(1000);
        var sw = Stopwatch.StartNew();
        graph.FindPath(ids.First(), ids.Last());
        sw.Stop();
        output.WriteLine($"LinearChain 1000: {sw.Elapsed.TotalMilliseconds:F3} ms");
        Assert.True(sw.Elapsed.TotalMilliseconds < 100.0);
    }

    // ════════════════════════════════════════════════════════════════════════
    // ③ Сетки — масштабируемость O(N log N)
    // ════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Сетка 10×10 (100 узлов) угол→угол → < 20 мс")]
    public void FindPath_Grid10x10_Under20ms()
    {
        var (graph, startId, endId) = GB.Grid(10, 10);
        var sw = Stopwatch.StartNew();
        graph.FindPath(startId, endId);
        sw.Stop();
        output.WriteLine($"Grid 10x10: {sw.Elapsed.TotalMilliseconds:F3} ms");
        Assert.True(sw.Elapsed.TotalMilliseconds < 20.0);
    }

    [Fact(DisplayName = "Сетка 20×20 (400 узлов) угол→угол → < 100 мс")]
    public void FindPath_Grid20x20_Under100ms()
    {
        var (graph, startId, endId) = GB.Grid(20, 20);
        var sw = Stopwatch.StartNew();
        graph.FindPath(startId, endId);
        sw.Stop();
        output.WriteLine($"Grid 20x20: {sw.Elapsed.TotalMilliseconds:F3} ms");
        Assert.True(sw.Elapsed.TotalMilliseconds < 100.0);
    }

    [Fact(DisplayName = "Сетка 30×30 (900 узлов) угол→угол → < 200 мс")]
    public void FindPath_Grid30x30_Under200ms()
    {
        var (graph, startId, endId) = GB.Grid(30, 30);
        var sw = Stopwatch.StartNew();
        graph.FindPath(startId, endId);
        sw.Stop();
        output.WriteLine($"Grid 30x30: {sw.Elapsed.TotalMilliseconds:F3} ms");
        Assert.True(sw.Elapsed.TotalMilliseconds < 200.0);
    }

    // ════════════════════════════════════════════════════════════════════════
    // ④ Плотный граф (dense) — наихудший случай по числу рёбер
    // ════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Плотный граф 100 узлов (~5000 рёбер) → < 200 мс")]
    public void FindPath_DenseGraph100Nodes_Under200ms()
    {
        // Каждый узел соединён с ~50 соседями (≈5000 направленных рёбер)
        var g = new NavGraph();
        const int N = 100;
        for (int i = 0; i < N; i++)
            g.Nodes.Add(GB.Node($"N{i}"));

        var rng = new Random(42);
        for (int i = 0; i < N; i++)
            for (int j = i + 1; j < N; j++)
                if (rng.NextDouble() < 0.5)
                    foreach (var e in GB.BiEdge($"N{i}", $"N{j}", (float)(rng.NextDouble() * 100 + 1)))
                        g.Edges.Add(e);

        var sw = Stopwatch.StartNew();
        g.FindPath("N0", $"N{N - 1}");
        sw.Stop();
        output.WriteLine($"Dense graph 100 nodes: {sw.Elapsed.TotalMilliseconds:F3} ms");
        Assert.True(sw.Elapsed.TotalMilliseconds < 200.0);
    }

    [Fact(DisplayName = "Плотный граф 200 узлов → < 500 мс")]
    public void FindPath_DenseGraph200Nodes_Under500ms()
    {
        var g = new NavGraph();
        const int N = 200;
        for (int i = 0; i < N; i++)
            g.Nodes.Add(GB.Node($"N{i}"));

        var rng = new Random(7);
        for (int i = 0; i < N; i++)
            for (int j = i + 1; j < N; j++)
                if (rng.NextDouble() < 0.5)
                    foreach (var e in GB.BiEdge($"N{i}", $"N{j}", (float)(rng.NextDouble() * 100 + 1)))
                        g.Edges.Add(e);

        var sw = Stopwatch.StartNew();
        g.FindPath("N0", $"N{N - 1}");
        sw.Stop();
        output.WriteLine($"Dense graph 200 nodes: {sw.Elapsed.TotalMilliseconds:F3} ms");
        Assert.True(sw.Elapsed.TotalMilliseconds < 500.0);
    }

    // ════════════════════════════════════════════════════════════════════════
    // ⑤ Серия запросов — среднее время одного вызова
    // ════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "50 случайных запросов на сетке 15×15 — среднее < 10 мс каждый")]
    public void FindPath_50RandomPaths_AverageUnder10ms()
    {
        var (graph, _, _) = GB.Grid(15, 15);
        var nodeIds = graph.Nodes.Select(n => n.Id).ToList();
        var rng = new Random(99);

        const int queries = 50;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < queries; i++)
        {
            var s = nodeIds[rng.Next(nodeIds.Count)];
            var e = nodeIds[rng.Next(nodeIds.Count)];
            graph.FindPath(s, e);
        }
        sw.Stop();

        double avgMs = sw.Elapsed.TotalMilliseconds / queries;
        output.WriteLine($"50 random on 15×15: avg {avgMs:F3} ms per query");
        Assert.True(avgMs < 10.0, $"Среднее время запроса {avgMs:F2} мс, ожидалось < 10 мс");
    }

    [Fact(DisplayName = "100 запросов на линейной цепочке 200 — среднее < 5 мс каждый")]
    public void FindPath_100Queries_LinearChain200_AverageUnder5ms()
    {
        var (graph, ids) = GB.LinearChain(200);
        var rng = new Random(13);

        const int queries = 100;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < queries; i++)
        {
            var si = rng.Next(ids.Count);
            var ei = rng.Next(ids.Count);
            graph.FindPath(ids[si], ids[ei]);
        }
        sw.Stop();

        double avgMs = sw.Elapsed.TotalMilliseconds / queries;
        output.WriteLine($"100 random on chain 200: avg {avgMs:F3} ms per query");
        Assert.True(avgMs < 5.0);
    }

    // ════════════════════════════════════════════════════════════════════════
    // ⑥ Производительность EmergencyService
    // ════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "FindNearestExit на сетке 20×20 с 4 выходами → < 100 мс")]
    public void FindNearestExit_Grid20x20_Under100ms()
    {
        var (graph, startId, _) = GB.Grid(20, 20);

        // Добавляем выходы по углам сетки (кроме стартового)
        string[] exitIds = ["N0_19", "N19_0", "N19_19"];
        foreach (var eid in exitIds)
            if (graph.Nodes.FirstOrDefault(n => n.Id == eid) is { } node)
                node.IsExit = true;

        // Добавляем хотя бы один гарантированный выход в том же здании
        var anyExit = graph.Nodes.FirstOrDefault(n => n.Id != startId);
        if (anyExit != null) anyExit.IsExit = true;

        var startNode = graph.Nodes.First(n => n.Id == startId);
        var svc = new EmergencyService();

        var sw = Stopwatch.StartNew();
        svc.FindNearestExitRoute(startNode, graph);
        sw.Stop();

        output.WriteLine($"FindNearestExit Grid 20x20: {sw.Elapsed.TotalMilliseconds:F3} ms");
        Assert.True(sw.Elapsed.TotalMilliseconds < 100.0);
    }

    [Fact(DisplayName = "FindNearestExit на линейной цепочке 500 — < 50 мс")]
    public void FindNearestExit_LinearChain500_Under50ms()
    {
        var (graph, ids) = GB.LinearChain(500);
        // Устанавливаем последний узел как выход
        graph.Nodes.Last().IsExit = true;

        var startNode = graph.Nodes.First();
        var svc = new EmergencyService();

        var sw = Stopwatch.StartNew();
        svc.FindNearestExitRoute(startNode, graph);
        sw.Stop();

        output.WriteLine($"FindNearestExit chain 500: {sw.Elapsed.TotalMilliseconds:F3} ms");
        Assert.True(sw.Elapsed.TotalMilliseconds < 50.0);
    }
}
