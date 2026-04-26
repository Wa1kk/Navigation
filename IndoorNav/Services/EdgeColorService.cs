namespace IndoorNav.Services;

/// <summary>
/// Управляет цветом краёв на iOS для edge-to-edge визуального продолжения
/// под Dynamic Island и Home Indicator.
/// Устанавливает BackgroundColor страницы и UIWindow — при SetUseSafeArea(false)
/// фон страницы заполняет весь экран.
/// </summary>
public static class EdgeColorService
{
    /// <summary>
    /// Установить цвет краёв. Красит BackgroundColor страницы и UIWindow.
    /// </summary>
    public static void SetEdgeColor(ContentPage page, string hexColor)
    {
#if IOS || MACCATALYST
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(() => SetEdgeColor(page, hexColor));
            return;
        }

        // Цвет страницы — заполняет весь экран при SetUseSafeArea(false)
        var color = Color.FromArgb(hexColor);
        page.BackgroundColor = color;

        // Цвет UIWindow — для зон за пределами MAUI content
        if (page.Handler?.PlatformView is UIKit.UIView pageNative)
        {
            var window = pageNative.Window;
            if (window != null)
                window.BackgroundColor = UIKit.UIColor.FromRGB(
                    (nfloat)color.Red,
                    (nfloat)color.Green,
                    (nfloat)color.Blue);
        }
#endif
    }
}
