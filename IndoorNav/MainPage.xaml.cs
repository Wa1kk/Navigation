using IndoorNav.Models;
using IndoorNav.ViewModels;

namespace IndoorNav;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _vm;

    public MainPage(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
        MainCanvas.NodeTapped += OnNodeTapped;
        _vm.PropertyChanged += OnVmPropertyChanged;
        SetSidebarExpanded(_vm.IsSidebarExpanded);

#if IOS || MACCATALYST
        Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.Page.SetUseSafeArea(this, false);
#endif
        Services.EdgeColorService.SetEdgeColor(this, "#FFFFFF");
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Services.EdgeColorService.SetEdgeColor(this, "#FFFFFF");
    }

#if IOS || MACCATALYST
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        ApplySafeAreaPadding();
    }

    private void ApplySafeAreaPadding()
    {
        if (Handler?.PlatformView is UIKit.UIView nativeView)
        {
            var insets = nativeView.SafeAreaInsets;
            MainGrid.Padding = new Thickness(
                insets.Left,
                insets.Top,
                insets.Right,
                insets.Bottom);
        }
    }
#endif

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.StartNode) && _vm.StartNode?.IsQrAnchor == true)
        {
            // При сканировании QR зумируем к узлу
            Dispatcher.Dispatch(() =>
            {
                var node = _vm.StartNode;
                if (node != null)
                    MainCanvas.ApplyOrQueueZoom(() => MainCanvas.ZoomToNode(node, 2.5f));
            });
        }
        else if (e.PropertyName == nameof(MainViewModel.PendingQrNode) && _vm.PendingQrNode != null)
        {
            // QR отсканирован — приближаемся к точке и показываем «Вы тут»
            Dispatcher.Dispatch(() =>
            {
                var node = _vm.PendingQrNode;
                if (node != null)
                    MainCanvas.ApplyOrQueueZoom(() => MainCanvas.ZoomToNode(node, 3.0f));
            });
        }
        else if (e.PropertyName == nameof(MainViewModel.HasRoute) && _vm.HasRoute)
        {
            // Откладываем на следующую итерацию главного потока — к этому моменту
            // BuildRouteSteps уже вызовет SelectedFloor (StartFloorLoad), и ApplyOrQueueZoom
            // правильно поставит зум в очередь вместо немедленного применения на старом этаже.
            Dispatcher.Dispatch(ApplyStepZoom);
        }

        if (e.PropertyName == nameof(MainViewModel.IsBuildingPickerOpen))
        {
            if (_vm.IsBuildingPickerOpen)
                _ = ShowBuildingPickerAsync();
            else
                _ = HideBuildingPickerAsync();
        }

        if (e.PropertyName == nameof(MainViewModel.IsPickerOpen))
        {
            if (_vm.IsPickerOpen)
                ShowNodePickerBlur();
            else
                HideNodePickerBlur();
        }

        if (e.PropertyName == nameof(MainViewModel.IsUserMenuOpen))
        {
            if (_vm.IsUserMenuOpen)
                _ = ShowUserMenuAsync();
            else
                _ = HideUserMenuAsync();
        }

        if (e.PropertyName == nameof(MainViewModel.IsNodePopupOpen))
        {
            if (_vm.IsNodePopupOpen)
                _ = ShowNodePopupAsync();
            else
                _ = HideNodePopupAsync();
        }

        if (e.PropertyName == nameof(MainViewModel.IsEmergencyNotificationVisible))
        {
            if (_vm.IsEmergencyNotificationVisible)
                SetSafeAreaColor(Color.FromArgb("#CC000000"));
            else
                SetSafeAreaColor(null);
        }

        if (e.PropertyName == nameof(MainViewModel.IsSidebarExpanded))
        {
            SetSidebarExpanded(_vm.IsSidebarExpanded);
        }
    }

    // ── Node picker animated bottom sheet ────────────────────────────────────
    //
    // PickerSheet is at bottom of screen (VerticalOptions="End").
    // TranslationY = 0   → sheet at bottom (handle + search visible)
    // TranslationY < 0   → sheet slides UP (more results visible)

    private double ScreenHeight => this.Window?.Height ?? 800;

    // Resting state: sheet at bottom, only handle + search bar visible
    private const double PickerRestY = 0;
    // Keyboard state: search bar just above keyboard (~25% up)
    private double GetPickerKeyboardY() => -(ScreenHeight * 0.16);
    // Full state: sheet near top, all results visible
    private double GetPickerFullY() => -(ScreenHeight * 0.80);
    private const double PickerResultsKeyboardHeight = 132;
    private const double PickerResultsExpandedHeight = 420;
    private bool _pickerKeyboardOpen;

    private void ShowNodePickerBlur()
    {
        NodePickerBackdrop.IsVisible = true;
        SetSafeAreaColor(Colors.White);
        // Position sheet below screen, make visible, animate to bottom
        PickerSheet.TranslationY = ScreenHeight;
        PickerSheet.IsVisible = true;
        _ = PickerSheet.TranslateTo(0, PickerRestY, 280, Easing.SinOut);
    }

    private void HideNodePickerBlur()
    {
        // Clear search text
        _vm.PickerSearchText = string.Empty;

        // Animate out downward past screen bottom, then hide
        _ = PickerSheet.TranslateTo(0, ScreenHeight, 220, Easing.SinIn)
            .ContinueWith(_ =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    PickerSheet.IsVisible = false;
                    PickerSheet.TranslationY = 0;
                    SetSafeAreaColor(null);
                });
            });
        NodePickerBackdrop.IsVisible = false;
        PickerSearchEntry.Unfocus();
#if IOS || MACCATALYST
        UIKit.UIApplication.SharedApplication.SendAction(
            new ObjCRuntime.Selector("resignFirstResponder"),
            null, null, null);
#endif
    }

    private void OnPickerSearchFocused(object? sender, FocusEventArgs e)
    {
        if (!e.IsFocused) return;
        _pickerKeyboardOpen = true;
        // Show 2–3 results above keyboard, rest scrolls
        PickerResultsScroll.MaximumHeightRequest = PickerResultsKeyboardHeight;
        // Slide up so search bar is above keyboard
        _ = PickerSheet.TranslateTo(0, GetPickerKeyboardY(), 250, Easing.SinOut);
    }

    private void OnPickerSearchUnfocused(object? sender, FocusEventArgs e)
    {
        if (e.IsFocused) return;
        if (!_vm.IsPickerOpen) return;
        _pickerKeyboardOpen = false;
        // Keep sheet at the same Y; extend list downward into the freed keyboard space.
        PickerResultsScroll.MaximumHeightRequest = PickerResultsExpandedHeight;
        _ = PickerSheet.TranslateTo(0, PickerRestY, 220, Easing.SinOut);
    }

    private double _pickerSheetStartY;
    private bool _pickerSheetDragging;

    private void OnPickerSheetPan(object? sender, PanUpdatedEventArgs e)
    {
        var fullY = GetPickerFullY();

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _pickerSheetStartY = PickerSheet.TranslationY;
                _pickerSheetDragging = true;
                break;

            case GestureStatus.Running:
                if (!_pickerSheetDragging) break;
                var newY = _pickerSheetStartY + e.TotalY;
                // If keyboard is open, don't allow dragging below keyboard position.
                var bottomLimit = _pickerKeyboardOpen ? GetPickerKeyboardY() : PickerRestY;
                PickerSheet.TranslationY = Math.Clamp(newY, fullY, bottomLimit);
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                _pickerSheetDragging = false;
                var currentY = PickerSheet.TranslationY;

                var keyboardY = GetPickerKeyboardY();
                if (_pickerKeyboardOpen)
                {
                    _ = PickerSheet.TranslateTo(0, keyboardY, 200, Easing.SinOut);
                }
                // Two zones: near bottom (0) → dismiss, otherwise → snap to rest
                else if (currentY > -80)
                {
                    // Dragged near bottom → dismiss
                    _vm.IsPickerOpen = false;
                }
                else
                {
                    // Snap to rest position at bottom
                    _ = PickerSheet.TranslateTo(0, PickerRestY, 200, Easing.SinOut);
                }
                break;
        }
    }

    // ── Native iOS safe area color overlay ──────────────────────────────────
    // Adds a UIView pinned to UIWindow (like BlurOverlay) to cover
    // the home indicator and dynamic island zones with a solid color.

    private const int SafeAreaOverlayTag = 888;

    /// <summary>
    /// Sets a solid color in the iOS safe area zones (home indicator + dynamic island).
    /// Pass null to remove the overlay.
    /// </summary>
    private void SetSafeAreaColor(Color? color)
    {
#if IOS || MACCATALYST
        var window = this.Window?.Handler?.PlatformView as UIKit.UIWindow;
        if (window == null) return;

        // Remove existing overlay
        foreach (var v in window.Subviews)
            if (v.Tag == SafeAreaOverlayTag)
                v.RemoveFromSuperview();

        if (color == null) return;

        var nativeColor = UIKit.UIColor.FromRGBA(
            (nfloat)color.Red,
            (nfloat)color.Green,
            (nfloat)color.Blue,
            (nfloat)color.Alpha);

        var overlay = new UIKit.UIView
        {
            Tag = SafeAreaOverlayTag,
            TranslatesAutoresizingMaskIntoConstraints = false,
            BackgroundColor = nativeColor,
            UserInteractionEnabled = false
        };
        window.AddSubview(overlay);

        // Pin to window bottom edge — covers home indicator safe area
        overlay.LeadingAnchor.ConstraintEqualTo(window.LeadingAnchor).Active = true;
        overlay.TrailingAnchor.ConstraintEqualTo(window.TrailingAnchor).Active = true;
        overlay.BottomAnchor.ConstraintEqualTo(window.BottomAnchor).Active = true;

        // Height = bottom safe area inset (home indicator height ~34pt)
        var bottomInset = window.SafeAreaInsets.Bottom;
        if (bottomInset > 0)
            overlay.HeightAnchor.ConstraintEqualTo(bottomInset).Active = true;
        else
            overlay.HeightAnchor.ConstraintEqualTo(50).Active = true;

        var topOverlay = new UIKit.UIView
        {
            Tag = SafeAreaOverlayTag,
            TranslatesAutoresizingMaskIntoConstraints = false,
            BackgroundColor = nativeColor,
            UserInteractionEnabled = false
        };
        window.AddSubview(topOverlay);

        topOverlay.LeadingAnchor.ConstraintEqualTo(window.LeadingAnchor).Active = true;
        topOverlay.TrailingAnchor.ConstraintEqualTo(window.TrailingAnchor).Active = true;
        topOverlay.TopAnchor.ConstraintEqualTo(window.TopAnchor).Active = true;

        var topInset = window.SafeAreaInsets.Top;
        if (topInset > 0)
            topOverlay.HeightAnchor.ConstraintEqualTo(topInset).Active = true;
        else
            topOverlay.HeightAnchor.ConstraintEqualTo(50).Active = true;
#endif
    }

    // ── Node popup animation (Liquid Glass) ─────────────────────────────────

    private async Task ShowNodePopupAsync()
    {
        NodePopupBackdrop.IsVisible = true;
        NodePopupCard.Scale = 0.85;
        NodePopupCard.Opacity = 0;
        NodePopupCard.IsVisible = true;

        await Task.WhenAll(
            NodePopupCard.ScaleTo(1, 250, Easing.SpringOut),
            NodePopupCard.FadeTo(1, 180, Easing.Linear)
        );
    }

    private async Task HideNodePopupAsync()
    {
        await Task.WhenAll(
            NodePopupCard.ScaleTo(0.85, 150, Easing.CubicIn),
            NodePopupCard.FadeTo(0, 100, Easing.Linear)
        );
        NodePopupCard.IsVisible = false;
        NodePopupBackdrop.IsVisible = false;
        NodePopupCard.Scale = 1;
        NodePopupCard.Opacity = 1;
    }

    // ── Building picker animation ────────────────────────────────────────────

    private async Task ShowBuildingPickerAsync()
    {
        // Показываем backdrop + sheet одновременно — без layout thrashing
        SetSafeAreaColor(Colors.White);
        BuildingPickerBackdrop.IsVisible = true;
        BuildingPickerSheet.Opacity = 0;
        BuildingPickerSheet.TranslationY = -24;
        BuildingPickerSheet.IsVisible = true;

        // Параллельные анимации: slide-up sheet
        await Task.WhenAll(
            BuildingPickerSheet.FadeTo(1, 160, Easing.Linear),
            BuildingPickerSheet.TranslateTo(0, 0, 220, Easing.CubicOut));
    }

    private async Task HideBuildingPickerAsync()
    {
        // Параллельно скрываем оба элемента
        await Task.WhenAll(
            BuildingPickerSheet.FadeTo(0, 120, Easing.Linear),
            BuildingPickerSheet.TranslateTo(0, -24, 180, Easing.CubicIn));
        BuildingPickerSheet.IsVisible = false;
        BuildingPickerBackdrop.IsVisible = false;
        BuildingPickerSheet.TranslationY = 0;
        BuildingPickerSheet.Opacity = 1;
        SetSafeAreaColor(null);
    }

    // ── User menu animation (Liquid Glass) ──────────────────────────────────

    private async Task ShowUserMenuAsync()
    {
        UserMenuBackdrop.IsVisible = true;
        UserMenuCard.Scale = 0.85;
        UserMenuCard.Opacity = 0;
        UserMenuCard.IsVisible = true;

        await Task.WhenAll(
            UserMenuCard.ScaleTo(1, 280, Easing.SpringOut),
            UserMenuCard.FadeTo(1, 200, Easing.Linear)
        );
    }

    private async Task HideUserMenuAsync()
    {
        await Task.WhenAll(
            UserMenuCard.ScaleTo(0.85, 160, Easing.CubicIn),
            UserMenuCard.FadeTo(0, 120, Easing.Linear)
        );
        UserMenuCard.IsVisible = false;
        UserMenuBackdrop.IsVisible = false;
        UserMenuCard.Scale = 1;
        UserMenuCard.Opacity = 1;
    }

    // ── Sidebar toggle (Desktop only) ───────────────────────────────────────

    private void SetSidebarExpanded(bool expanded)
    {
        if (DeviceInfo.Current.Idiom == DeviceIdiom.Phone) return;

        if (expanded)
        {
            SidebarPanel.IsVisible = true;
            MainGrid.ColumnDefinitions[0].Width = new GridLength(218);
            SidebarHandle.IsVisible = false;
        }
        else
        {
            SidebarPanel.IsVisible = false;
            MainGrid.ColumnDefinitions[0].Width = new GridLength(0);
            SidebarHandle.IsVisible = true;
        }
    }

    private void OnNodeTapped(object? sender, NavNode node)
    {
        _vm.OnCanvasNodeTapped(node);
    }

    private async void OnNextStepClicked(object sender, EventArgs e)
    {
        if (!_vm.HasNextStep) return;
        double w = this.Width > 0 ? this.Width : 400;

        // 1. Slide current content out to the left
        await StepTextContent.TranslateTo(-w, 0, 160, Easing.CubicIn);

        // 2. Update VM — labels now show the new step text
        _vm.NextStepCommand.Execute(null);

        // 2б. Сразу ставим зум в очередь (пока _floorLoading = true).
        //     Если этаж загрузится раньше конца анимации — зум применится правильно.
        ApplyStepZoom();

        // 3. Reposition off-screen to the right (so it slides in from the right)
        StepTextContent.TranslationX = w;

        // 4. Slide in from the right
        await StepTextContent.TranslateTo(0, 0, 160, Easing.CubicOut);
    }

    private async void OnPrevStepClicked(object sender, EventArgs e)
    {
        double w = this.Width > 0 ? this.Width : 400;

        if (!_vm.HasPreviousStep)
        {
            // На первом шаге — очистить маршрут и вернуться к поиску
            await StepTextContent.TranslateTo(w, 0, 160, Easing.CubicIn);
            _vm.ClearRouteCommand.Execute(null);
            StepTextContent.TranslationX = 0;
            return;
        }

        // 1. Slide current content out to the right
        await StepTextContent.TranslateTo(w, 0, 160, Easing.CubicIn);

        // 2. Update VM
        _vm.PreviousStepCommand.Execute(null);

        // 2б. Сразу ставим зум в очередь (пока _floorLoading = true).
        ApplyStepZoom();

        // 3. Reposition off-screen to the left
        StepTextContent.TranslationX = -w;

        // 4. Slide in from the left
        await StepTextContent.TranslateTo(0, 0, 160, Easing.CubicOut);
    }

    private void ApplyStepZoom()
    {
        var step = _vm.CurrentStep;
        if (step == null) { MainCanvas.ApplyOrQueueZoom(null); return; }

        // Для шагов-переходов (лестница/лифт) и шагов «идти до лестницы» — отдаляем на 15%
        bool isTransition = step.FocusNode != null;
        bool walkToTransition = step.FocusRect != null && step.Text != null
            && (step.Text.Contains("лестниц", StringComparison.OrdinalIgnoreCase)
                || step.Text.Contains("лифт", StringComparison.OrdinalIgnoreCase));
        float zoomOut = (isTransition || walkToTransition) ? 1.15f : 1f;

        if (step.FocusRect is { } rect)
            MainCanvas.ApplyOrQueueZoom(() => MainCanvas.ZoomToFitRect(rect.MinX, rect.MinY, rect.MaxX, rect.MaxY, zoomOut));
        else if (step.FocusNode is { } node)
            MainCanvas.ApplyOrQueueZoom(() => MainCanvas.ZoomToSvgPoint(node.X, node.Y, zoomOut));
        else
            MainCanvas.ApplyOrQueueZoom(null);
    }
}
