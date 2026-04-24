using IndoorNav.Services;

namespace IndoorNav.Pages;

public partial class QrScanPage : ContentPage
{
    private readonly QrService _qrService;

    public QrScanPage(QrService qrService)
    {
        InitializeComponent();
        _qrService = qrService;
    }

    private async Task ProcessImageAsync(byte[] bytes)
    {
        var text = _qrService.DecodeFromBytes(bytes);
        if (text == null)
        {
            await DisplayAlert("QR", "QR-код не найден в изображении.", "ОК");
            return;
        }
        var nodeId = DeepLinkService.ParseUri(text);
        if (nodeId == null)
        {
            await DisplayAlert("QR", "Нераспознанный формат QR-кода.", "ОК");
            return;
        }
        DeepLinkService.RequestNode(nodeId);
        await Navigation.PopModalAsync();
    }

    private async void OnCapturePhotoClicked(object? sender, EventArgs e)
    {
        try
        {
            if (!MediaPicker.Default.IsCaptureSupported)
            {
                await DisplayAlert("QR", "Камера недоступна на этом устройстве.", "ОК");
                return;
            }
            var photo = await MediaPicker.Default.CapturePhotoAsync();
            if (photo == null) return;
            byte[] bytes;
            using var stream = await photo.OpenReadAsync();
            using var ms     = new MemoryStream();
            await stream.CopyToAsync(ms);
            bytes = ms.ToArray();
            await ProcessImageAsync(bytes);
        }
        catch (Exception ex)
        {
            await DisplayAlert("QR", $"Не удалось открыть камеру: {ex.Message}", "ОК");
        }
    }

    private async void OnPickFromGalleryClicked(object? sender, EventArgs e)
    {
        try
        {
            var result = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = "Выберите изображение с QR-кодом",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.Android,    new[] { "image/png", "image/jpeg", "image/bmp" } },
                    { DevicePlatform.iOS,         new[] { "public.image" } },
                    { DevicePlatform.WinUI,       new[] { ".png", ".jpg", ".jpeg" } },
                    { DevicePlatform.MacCatalyst, new[] { "public.image" } },
                })
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
