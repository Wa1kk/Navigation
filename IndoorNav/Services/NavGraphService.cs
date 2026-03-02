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
            // Всегда читаем бандл, чтобы знать его DataVersion
            NavGraph? bundled = null;
            try
            {
                using var stream = await FileSystem.OpenAppPackageFileAsync("navgraph.json");
                using var reader = new StreamReader(stream);
                var bundledJson = await reader.ReadToEndAsync();
                bundled = JsonSerializer.Deserialize<NavGraph>(bundledJson, JsonOpts);
            }
            catch { /* бандл недоступен — продолжаем с локальным */ }

            if (File.Exists(FilePath))
            {
                // Локальный файл существует — проверяем версию
                var localJson = await File.ReadAllTextAsync(FilePath);
                var local = JsonSerializer.Deserialize<NavGraph>(localJson, JsonOpts) ?? new NavGraph();

                if (bundled != null && bundled.DataVersion > local.DataVersion)
                {
                    // Бандл новее — используем бандл и перезаписываем локальный
                    System.Diagnostics.Debug.WriteLine(
                        $"Bundle DataVersion ({bundled.DataVersion}) > local ({local.DataVersion}), switching to bundle.");
                    _graph = bundled;
                    MigrateWaypoints();
                    await SaveAsync();
                }
                else
                {
                    _graph = local;
                    if (MigrateWaypoints()) await SaveAsync();
                }
            }
            else
            {
                // Локального файла нет — берём бандл
                _graph = bundled ?? new NavGraph();
                MigrateWaypoints();
                await SaveAsync();
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
