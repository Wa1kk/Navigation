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
    }

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

        if (e.PropertyName == nameof(MainViewModel.IsSidebarExpanded))
        {
            SetSidebarExpanded(_vm.IsSidebarExpanded);
        }
    }

    // ── Node picker blur ─────────────────────────────────────────────────────

    private void ShowNodePickerBlur()
    {
        NodePickerBackdrop.IsVisible = true;
    }

    private void HideNodePickerBlur()
    {
        NodePickerBackdrop.IsVisible = false;
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
        BuildingPickerBackdrop.IsVisible = true;
        BuildingPickerSheet.TranslationY = 600;
        BuildingPickerSheet.IsVisible = true;

        // Параллельные анимации: slide-up sheet
        await BuildingPickerSheet.TranslateTo(0, 0, 300, Easing.CubicOut);
    }

    private async Task HideBuildingPickerAsync()
    {
        // Параллельно скрываем оба элемента
        await BuildingPickerSheet.TranslateTo(0, 600, 220, Easing.CubicIn);
        BuildingPickerSheet.IsVisible = false;
        BuildingPickerBackdrop.IsVisible = false;
        BuildingPickerSheet.TranslationY = 0;
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
        if (step?.FocusRect is { } rect)
            MainCanvas.ApplyOrQueueZoom(() => MainCanvas.ZoomToFitRect(rect.MinX, rect.MinY, rect.MaxX, rect.MaxY));
        else if (step?.FocusNode is { } node)
            MainCanvas.ApplyOrQueueZoom(() => MainCanvas.ZoomToSvgPoint(node.X, node.Y));
        else
            MainCanvas.ApplyOrQueueZoom(null);
    }
}
