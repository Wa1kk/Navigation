using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.UI.Xaml.Media;

namespace IndoorNav.Controls;

public class BlurOverlayHandler : ContentViewHandler
{
    protected override void ConnectHandler(ContentPanel platformView)
    {
        base.ConnectHandler(platformView);
        ApplyAcrylic(platformView);
    }

    private static void ApplyAcrylic(Microsoft.UI.Xaml.FrameworkElement view)
    {
        try
        {
            var acrylic = new AcrylicBrush
            {
                TintColor         = Windows.UI.Color.FromArgb(180, 10, 15, 30),
                TintOpacity       = 0.65,
                FallbackColor     = Windows.UI.Color.FromArgb(200, 10, 15, 30),
            };
            // ContentPanel наследует от Panel — у него есть Background
            if (view is Microsoft.UI.Xaml.Controls.Panel panel)
                panel.Background = acrylic;
        }
        catch
        {
            // Fallback: если AcrylicBrush недоступен (старый драйвер/VM)
            if (view is Microsoft.UI.Xaml.Controls.Panel panel)
                panel.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(190, 10, 15, 30));
        }
    }
}
