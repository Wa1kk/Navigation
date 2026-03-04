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

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var msg = e.ExceptionObject?.ToString() ?? "Unknown";
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try { await Current!.Windows[0].Page!.DisplayAlert("CRASH", msg, "OK"); } catch { }
            });
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "indoornav_crash.txt"),
                msg);
        };
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
