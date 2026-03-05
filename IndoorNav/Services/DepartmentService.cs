using System.Text.Json;
using IndoorNav.Models;

namespace IndoorNav.Services;

/// <summary>Stores departments and study groups, persisted to departments.json.</summary>
public class DepartmentService
{
    private const string FileName = "departments.json";
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private List<Department> _departments = new();

    private static string GetProjectRootPath()
    {
        var basePath = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(basePath);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "IndoorNav.csproj")))
            dir = dir.Parent;
        return dir?.FullName ?? basePath;
    }

    private string FilePath => Path.Combine(GetProjectRootPath(), "Resources", "Raw", FileName);

    public IReadOnlyList<Department> Departments => _departments;

    public async Task LoadAsync()
    {
        if (!File.Exists(FilePath)) return;
        try
        {
            var json = await File.ReadAllTextAsync(FilePath);
            _departments = JsonSerializer.Deserialize<List<Department>>(json, JsonOpts) ?? new();
            // Back-fill DepartmentId on groups in case data was saved before it was tracked
            foreach (var d in _departments)
                foreach (var g in d.Groups)
                    g.DepartmentId = d.Id;
        }
        catch { _departments = new(); }
    }

    private async Task SaveAsync()
    {
        var json = JsonSerializer.Serialize(_departments, JsonOpts);
        await File.WriteAllTextAsync(FilePath, json);
    }

    // ── Department CRUD ──────────────────────────────────────────────────────────

    public async Task<Department> AddDepartmentAsync(string name)
    {
        var dept = new Department { Name = name.Trim() };
        _departments.Add(dept);
        await SaveAsync();
        return dept;
    }

    public async Task RenameDepartmentAsync(string id, string newName)
    {
        var d = _departments.FirstOrDefault(x => x.Id == id);
        if (d == null) return;
        d.Name = newName.Trim();
        await SaveAsync();
    }

    public async Task RemoveDepartmentAsync(string id)
    {
        _departments.RemoveAll(d => d.Id == id);
        await SaveAsync();
    }

    // ── Group CRUD ───────────────────────────────────────────────────────────────

    public async Task<StudyGroup> AddGroupAsync(string departmentId, string name)
    {
        var dept = _departments.FirstOrDefault(d => d.Id == departmentId);
        if (dept == null) throw new InvalidOperationException("Department not found");
        var group = new StudyGroup { Name = name.Trim(), DepartmentId = departmentId };
        dept.Groups.Add(group);
        await SaveAsync();
        return group;
    }

    public async Task RenameGroupAsync(string departmentId, string groupId, string newName)
    {
        var dept = _departments.FirstOrDefault(d => d.Id == departmentId);
        var group = dept?.Groups.FirstOrDefault(g => g.Id == groupId);
        if (group == null) return;
        group.Name = newName.Trim();
        await SaveAsync();
    }

    public async Task RemoveGroupAsync(string departmentId, string groupId)
    {
        var dept = _departments.FirstOrDefault(d => d.Id == departmentId);
        var group = dept?.Groups.FirstOrDefault(g => g.Id == groupId);
        if (group != null) dept!.Groups.Remove(group);
        await SaveAsync();
    }

    // ── Lookup ───────────────────────────────────────────────────────────────────

    public StudyGroup? FindGroup(string groupId) =>
        _departments.SelectMany(d => d.Groups).FirstOrDefault(g => g.Id == groupId);

    public Department? FindDepartmentByGroup(string groupId) =>
        _departments.FirstOrDefault(d => d.Groups.Any(g => g.Id == groupId));

    /// <summary>All groups across all departments (for schedule pickers etc.).</summary>
    public IEnumerable<StudyGroup> AllGroups() =>
        _departments.SelectMany(d => d.Groups);
}
