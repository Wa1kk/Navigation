namespace IndoorNav.Models;

public class StudyGroup
{
    public string Id           { get; set; } = Guid.NewGuid().ToString();
    public string Name         { get; set; } = string.Empty;
    public string DepartmentId { get; set; } = string.Empty;
}

public class Department
{
    public string Id           { get; set; } = Guid.NewGuid().ToString();
    public string Name         { get; set; } = string.Empty;
    public List<StudyGroup> Groups { get; set; } = new();
}
