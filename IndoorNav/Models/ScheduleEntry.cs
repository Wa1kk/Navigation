namespace IndoorNav.Models;

/// <summary>
/// Associates a room (node) with a group for a specific lesson time slot.
/// </summary>
public class ScheduleEntry
{
    public string Id         { get; set; } = Guid.NewGuid().ToString();
    public string RoomNodeId { get; set; } = string.Empty;   // NavNode.Id of the classroom
    public string RoomName   { get; set; } = string.Empty;   // cached for display
    public string GroupName  { get; set; } = string.Empty;
    public string GroupId    { get; set; } = string.Empty;
    public int    PersonCount{ get; set; }
    /// <summary>Recurring day of week: 0=Sunday, 1=Monday, ..., 5=Friday, 6=Saturday.</summary>
    public int DayOfWeek { get; set; } = 5; // default Friday
    // Time slots — each covers HH:mm – HH:mm on the given day
    public List<TimeSlot> TimeSlots { get; set; } = new();

    private static readonly string[] _ruDayNames =
        { "Понедельник", "Вторник", "Среда", "Четверг", "Пятница", "Суббота", "Воскресенье" };

    public string DisplayDate => _ruDayNames[Math.Clamp(DayOfWeek, 0, 6)];
}

public class TimeSlot
{
    public string StartTime { get; set; } = "08:00"; // HH:mm
    public string EndTime   { get; set; } = "09:30";

    public bool IsActiveNow()
    {
        var now = TimeOnly.FromDateTime(DateTime.Now);
        if (!TimeOnly.TryParse(StartTime, out var start)) return false;
        if (!TimeOnly.TryParse(EndTime,   out var end))   return false;
        return now >= start && now <= end;
    }

    public override string ToString() => $"{StartTime}–{EndTime}";
}
