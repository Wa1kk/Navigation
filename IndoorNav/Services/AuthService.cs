using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IndoorNav.Models;
using Microsoft.Maui.Storage;

namespace IndoorNav.Services;

/// <summary>
/// Provides user authentication with local JSON storage.
/// Users are stored in AppDataDirectory/users.json
/// Current session is persisted via Preferences.
/// </summary>
public class AuthService
{
    private const string UsersFileName       = "users.json";
    private const string SessionUserIdKey    = "auth_user_id";
    private const string SessionRoleKey      = "auth_role";

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private List<AuthUser> _users = new();
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public AuthUser? CurrentUser { get; private set; }
    public bool IsLoggedIn       => CurrentUser != null;
    public UserRole CurrentRole  => CurrentUser?.Role ?? UserRole.Guest;

    /// <summary>Fired whenever the current user changes (login, logout, guest).</summary>
    public event EventHandler? UserChanged;

    private static string GetProjectRootPath()
    {
        var basePath = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(basePath);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "IndoorNav.csproj")))
            dir = dir.Parent;
        return dir?.FullName ?? basePath;
    }

    private static string ProjectRawPath =>
        Path.Combine(GetProjectRootPath(), "Resources", "Raw", UsersFileName);

    private static string LocalPath =>
        Path.Combine(FileSystem.AppDataDirectory, UsersFileName);

    private static string FilePath =>
#if IOS || ANDROID
        LocalPath;
#else
        ProjectRawPath;
#endif

    // ── Initialisation ───────────────────────────────────────────────────────────

    public async Task InitAsync()
    {
        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;
            await LoadUsersAsync();
            RestoreSession();
            _initialized = true;
        }
        finally { _initLock.Release(); }
    }

    private async Task LoadUsersAsync()
    {
        if (File.Exists(FilePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(FilePath);
                _users = JsonSerializer.Deserialize<List<AuthUser>>(json, JsonOpts) ?? new();
            }
            catch { _users = new(); }
        }

        var bundledUsers = await LoadBundledUsersAsync();

        if (_users.Count == 0 && bundledUsers.Count > 0)
        {
            _users = bundledUsers;
            await SaveUsersAsync();
        }
        else if (bundledUsers.Count > 0)
        {
            var merged = false;
            foreach (var bundled in bundledUsers)
            {
                if (_users.Any(u => u.Id == bundled.Id || u.Username.Equals(bundled.Username, StringComparison.OrdinalIgnoreCase)))
                    continue;

                _users.Add(bundled);
                merged = true;
            }

            if (merged)
                await SaveUsersAsync();
        }

        // Ensure there is always a default admin account
        if (!_users.Any(u => u.Role == UserRole.Admin))
        {
            _users.Add(new AuthUser
            {
                Username     = "0",
                PasswordHash = Hash("0"),
                Role         = UserRole.Admin,
                DisplayName  = "Администратор"
            });
            await SaveUsersAsync();
        }
        else
        {
            // Migrate old default credentials (admin/admin) to new defaults (0/0)
            var oldAdmin = _users.FirstOrDefault(u =>
                u.Role == UserRole.Admin &&
                u.Username == "admin" &&
                u.PasswordHash == Hash("admin"));
            if (oldAdmin != null)
            {
                oldAdmin.Username     = "0";
                oldAdmin.PasswordHash = Hash("0");
                await SaveUsersAsync();
            }
        }
    }

    private static async Task<List<AuthUser>> LoadBundledUsersAsync()
    {
        try
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync(UsersFileName);
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync();
            return JsonSerializer.Deserialize<List<AuthUser>>(json, JsonOpts) ?? new();
        }
        catch
        {
            if (!File.Exists(ProjectRawPath))
                return new();

            try
            {
                var json = await File.ReadAllTextAsync(ProjectRawPath);
                return JsonSerializer.Deserialize<List<AuthUser>>(json, JsonOpts) ?? new();
            }
            catch
            {
                return new();
            }
        }
    }

    private async Task SaveUsersAsync()
    {
        var json = JsonSerializer.Serialize(_users, JsonOpts);
        await File.WriteAllTextAsync(FilePath, json);
    }

    private void RestoreSession()
    {
        var savedId = Preferences.Default.Get(SessionUserIdKey, string.Empty);
        if (string.IsNullOrEmpty(savedId)) return;
        CurrentUser = _users.FirstOrDefault(u => u.Id == savedId);
    }

    // ── Public API ───────────────────────────────────────────────────────────────

    /// <summary>Login with username + password. Returns null if credentials are wrong.</summary>
    public async Task<AuthUser?> LoginAsync(string username, string password)
    {
        await InitAsync();
        username = username.Trim();
        password = password.TrimEnd('\r', '\n');

        var user = _users.FirstOrDefault(u =>
            string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase)
            && u.PasswordHash == Hash(password));

        if (user == null)
        {
            user = (await LoadBundledUsersAsync()).FirstOrDefault(u =>
                string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase)
                && u.PasswordHash == Hash(password));

            if (user == null) return null;

            var local = _users.FirstOrDefault(u =>
                u.Id == user.Id || u.Username.Equals(user.Username, StringComparison.OrdinalIgnoreCase));
            if (local == null)
                _users.Add(user);
            else
            {
                local.PasswordHash = user.PasswordHash;
                local.Role         = user.Role;
                local.GroupId      = user.GroupId;
                local.DisplayName  = user.DisplayName;
            }
            await SaveUsersAsync();
        }

        SetCurrentUser(user);
        return user;
    }

    /// <summary>Login as guest (no credentials needed).</summary>
    public AuthUser LoginAsGuest()
    {
        var guest = new AuthUser
        {
            Id          = "guest",
            Username    = "guest",
            Role        = UserRole.Guest,
            DisplayName = "Гость"
        };
        CurrentUser = guest;
        // Don't persist guest session across restarts
        Preferences.Default.Remove(SessionUserIdKey);
        UserChanged?.Invoke(this, EventArgs.Empty);
        return guest;
    }

    public void Logout()
    {
        CurrentUser = null;
        Preferences.Default.Remove(SessionUserIdKey);
        Preferences.Default.Remove(SessionRoleKey);
        UserChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<AuthUser> GetAllUsers() => _users;

    public IReadOnlyList<AuthUser> GetStudents() =>
        _users.Where(u => u.Role == UserRole.Student).ToList();

    public async Task AddUserAsync(AuthUser user)
    {
        user.PasswordHash = Hash(user.PasswordHash); // hash plain-text password passed in
        _users.Add(user);
        await SaveUsersAsync();
    }

    public async Task RemoveUserAsync(string userId)
    {
        _users.RemoveAll(u => u.Id == userId);
        await SaveUsersAsync();
    }

    /// <summary>Persist in-place edits made to a user object that is already in the list.</summary>
    public async Task UpdateUserAsync() => await SaveUsersAsync();

    public async Task ChangePasswordAsync(string userId, string newPassword)
    {
        var user = _users.FirstOrDefault(u => u.Id == userId);
        if (user == null) return;
        user.PasswordHash = Hash(newPassword);
        await SaveUsersAsync();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private void SetCurrentUser(AuthUser user)
    {
        CurrentUser = user;
        Preferences.Default.Set(SessionUserIdKey, user.Id);
        Preferences.Default.Set(SessionRoleKey, user.Role.ToString());
        UserChanged?.Invoke(this, EventArgs.Empty);
    }

    public static string Hash(string input)
    {
        var bytes  = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLower();
    }
}
