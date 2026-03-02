using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IndoorNav.Models;

namespace IndoorNav.Services;

public class NavGraphService
{
    private static readonly string FilePath =
        Path.Combine(FileSystem.AppDataDirectory, "navgraph.json");

    // Хранит MD5 последнего известного бандла — если бандл изменился (новый git pull),
    // локальный кэш автоматически заменяется новым бандлом без ручных правок.
    private static readonly string BundleHashPath =
        Path.Combine(FileSystem.AppDataDirectory, "bundle_hash.txt");

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
            // Читаем бандл и считаем его MD5-хеш
            string? bundledJson = null;
            string bundleHash   = string.Empty;
            try
            {
                using var stream = await FileSystem.OpenAppPackageFileAsync("navgraph.json");
                using var reader = new StreamReader(stream);
                bundledJson = await reader.ReadToEndAsync();
                bundleHash  = ComputeMd5(bundledJson);
            }
            catch { /* бандл недоступен — продолжаем с локальным */ }

            // Хеш, который был при предыдущем запуске
            string savedHash = File.Exists(BundleHashPath)
                ? await File.ReadAllTextAsync(BundleHashPath)
                : string.Empty;

            bool bundleChanged = !string.IsNullOrEmpty(bundleHash) && bundleHash != savedHash;

            if (bundleChanged || !File.Exists(FilePath))
            {
                // Бандл изменился (новый git pull) или локального файла нет — берём бандл
                if (bundledJson != null)
                {
                    System.Diagnostics.Debug.WriteLine(
                        bundleChanged ? "Bundle changed, replacing local cache." : "No local cache, loading bundle.");
                    _graph = JsonSerializer.Deserialize<NavGraph>(bundledJson, JsonOpts) ?? new NavGraph();
                    MigrateWaypoints();
                    await SaveAsync();
                    await File.WriteAllTextAsync(BundleHashPath, bundleHash);
                }
                else
                {
                    _graph = new NavGraph();
                }
            }
            else
            {
                // Бандл не изменился — используем локальный файл (с правками администратора)
                var localJson = await File.ReadAllTextAsync(FilePath);
                _graph = JsonSerializer.Deserialize<NavGraph>(localJson, JsonOpts) ?? new NavGraph();
                if (MigrateWaypoints()) await SaveAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NavGraph load error: {ex.Message}");
            _graph = new NavGraph();
        }
    }

    private static string ComputeMd5(string text)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes);
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
        if (File.Exists(BundleHashPath))
            File.Delete(BundleHashPath);
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
