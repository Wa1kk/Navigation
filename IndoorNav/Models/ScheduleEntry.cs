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
    public int    PersonCount{ get; set; }
    // Daily time slots — each entry covers HH:mm – HH:mm
    public List<TimeSlot> TimeSlots { get; set; } = new();
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
