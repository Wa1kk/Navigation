namespace IndoorNav.Services;

/// <summary>
/// Простой счётчик ссылок для blur overlay.
/// Сам blur реализован в BlurOverlay.cs через UIVisualEffectView.
/// Этот сервис только управляет ref counting для нескольких popup.
/// </summary>
public static class FullscreenBlurService
{
    private static int _refCount;

    public static void Show() { _refCount++; }
    public static void Hide() { _refCount = Math.Max(0, _refCount - 1); }
    public static bool IsVisible => _refCount > 0;
}
