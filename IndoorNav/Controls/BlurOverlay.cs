namespace IndoorNav.Controls;

/// <summary>
/// Полупрозрачное размытое наложение поверх контента.
/// На iOS — UIVisualEffectView (SystemUltraThinMaterialLight).
/// На Windows — WinUI AcrylicBrush (через BlurOverlayHandler).
/// На других платформах — тёмный полупрозрачный фон как fallback.
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

        foreach (var sub in uiView.Subviews)
            if (sub is UIKit.UIVisualEffectView) sub.RemoveFromSuperview();

        var blurEffect = UIKit.UIBlurEffect.FromStyle(UIKit.UIBlurEffectStyle.SystemUltraThinMaterialLight);
        var blurView = new UIKit.UIVisualEffectView(blurEffect)
        {
            TranslatesAutoresizingMaskIntoConstraints = false
        };
        uiView.InsertSubview(blurView, 0);
        blurView.LeadingAnchor.ConstraintEqualTo(uiView.LeadingAnchor).Active = true;
        blurView.TrailingAnchor.ConstraintEqualTo(uiView.TrailingAnchor).Active = true;
        blurView.TopAnchor.ConstraintEqualTo(uiView.TopAnchor).Active = true;
        blurView.BottomAnchor.ConstraintEqualTo(uiView.BottomAnchor).Active = true;
#endif
    }
}
