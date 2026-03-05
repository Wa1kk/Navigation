using System.Text.Json;
using IndoorNav.Models;

namespace IndoorNav.Services;

/// <summary>Stores and queries the class schedule (room → group → time).</summary>
public class ScheduleService
{
    private const string FileName = "schedule.json";
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private List<ScheduleEntry> _entries = new();

        private static string GetProjectRootPath()
        {
            var basePath = AppContext.BaseDirectory;
            var dir = new DirectoryInfo(basePath);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "IndoorNav.csproj")))
                dir = dir.Parent;
            return dir?.FullName ?? basePath;
        }

        private string FilePath => Path.Combine(GetProjectRootPath(), "Resources", "Raw", FileName);
        try
        {
            var json = await File.ReadAllTextAsync(FilePath);
            _entries = JsonSerializer.Deserialize<List<ScheduleEntry>>(json, JsonOpts) ?? new();
        }
        catch { _entries = new(); }
    }

    public async Task SaveAsync()
    {
        var json = JsonSerializer.Serialize(_entries, JsonOpts);
        await File.WriteAllTextAsync(FilePath, json);
    }

    public async Task AddEntryAsync(ScheduleEntry entry)
    {
        _entries.Add(entry);
        await SaveAsync();
    }

    public async Task RemoveEntryAsync(string id)
    {
        _entries.RemoveAll(e => e.Id == id);
        await SaveAsync();
    }

    public async Task ClearAllAsync()
    {
        _entries.Clear();
        await SaveAsync();
    }

    public async Task UpdateEntryAsync(ScheduleEntry entry)
    {
        var idx = _entries.FindIndex(e => e.Id == entry.Id);
        if (idx >= 0) _entries[idx] = entry;
        await SaveAsync();
    }

    private static bool EntryMatchesDate(ScheduleEntry e) =>
        e.DayOfWeek == (int)DateTime.Now.DayOfWeek;

    /// <summary>Returns the first active schedule entry for a given room right now.</summary>
    public ScheduleEntry? GetCurrentEntryForRoom(string roomNodeId) =>
        _entries.FirstOrDefault(e =>
            e.RoomNodeId == roomNodeId &&
            EntryMatchesDate(e) &&
            e.TimeSlots.Any(t => t.IsActiveNow()));

    /// <summary>All rooms currently occupied (used for emergency routing).</summary>
    public IEnumerable<ScheduleEntry> GetCurrentlyActiveEntries() =>
        _entries.Where(e => EntryMatchesDate(e) && e.TimeSlots.Any(t => t.IsActiveNow()));

    /// <summary>Returns the active entry for a group right now (for emergency auto-routing).</summary>
    public ScheduleEntry? GetCurrentEntryForGroup(string groupId) =>
        string.IsNullOrWhiteSpace(groupId) ? null :
        _entries.FirstOrDefault(e =>
            string.Equals(e.GroupId, groupId, StringComparison.OrdinalIgnoreCase) &&
            EntryMatchesDate(e) &&
            e.TimeSlots.Any(t => t.IsActiveNow()));
}
