using IndoorNav.Pages;
using IndoorNav.Services;

namespace IndoorNav;

public partial class App : Application
{
    private readonly AuthService _authService;
    private readonly SplashPage  _splashPage;
    private readonly LoginPage   _loginPage;
    private readonly NotificationService _notificationService;
    private readonly EmergencyService _emergencyService;

    public App(AuthService authService, SplashPage splashPage, LoginPage loginPage, NotificationService notificationService, EmergencyService emergencyService)
    {
        InitializeComponent();
        _authService = authService;
        _splashPage  = splashPage;
        _loginPage   = loginPage;
        _notificationService = notificationService;
        _emergencyService = emergencyService;

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
            await _notificationService.InitializeAsync();

            // Load emergency state early so we can check IsEmergencyActive
            await _emergencyService.LoadAsync();

            // If emergency was active before app was killed, restore notification spam
            if (_emergencyService.IsEmergencyActive || _notificationService.IsEmergencySpamActive)
            {
                await _notificationService.CheckPermissionAsync();
                _notificationService.RestartSpamFromPersistedState();
            }

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

    protected override void OnStart()
    {
        base.OnStart();
        _notificationService.OnAppForegrounded();
    }

    protected override void OnResume()
    {
        base.OnResume();
        _notificationService.OnAppForegrounded();
    }

    protected override void OnSleep()
    {
        base.OnSleep();
        _notificationService.OnAppBackgrounded();
    }
}
