using IndoorNav.Services;
using ZXing.Net.Maui;

namespace IndoorNav.Pages;

public partial class QrScanPage : ContentPage
{
    private readonly QrService _qrService;
    private bool _isProcessing;

    public QrScanPage(QrService qrService)
    {
        InitializeComponent();
        _qrService = qrService;
        CameraScanner.Options = new BarcodeReaderOptions
        {
            Formats = BarcodeFormats.TwoDimensional,
            AutoRotate = true,
            Multiple = false,
            TryHarder = true
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _isProcessing = false;
        CameraScanner.IsDetecting = true;
    }

    protected override void OnDisappearing()
    {
        CameraScanner.IsDetecting = false;
        base.OnDisappearing();
    }

    private async Task ProcessQrTextAsync(string? text)
    {
        var nodeId = DeepLinkService.ParseUri(text);
        if (nodeId == null)
        {
            await DisplayAlert("QR", "Нераспознанный формат QR-кода.", "ОК");
            return;
        }
        DeepLinkService.RequestNode(nodeId);
        await Navigation.PopModalAsync();
    }

    private async Task ProcessImageAsync(byte[] bytes)
    {
        var text = _qrService.DecodeFromBytes(bytes);
        if (text == null)
        {
            await DisplayAlert("QR", "QR-код не найден в изображении.", "ОК");
            return;
        }
        await ProcessQrTextAsync(text);
    }

    private void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        if (_isProcessing) return;
        var value = e.Results?.FirstOrDefault()?.Value;
        if (string.IsNullOrWhiteSpace(value)) return;

        _isProcessing = true;
        CameraScanner.IsDetecting = false;
        MainThread.BeginInvokeOnMainThread(async () => await ProcessQrTextAsync(value));
    }

    private void OnRestartScanClicked(object? sender, EventArgs e)
    {
        _isProcessing = false;
        CameraScanner.IsDetecting = true;
    }

    private async void OnPickFromGalleryClicked(object? sender, EventArgs e)
    {
        try
        {
            var result = await MediaPicker.Default.PickPhotoAsync(new MediaPickerOptions
            {
                Title = "Выберите изображение с QR-кодом"
            });
            if (result == null) return;
            byte[] bytes;
            using var stream = await result.OpenReadAsync();
            using var ms     = new MemoryStream();
            await stream.CopyToAsync(ms);
            bytes = ms.ToArray();
            await ProcessImageAsync(bytes);
        }
        catch (Exception ex)
        {
            await DisplayAlert("QR", $"Не удалось загрузить изображение: {ex.Message}", "ОК");
        }
    }

    private void OnCloseClicked(object? sender, EventArgs e) =>
        _ = Navigation.PopModalAsync();
}
