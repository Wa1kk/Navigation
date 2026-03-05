#if ANDROID || IOS
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;
#endif
using IndoorNav.Services;

namespace IndoorNav.Pages;

/// <summary>
/// Modal QR scanner page.  On Android/iOS opens the device camera and detects
/// QR codes automatically.  On other platforms shows a text-entry fallback.
/// </summary>
public partial class QrScanPage : ContentPage
{
#if ANDROID || IOS
    private bool _handled;
#endif

    public QrScanPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
#if ANDROID || IOS
        _handled = false;
#endif
        SetupCamera();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
#if ANDROID || IOS
        if (CameraHolder.Content is CameraBarcodeReaderView cv)
        {
            cv.IsDetecting = false;
            cv.Handler?.DisconnectHandler();
        }
        CameraHolder.Content = null;
#endif
    }

    // ── Setup ─────────────────────────────────────────────────────────────────

    private void SetupCamera()
    {
#if ANDROID || IOS
        var camera = new CameraBarcodeReaderView
        {
            Options = new BarcodeReaderOptions
            {
                Formats      = BarcodeFormats.QrCode,
                AutoRotate   = true,
                Multiple     = false,
                TryHarder    = true
            },
            IsDetecting          = true,
            HorizontalOptions    = LayoutOptions.Fill,
            VerticalOptions      = LayoutOptions.Fill
        };
        camera.BarcodesDetected += OnBarcodesDetected;
        CameraHolder.Content = camera;
#endif
    }

    // ── Events ────────────────────────────────────────────────────────────────

#if ANDROID || IOS
    private void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        if (_handled) return;
        var raw = e.Results?.FirstOrDefault()?.Value;
        if (string.IsNullOrWhiteSpace(raw)) return;
        _handled = true;

        var nodeId = DeepLinkService.ParseUri(raw) ?? raw.TrimStart('/');
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            DeepLinkService.RequestNode(nodeId);
            await Navigation.PopModalAsync();
        });
    }
#endif

    private void OnCloseClicked(object? sender, EventArgs e) =>
        _ = Navigation.PopModalAsync();
}
