namespace IndoorNav.Models;

public enum UserRole { Admin, Student, Guest }

public class AuthUser
{
    public string Id          { get; set; } = Guid.NewGuid().ToString();
    public string Username    { get; set; } = string.Empty;
    public string PasswordHash{ get; set; } = string.Empty;
    public UserRole Role      { get; set; }
    public string GroupId     { get; set; } = string.Empty; // для студентов
    public string DisplayName { get; set; } = string.Empty;
}
