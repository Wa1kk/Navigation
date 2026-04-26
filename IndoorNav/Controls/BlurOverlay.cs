namespace IndoorNav.Controls;

/// <summary>
/// Полупрозрачное размытое наложение поверх контента.
/// На iOS — UIVisualEffectView (SystemUltraThinMaterialLight) + tint.
/// Привязывается к UIWindow для edge-to-edge покрытия.
/// </summary>
public class BlurOverlay : ContentView
{
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        ApplyNativeBlur();
    }

    private void ApplyNativeBlur()
    {
#if IOS || MACCATALYST
        if (Handler?.PlatformView is not UIKit.UIView uiView) return;

        // Очищаем фон нативного view — иначе MAUI BackgroundColor перекроет blur
        uiView.BackgroundColor = UIKit.UIColor.Clear;
        uiView.Opaque = false;

        // Удаляем старые blur/tint subviews
        foreach (var sub in uiView.Subviews)
            if (sub is UIKit.UIVisualEffectView || sub.Tag == 99)
                sub.RemoveFromSuperview();

        // Находим window для edge-to-edge привязки
        var window = uiView.Window;
        if (window != null)
        {
            InstallBlur(uiView, window);
        }
        else
        {
            // Window ещё не доступен — проверим после задержки
            Microsoft.Maui.Dispatching.IDispatcherTimer? timer = null;
            timer = Dispatcher.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(50);
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                var w = uiView.Window;
                if (w != null) InstallBlur(uiView, w);
            };
            timer.Start();
        }
#endif
    }

#if IOS || MACCATALYST
    private bool _installed;

    private void InstallBlur(UIKit.UIView uiView, UIKit.UIWindow window)
    {
        if (_installed) return;
        _installed = true;

        // Blur
        var blurEffect = UIKit.UIBlurEffect.FromStyle(UIKit.UIBlurEffectStyle.SystemUltraThinMaterialLight);
        var blurView = new UIKit.UIVisualEffectView(blurEffect)
        {
            TranslatesAutoresizingMaskIntoConstraints = false
        };
        uiView.InsertSubview(blurView, 0);

        // Привязка к WINDOW — покрывает весь экран edge-to-edge
        blurView.LeadingAnchor.ConstraintEqualTo(window.LeadingAnchor).Active = true;
        blurView.TrailingAnchor.ConstraintEqualTo(window.TrailingAnchor).Active = true;
        blurView.TopAnchor.ConstraintEqualTo(window.TopAnchor).Active = true;
        blurView.BottomAnchor.ConstraintEqualTo(window.BottomAnchor).Active = true;

        // Tint overlay (затемнение поверх blur)
        var tintView = new UIKit.UIView
        {
            TranslatesAutoresizingMaskIntoConstraints = false,
            BackgroundColor = UIKit.UIColor.FromRGBA(0, 0, 0, 0x33),
            Tag = 99
        };
        uiView.InsertSubview(tintView, 1);

        tintView.LeadingAnchor.ConstraintEqualTo(window.LeadingAnchor).Active = true;
        tintView.TrailingAnchor.ConstraintEqualTo(window.TrailingAnchor).Active = true;
        tintView.TopAnchor.ConstraintEqualTo(window.TopAnchor).Active = true;
        tintView.BottomAnchor.ConstraintEqualTo(window.BottomAnchor).Active = true;
    }
#endif
}
