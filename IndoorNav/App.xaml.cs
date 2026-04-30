using IndoorNav.Pages;
using IndoorNav.Services;

namespace IndoorNav;

public partial class App : Application
{
    private readonly AuthService _authService;
    private readonly SplashPage  _splashPage;
    private readonly LoginPage   _loginPage;

    public App(AuthService authService, SplashPage splashPage, LoginPage loginPage)
    {
        InitializeComponent();
        _authService = authService;
        _splashPage  = splashPage;
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
        var window = new Window(_splashPage);

        _ = Task.Run(async () =>
        {
            await _authService.InitAsync();

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_authService.IsLoggedIn)
                {
                    var shell = IPlatformApplication.Current!.Services
                        .GetRequiredService<AppShell>();
                    window.Page = shell;
                }
                else
                {
                    window.Page = _loginPage;
                }
            });
        });

        return window;
    }
}
