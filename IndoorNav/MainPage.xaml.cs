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
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.HasRoute) && _vm.HasRoute)
        {
            // Откладываем на следующую итерацию главного потока — к этому моменту
            // BuildRouteSteps уже вызовет SelectedFloor (StartFloorLoad), и ApplyOrQueueZoom
            // правильно поставит зум в очередь вместо немедленного применения на старом этаже.
            Dispatcher.Dispatch(ApplyStepZoom);
        }
    }

    private void OnNodeTapped(object sender, NavNode node)
    {
        _vm.OnCanvasNodeTapped(node);
    }

    private async void OnNextStepClicked(object sender, EventArgs e)
    {
        if (!_vm.HasNextStep) return;
        double w = this.Width > 0 ? this.Width : 400;

        // 1. Slide current content out to the left
        await StepContent.TranslateTo(-w, 0, 160, Easing.CubicIn);

        // 2. Update VM — labels now show the new step text
        _vm.NextStepCommand.Execute(null);

        // 2б. Сразу ставим зум в очередь (пока _floorLoading = true).
        //     Если этаж загрузится раньше конца анимации — зум применится правильно.
        ApplyStepZoom();

        // 3. Reposition off-screen to the right (so it slides in from the right)
        StepContent.TranslationX = w;

        // 4. Slide in from the right
        await StepContent.TranslateTo(0, 0, 160, Easing.CubicOut);
    }

    private async void OnPrevStepClicked(object sender, EventArgs e)
    {
        if (!_vm.HasPreviousStep) return;
        double w = this.Width > 0 ? this.Width : 400;

        // 1. Slide current content out to the right
        await StepContent.TranslateTo(w, 0, 160, Easing.CubicIn);

        // 2. Update VM
        _vm.PreviousStepCommand.Execute(null);

        // 2б. Сразу ставим зум в очередь (пока _floorLoading = true).
        ApplyStepZoom();

        // 3. Reposition off-screen to the left
        StepContent.TranslationX = -w;

        // 4. Slide in from the left
        await StepContent.TranslateTo(0, 0, 160, Easing.CubicOut);
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
