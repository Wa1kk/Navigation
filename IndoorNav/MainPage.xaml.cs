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

        // 3. Reposition off-screen to the right (so it slides in from the right)
        StepContent.TranslationX = w;

        // 4. Slide in from the right
        await StepContent.TranslateTo(0, 0, 160, Easing.CubicOut);

        // 5. Ясли у шага есть узел фокуса — приближаемся, иначе сбрасываем зум
        ApplyStepZoom();
    }

    private async void OnPrevStepClicked(object sender, EventArgs e)
    {
        if (!_vm.HasPreviousStep) return;
        double w = this.Width > 0 ? this.Width : 400;

        // 1. Slide current content out to the right
        await StepContent.TranslateTo(w, 0, 160, Easing.CubicIn);

        // 2. Update VM
        _vm.PreviousStepCommand.Execute(null);

        // 3. Reposition off-screen to the left
        StepContent.TranslationX = -w;

        // 4. Slide in from the left
        await StepContent.TranslateTo(0, 0, 160, Easing.CubicOut);

        // 5. Зум / сброс
        ApplyStepZoom();
    }

    private void ApplyStepZoom()
    {
        var step = _vm.CurrentStep;
        if (step?.FocusRect is { } rect)
            MainCanvas.ZoomToFitRect(rect.MinX, rect.MinY, rect.MaxX, rect.MaxY);
        else if (step?.FocusNode is { } node)
            MainCanvas.ZoomToSvgPoint(node.X, node.Y);
        else
            MainCanvas.ResetZoom();
    }
}
