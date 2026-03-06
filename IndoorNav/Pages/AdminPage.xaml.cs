using IndoorNav.Models;
using IndoorNav.ViewModels;
using SkiaSharp;

namespace IndoorNav.Pages;

public partial class AdminPage : ContentPage
{
    private AdminViewModel Vm => (AdminViewModel)BindingContext;
    private readonly MainViewModel _mainVm;

    public AdminPage(AdminViewModel viewModel, MainViewModel mainViewModel)
    {
        try
        {
        InitializeComponent();
        BindingContext = viewModel;
        _mainVm = mainViewModel;

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
        AdminCanvas.BoundaryVertexTapped += (_, args) => Vm.BoundaryVertexTappedCommand.Execute(args);
        }
        catch (Exception ex)
        {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "indoornav_crash.txt"),
                ex.ToString());
            throw;
        }
    }

    // ← Выход из режима администратора (кнопка на телефоне)
    private async void OnExitAdminClicked(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("..");

    // Выбор файла иконки для вершины графа
    private async void OnPickNodeIconClicked(object sender, EventArgs e)
    {
        if (Vm.SelectedNode == null) return;
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Выберите иконку (PNG, JPG, WEBP)",
                FileTypes   = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI,   new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp" } },
                    { DevicePlatform.Android, new[] { "image/*" } },
                    { DevicePlatform.iOS,     new[] { "public.image" } },
                }),
            });
            if (result == null) return;
            // Очищаем кеш растровых иконок, чтобы новый файл (даже с тем же путём) перезагрузился
            AdminCanvas.ClearIconCache();
            Vm.SelectedNodeIconPath = result.FullPath;
            AdminCanvas.InvalidateSurface();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Ошибка", $"Не удалось открыть файл:\n{ex.Message}", "ОК");
        }
    }

    // Кнопка связи выбранной точки — предложить удалить с подтверждением
    private async void OnEdgeButtonClicked(object sender, EventArgs e)
    {
        if (sender is not VisualElement el) return;
        if (el.BindingContext is not SelectedEdgeItem item) return;

        bool ok = await DisplayAlert(
            "Удалить связь",
            $"Удалить связь с точкой «{item.OtherNodeName}»?",
            "Удалить", "Отмена");

        if (ok)
            Vm.RemoveEdgeCommand.Execute(item);
    }

    // При открытии: синхронизируем здание и этаж из пользовательского режима
    protected override void OnAppearing()
    {
        base.OnAppearing();
        var srcBuilding = _mainVm.SelectedBuilding;
        var srcFloor    = _mainVm.SelectedFloor;
        if (srcBuilding == null) return;

        var adminBuilding = Vm.Buildings.FirstOrDefault(b => b.Id == srcBuilding.Id);
        if (adminBuilding == null) return;

        Vm.SelectedBuilding = adminBuilding;   // auto-sets floor to 1

        if (srcFloor != null)
        {
            var adminFloor = adminBuilding.Floors.FirstOrDefault(f => f.Number == srcFloor.Number);
            if (adminFloor != null)
                Vm.SelectedFloor = adminFloor;
        }
    }

    // При закрытии: синхронизируем здание и этаж обратно в пользовательский режим
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        var adminBuilding = Vm.SelectedBuilding;
        var adminFloor    = Vm.SelectedFloor;
        if (adminBuilding == null) return;

        var userBuilding = _mainVm.SelectedBuilding?.Id == adminBuilding.Id
            ? _mainVm.SelectedBuilding
            : null;
        // Здание может быть тем же объектом (shared NavGraphService), просто обновляем этаж
        if (adminFloor != null)
        {
            var userFloor = _mainVm.SelectedBuilding?.Floors
                .FirstOrDefault(f => f.Number == adminFloor.Number);
            if (userFloor != null)
                _mainVm.SelectedFloor = userFloor;
        }
    }

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
