using System.Text.Json;
using IndoorNav.Models;
using Microsoft.Maui.Storage;

namespace IndoorNav.Services;

public class NavGraphService
{
    /// <summary>
    /// Получает путь к файлу navgraph.json. На iOS/Android используется AppDataDirectory,
    /// на Desktop пытается найти файл в проекте.
    /// </summary>
    private static string GetNavGraphPath()
    {
#if IOS || ANDROID
        // На мобильных платформах используем AppDataDirectory (безопасное место для данных)
        var appDataDir = FileSystem.AppDataDirectory;
        return Path.Combine(appDataDir, "navgraph.json");
#else
        // На Desktop пытаемся найти файл в Resources/Raw
        var basePath = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(basePath);
        
        // Поднимаемся вверх пока не найдем папку с IndoorNav.csproj
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "IndoorNav.csproj")))
        {
            dir = dir.Parent;
        }
        
        if (dir == null) dir = new DirectoryInfo(basePath);
        return Path.Combine(dir.FullName, "Resources", "Raw", "navgraph.json");
#endif
    }

    private static readonly string FilePath = GetNavGraphPath();

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
                var localJson = await File.ReadAllTextAsync(FilePath);
                _graph = JsonSerializer.Deserialize<NavGraph>(localJson, JsonOpts) ?? new NavGraph();
                MigrateWaypoints();
            }
            else
            {
#if IOS || ANDROID
                // На мобильных устройствах попытаемся загрузить из бандла
                try
                {
                    using var stream = await FileSystem.OpenAppPackageFileAsync("navgraph.json");
                    using var reader = new StreamReader(stream);
                    var bundledJson = await reader.ReadToEndAsync();
                    _graph = JsonSerializer.Deserialize<NavGraph>(bundledJson, JsonOpts) ?? new NavGraph();
                    MigrateWaypoints();
                    
                    // Сохраняем локально для последующего использования
                    await SaveAsync();
                }
                catch
                {
                    _graph = new NavGraph();
                }
#endif
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
    /// Также мигрирует устаревшее поле Boundary → Boundaries[0].
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

            // Миграция: перенести единственный полигон Boundary → Boundaries[0]
            if (n.Boundary != null && n.Boundaries == null)
            {
                n.Boundaries = new List<List<float[]>> { n.Boundary };
                n.Boundary   = null;
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
