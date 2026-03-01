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
                if (e.Key == Windows.System.VirtualKey.Delete)
                    Vm.DeleteSelectedCommand.Execute(null);
            };
        }
#endif
    }
}
