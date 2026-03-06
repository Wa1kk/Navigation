using Microsoft.Extensions.Logging;
using SkiaSharp.Views.Maui.Controls.Hosting;
using IndoorNav.Services;
using IndoorNav.ViewModels;
using IndoorNav.Pages;
using IndoorNav.Controls;
#if ANDROID || IOS
using ZXing.Net.Maui.Controls;
#endif

namespace IndoorNav;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseSkiaSharp()
#if ANDROID || IOS
            .UseBarcodeReader()
#endif
            .ConfigureMauiHandlers(handlers =>
            {
#if WINDOWS
                handlers.AddHandler<BlurOverlay, BlurOverlayHandler>();
#endif
            })
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Services
        builder.Services.AddSingleton<NavGraphService>();
        builder.Services.AddSingleton<AuthService>();
        builder.Services.AddSingleton<EmergencyService>();
        builder.Services.AddSingleton<ScheduleService>();
        builder.Services.AddSingleton<DepartmentService>();
        builder.Services.AddSingleton<QrService>();

        // ViewModels
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddTransient<AdminViewModel>();
        builder.Services.AddTransient<LoginViewModel>();

        // Pages
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddTransient<AdminPage>();
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<QrScanPage>();

        // Shell (singleton so it can be retrieved in LoginViewModel)
        builder.Services.AddSingleton<AppShell>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}

