using System.Text.Json;
using IndoorNav.Models;

namespace IndoorNav.Services;

public class NavGraphService
{
    private static readonly string FilePath =
        Path.Combine(FileSystem.AppDataDirectory, "navgraph.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true
    };

    private NavGraph _graph = new();

    public NavGraph Graph => _graph;

    public async Task LoadAsync()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                // Локальный файл (отредактированный пользователем)
                var json = await File.ReadAllTextAsync(FilePath);
                _graph = JsonSerializer.Deserialize<NavGraph>(json, JsonOpts) ?? new NavGraph();
                if (MigrateWaypoints()) await SaveAsync();
            }
            else
            {
                // Начальные данные, вшитые в бандл при сборке
                try
                {
                    using var stream = await FileSystem.OpenAppPackageFileAsync("navgraph.json");
                    using var reader = new StreamReader(stream);
                    var json = await reader.ReadToEndAsync();
                    _graph = JsonSerializer.Deserialize<NavGraph>(json, JsonOpts) ?? new NavGraph();
                    MigrateWaypoints();
                    // Сохраняем в локальный файл, чтобы следующий запуск работал без бандла
                    await SaveAsync();
                }
                catch
                {
                    _graph = new NavGraph();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NavGraph load error: {ex.Message}");
            _graph = new NavGraph();
        }
    }

    /// <summary>
    /// Исправляет данные: только узлы с именем "wp*" являются waypoint-ами.
    /// Все остальные не-транзитные узлы — видимые точки (аудитории и т.п.).
    /// Запускается при каждой загрузке чтобы исправить ошибку предыдущей миграции.
    /// </summary>
    private bool MigrateWaypoints()
    {
        bool anyChange = false;

        foreach (var n in _graph.Nodes)
        {
            bool shouldBeWaypoint = !n.IsTransition &&
                                    n.Name.StartsWith("wp", StringComparison.OrdinalIgnoreCase);

            if (n.IsWaypoint != shouldBeWaypoint && !n.IsTransition)
            {
                n.IsWaypoint = shouldBeWaypoint;
                anyChange    = true;
            }
        }

        return anyChange;
    }

    public async Task SaveAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(_graph, JsonOpts);
            await File.WriteAllTextAsync(FilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NavGraph save error: {ex.Message}");
        }
    }

    // ---- Shortcut helpers used by ViewModels ----

    public async Task ResetAsync()
    {
        _graph = new NavGraph();
        if (File.Exists(FilePath))
            File.Delete(FilePath);
        await Task.CompletedTask;
    }

    public void AddNode(NavNode node) => _graph.Nodes.Add(node);

    public void RemoveNode(string id)
    {
        _graph.Nodes.RemoveAll(n => n.Id == id);
        _graph.Edges.RemoveAll(e => e.FromId == id || e.ToId == id);
    }

    public void AddEdge(string fromId, string toId, bool crossFloor = false) =>
        _graph.AddEdge(fromId, toId, crossFloor);

    public void RemoveEdge(string fromId, string toId) =>
        _graph.RemoveEdge(fromId, toId);
}
