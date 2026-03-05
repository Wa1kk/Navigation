#if ANDROID || IOS
// Camera QR scanning not available in ZXing.Net.Maui 0.4.0
// Using text entry fallback instead
#endif
using IndoorNav.Services;

namespace IndoorNav.Pages;

/// <summary>
/// Modal QR scanner page. Shows a text-entry fallback for scanning QR codes.
/// </summary>
public partial class QrScanPage : ContentPage
{
    public QrScanPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        SetupCamera();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Cleanup if needed
    }

    // ── Setup ─────────────────────────────────────────────────────────────────

    private void SetupCamera()
    {
        // Camera scanning not available - text entry fallback is shown in XAML
    }

    // ── Events ────────────────────────────────────────────────────────────────

    private void OnCloseClicked(object? sender, EventArgs e) =>
        _ = Navigation.PopModalAsync();
}
