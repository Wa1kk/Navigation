using IndoorNav.Models;
using IndoorNav.ViewModels;
using SkiaSharp;

namespace IndoorNav.Pages;

public partial class AdminPage : ContentPage
{
    private AdminViewModel Vm => (AdminViewModel)BindingContext;

    public AdminPage(AdminViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;

        // Подключаем события SvgView → команды ViewModel
        AdminCanvas.CanvasTapped += (_, svgPos) => Vm.CanvasTappedCommand.Execute(svgPos);
        AdminCanvas.NodeTapped   += (_, node)   => Vm.NodeTappedCommand.Execute(node);
        AdminCanvas.NodeMoved    += (_, args)   =>
        {
            Vm.NodeMovedCommand.Execute(args);
            // Перерисовываем вручную — PropertyChanged на координатах узла не триггерит SvgView
            AdminCanvas.InvalidateSurface();
        };
        AdminCanvas.BoundaryVertexMoved  += (_, args) =>
        {
            Vm.BoundaryVertexMovedCommand.Execute(args);
            AdminCanvas.InvalidateSurface();
        };
        AdminCanvas.BoundaryVertexTapped += (_, idx) => Vm.BoundaryVertexTappedCommand.Execute(idx);
    }

    // ← Выход из режима администратора (кнопка на телефоне)
    private async void OnExitAdminClicked(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("..");

    // Глобальный обработчик Delete — дополняет KeyboardAccelerator на кнопке
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
#if WINDOWS
        if (Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement elem)
        {
            elem.KeyDown += (_, e) =>
            {
                bool ctrl = (Microsoft.UI.Input.InputKeyboardSource
                    .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control) &
                    Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;

                if (e.Key == Windows.System.VirtualKey.Delete)
                {
                    Vm.DeleteSelectedCommand.Execute(null);
                }
                else if (ctrl && e.Key == Windows.System.VirtualKey.C)
                {
                    Vm.CopyNodeCommand.Execute(null);
                    e.Handled = true;
                }
                else if (ctrl && e.Key == Windows.System.VirtualKey.V)
                {
                    Vm.PasteNodeCommand.Execute(null);
                    e.Handled = true;
                }
            };
        }
#endif
    }
}
