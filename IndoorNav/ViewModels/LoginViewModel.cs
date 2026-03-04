using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using IndoorNav.Models;
using IndoorNav.Services;
using Microsoft.Extensions.DependencyInjection;

namespace IndoorNav.ViewModels;

public class LoginViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly AuthService _auth;

    private string _selectedRole = "User";   // "User" | "Guest"
    private string _username     = string.Empty;
    private string _password     = string.Empty;
    private string _errorMessage = string.Empty;
    private bool   _isBusy;

    public LoginViewModel(AuthService auth)
    {
        _auth = auth;

        LoginCommand           = new Command(ExecuteLogin,     () => !IsBusy);
        SelectUserRoleCommand  = new Command(() => SelectedRole = "User");
        SelectGuestRoleCommand = new Command(() => SelectedRole = "Guest");

        // Legacy aliases (kept so any XAML references still compile)
        SelectAdminRoleCommand   = SelectUserRoleCommand;
        SelectStudentRoleCommand = SelectUserRoleCommand;
    }

    // ── Properties ──────────────────────────────────────────────────────────────

    public string SelectedRole
    {
        get => _selectedRole;
        set
        {
            if (_selectedRole == value) return;
            _selectedRole = value;
            ErrorMessage  = string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsUser));
            OnPropertyChanged(nameof(IsAdmin));
            OnPropertyChanged(nameof(IsStudent));
            OnPropertyChanged(nameof(IsGuest));
            OnPropertyChanged(nameof(ShowCredentials));
            OnPropertyChanged(nameof(ShowUsernameField));
        }
    }

    public bool IsUser    => _selectedRole == "User";
    public bool IsGuest   => _selectedRole == "Guest";

    // Legacy aliases used in XAML bindings
    public bool IsAdmin   => IsUser;
    public bool IsStudent => IsUser;

    // Show username + password for User; nothing for Guest
    public bool ShowCredentials   => IsUser;
    public bool ShowUsernameField => IsUser;

    public string Username
    {
        get => _username;
        set { _username = value; OnPropertyChanged(); ErrorMessage = string.Empty; }
    }

    public string Password
    {
        get => _password;
        set { _password = value; OnPropertyChanged(); ErrorMessage = string.Empty; }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set { _errorMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasError)); }
    }
    public bool HasError => !string.IsNullOrEmpty(_errorMessage);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            _isBusy = value;
            OnPropertyChanged();
            ((Command)LoginCommand).ChangeCanExecute();
        }
    }

    // ── Commands ────────────────────────────────────────────────────────────────

    public ICommand LoginCommand             { get; }
    public ICommand SelectUserRoleCommand    { get; }
    public ICommand SelectGuestRoleCommand   { get; }
    // Legacy aliases
    public ICommand SelectAdminRoleCommand   { get; }
    public ICommand SelectStudentRoleCommand { get; }

    // ── Login logic ─────────────────────────────────────────────────────────────

    private void ExecuteLogin()
    {
        ErrorMessage = string.Empty;
        IsBusy = true;
        Task.Run(ExecuteLoginAsync);
    }

    private async Task ExecuteLoginAsync()
    {
        try
        {
            if (IsGuest)
            {
                _auth.LoginAsGuest();
                NavigateToMain();
                return;
            }

            var username = _username.Trim();
            var password = _password;

            if (string.IsNullOrWhiteSpace(username))
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    ErrorMessage = "Введите логин";
                    IsBusy = false;
                });
                return;
            }
            if (string.IsNullOrWhiteSpace(password))
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    ErrorMessage = "Введите пароль";
                    IsBusy = false;
                });
                return;
            }

            var user = await _auth.LoginAsync(username, password);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                IsBusy = false;
                if (user == null)
                {
                    ErrorMessage = "Неверный логин или пароль";
                    Password = string.Empty;
                    return;
                }
                NavigateToMain();
            });
        }
        catch (Exception ex)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ErrorMessage = ex.Message;
                IsBusy = false;
            });
        }
    }

    private static void NavigateToMain()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var shell = IPlatformApplication.Current!.Services
                .GetRequiredService<AppShell>();
            Application.Current!.Windows[0].Page = shell;
        });
    }

    // ── INotifyPropertyChanged ───────────────────────────────────────────────────

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
