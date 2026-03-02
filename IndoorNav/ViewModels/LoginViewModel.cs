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

    private string _selectedRole = "Admin";   // "Admin" | "Student" | "Guest"
    private string _username     = string.Empty;
    private string _password     = string.Empty;
    private string _errorMessage = string.Empty;
    private bool   _isBusy;

    public LoginViewModel(AuthService auth)
    {
        _auth = auth;

        LoginCommand           = new Command(ExecuteLogin,     () => !IsBusy);
        SelectAdminRoleCommand = new Command(() => SelectedRole = "Admin");
        SelectStudentRoleCommand = new Command(() => SelectedRole = "Student");
        SelectGuestRoleCommand = new Command(() => SelectedRole = "Guest");
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
            OnPropertyChanged(nameof(IsAdmin));
            OnPropertyChanged(nameof(IsStudent));
            OnPropertyChanged(nameof(IsGuest));
            OnPropertyChanged(nameof(ShowCredentials));
            OnPropertyChanged(nameof(ShowUsernameField));
        }
    }

    public bool IsAdmin   => _selectedRole == "Admin";
    public bool IsStudent => _selectedRole == "Student";
    public bool IsGuest   => _selectedRole == "Guest";

    // Admin needs only password; Student needs both; Guest needs neither
    public bool ShowCredentials  => !IsGuest;
    public bool ShowUsernameField => IsStudent;

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

    public ICommand LoginCommand            { get; }
    public ICommand SelectAdminRoleCommand  { get; }
    public ICommand SelectStudentRoleCommand{ get; }
    public ICommand SelectGuestRoleCommand  { get; }

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

            var username = IsAdmin ? "admin" : _username.Trim();
            var password = _password;

            if (IsStudent && string.IsNullOrWhiteSpace(_username))
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    ErrorMessage = "Введите имя пользователя";
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
                    ErrorMessage = "Неверные учётные данные";
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
