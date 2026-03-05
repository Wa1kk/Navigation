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
            var choice = await DisplayAlert("QR-код отсканирован", "Хотите сохранить QR-код?", "Сохранить", "Нет");
            if (choice)
            {
                await SaveQrCodeAsync(raw);
            }
            else
            {
                DeepLinkService.RequestNode(nodeId);
                await Navigation.PopModalAsync();
            }
        });
    }
#endif

    private async Task SaveQrCodeAsync(string qrData)
    {
        try
        {
            var fileName = $"QR_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt";
            
            // Попробуем использовать встроенную папку сохранения
            string? folderPath = null;
            
#if WINDOWS || MACCATALYST
            // На Windows/Mac используем Documents папку
            folderPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
#elif ANDROID
            // На Android используем Pictures папку
            folderPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
#elif IOS
            // На iOS используем Documents при наличии доступа
            folderPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
#endif

            if (string.IsNullOrEmpty(folderPath))
            {
                await DisplayAlert("Ошибка", "Не удалось определить папку для сохранения", "OK");
                return;
            }

            var filePath = Path.Combine(folderPath, fileName);
            await File.WriteAllTextAsync(filePath, qrData);

            await DisplayAlert("Готово", $"QR-код сохранён в:\n{filePath}", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Ошибка", $"Ошибка при сохранении: {ex.Message}", "OK");
        }
        finally
        {
            DeepLinkService.RequestNode(qrData.TrimStart('/'));
            await Navigation.PopModalAsync();
        }
    }

    private void OnCloseClicked(object? sender, EventArgs e) =>
        _ = Navigation.PopModalAsync();
}
