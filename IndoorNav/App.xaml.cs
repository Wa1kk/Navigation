using IndoorNav.Pages;
using IndoorNav.Services;

namespace IndoorNav;

public partial class App : Application
{
    private readonly AuthService _authService;
    private readonly LoginPage   _loginPage;

    public App(AuthService authService, LoginPage loginPage)
    {
        InitializeComponent();
        _authService = authService;
        _loginPage   = loginPage;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(_loginPage);

        // Initialise auth async; if an active session exists, skip straight to the main shell
        _ = Task.Run(async () =>
        {
            await _authService.InitAsync();
            if (_authService.IsLoggedIn)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    var shell = IPlatformApplication.Current!.Services
                        .GetRequiredService<AppShell>();
                    window.Page = shell;
                });
            }
        });

        return window;
    }
}
