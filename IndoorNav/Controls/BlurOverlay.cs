namespace IndoorNav.Controls;

/// <summary>
/// Полупрозрачное размытое наложение поверх контента.
/// На iOS — UIVisualEffectView (SystemUltraThinMaterialLight) + tint.
/// Привязывается к UIWindow для edge-to-edge покрытия.
/// Переустанавливает blur при каждом показе (IsVisibleChanged).
/// </summary>
public class BlurOverlay : ContentView
{
    private const int TintTag = 99;
    private const int FillTag = 100;

    public bool UseBlur { get; set; } = true;

    public BlurOverlay()
    {
        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(IsVisible) && IsVisible)
                ApplyNativeBlur();
        };
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (IsVisible)
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
            if (sub is UIKit.UIVisualEffectView || sub.Tag == TintTag || sub.Tag == FillTag)
                sub.RemoveFromSuperview();

        // Находим window для edge-to-edge привязки
        TryInstallBlur(uiView, 0);
#endif
    }

#if IOS || MACCATALYST
    private void TryInstallBlur(UIKit.UIView uiView, int attempt)
    {
        var window = uiView.Window;
        if (window != null)
        {
            InstallBlur(uiView, window);
            return;
        }

        // Window ещё не доступен — повторим до 10 раз с интервалом 50мс
        if (attempt >= 10) return;

        var timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(50);
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            TryInstallBlur(uiView, attempt + 1);
        };
        timer.Start();
    }

    private void InstallBlur(UIKit.UIView uiView, UIKit.UIWindow window)
    {
        if (!UseBlur)
        {
            InstallFill(uiView, window);
            return;
        }

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
            Tag = TintTag
        };
        uiView.InsertSubview(tintView, 1);

        tintView.LeadingAnchor.ConstraintEqualTo(window.LeadingAnchor).Active = true;
        tintView.TrailingAnchor.ConstraintEqualTo(window.TrailingAnchor).Active = true;
        tintView.TopAnchor.ConstraintEqualTo(window.TopAnchor).Active = true;
        tintView.BottomAnchor.ConstraintEqualTo(window.BottomAnchor).Active = true;
    }

    private void InstallFill(UIKit.UIView uiView, UIKit.UIWindow window)
    {
        var color = BackgroundColor ?? Colors.White;
        var fillView = new UIKit.UIView
        {
            TranslatesAutoresizingMaskIntoConstraints = false,
            BackgroundColor = UIKit.UIColor.FromRGBA(
                (nfloat)color.Red,
                (nfloat)color.Green,
                (nfloat)color.Blue,
                (nfloat)color.Alpha),
            Tag = FillTag,
            UserInteractionEnabled = false
        };
        uiView.InsertSubview(fillView, 0);

        fillView.LeadingAnchor.ConstraintEqualTo(window.LeadingAnchor).Active = true;
        fillView.TrailingAnchor.ConstraintEqualTo(window.TrailingAnchor).Active = true;
        fillView.TopAnchor.ConstraintEqualTo(window.TopAnchor).Active = true;
        fillView.BottomAnchor.ConstraintEqualTo(window.BottomAnchor).Active = true;
    }
#endif
}
