using IndoorNav.Models;
using IndoorNav.ViewModels;
using SkiaSharp;
using Microsoft.Maui;
using Microsoft.Maui.Storage;
using Microsoft.Maui.ApplicationModel.DataTransfer;
#if IOS || MACCATALYST
using Foundation;
using UIKit;
#endif

namespace IndoorNav.Pages;

public partial class AdminPage : ContentPage
{
    private AdminViewModel Vm => (AdminViewModel)BindingContext;
    private readonly MainViewModel _mainVm;
#if IOS || MACCATALYST
    private UIImageView? _qrInteractionImageView;
    private UILongPressGestureRecognizer? _qrLongPressRecognizer;
    private UIDragInteraction? _qrDragInteraction;
    private QrDragInteractionDelegate? _qrDragDelegate;
#endif

    public AdminPage(AdminViewModel viewModel, MainViewModel mainViewModel)
    {
        try
        {
        InitializeComponent();
        BindingContext = viewModel;
        _mainVm = mainViewModel;
        viewModel.PropertyChanged += OnAdminVmPropertyChanged;

#if IOS || MACCATALYST
        Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.Page.SetUseSafeArea(this, false);
#endif

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

    private void OnAdminVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AdminViewModel.QrPopupVisible) && Vm.QrPopupVisible)
            Dispatcher.Dispatch(InstallQrImageInteractions);
    }

    // ← Выход из режима администратора (кнопка на телефоне)
    private async void OnExitAdminClicked(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("..");

#if IOS || MACCATALYST
    private void ApplySafeAreaPadding()
    {
        if (Handler?.PlatformView is UIKit.UIView nativeView)
        {
            var insets = nativeView.SafeAreaInsets;
            AdminGrid.Padding = new Thickness(
                insets.Left,
                insets.Top,
                insets.Right,
                insets.Bottom);
        }
    }
#endif

    /// <summary>
    /// Нормализует путь иконки для разных платформ.
    /// На Desktop копирует файл в Resources/Raw/Icons/ проекта.
    /// На iOS/Android сохраняет в AppDataDirectory и возвращает относительный путь.
    /// </summary>
    private static string NormalizeIconPath(string fullPath)
    {
        try
        {
            var fileName = Path.GetFileName(fullPath);
            
#if IOS || ANDROID
            // На мобильных платформах сохраняем в AppDataDirectory
            var iconsDir = Path.Combine(FileSystem.AppDataDirectory, "Icons");
            Directory.CreateDirectory(iconsDir);
            var dest = Path.Combine(iconsDir, fileName);
            if (File.Exists(fullPath))
                File.Copy(fullPath, dest, overwrite: true);
            // Возвращаем путь относительно AppDataDirectory
            return "Icons/" + fileName;
#else
            // На Desktop используем Resources/Raw/Icons
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "IndoorNav.csproj")))
                dir = dir.Parent;
            if (dir == null) return "Icons/" + fileName;

            var projectIconsDir = Path.Combine(dir.FullName, "Resources", "Raw", "Icons");
            Directory.CreateDirectory(projectIconsDir);
            var destPath = Path.Combine(projectIconsDir, fileName);
            if (File.Exists(fullPath) && !File.Exists(destPath))
                File.Copy(fullPath, destPath);
            return "Icons/" + fileName;
#endif
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NormalizeIconPath error: {ex.Message}");
            return fullPath;
        }
    }

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
            Vm.SelectedNodeIconPath = NormalizeIconPath(result.FullPath);
            AdminCanvas.InvalidateSurface();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Ошибка", $"Не удалось открыть файл:\n{ex.Message}", "ОК");
        }
    }

    // Выбор файла иконки для нескольких выделенных точек (режим мультивыбора)
    private async void OnPickIconForSelectionClicked(object sender, EventArgs e)
    {
        if (!Vm.HasMultiSelection) return;
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Выберите иконку для всех выбранных точек",
                FileTypes   = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI,   new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp" } },
                    { DevicePlatform.Android, new[] { "image/*" } },
                    { DevicePlatform.iOS,     new[] { "public.image" } },
                }),
            });
            if (result == null) return;
            AdminCanvas.ClearIconCache();
            Vm.SetIconForSelection(NormalizeIconPath(result.FullPath));
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

    private async void OnSaveQrCodeClicked(object sender, EventArgs e)
    {
        await ShowQrImageActionsAsync(includeCopy: false);
    }

    private async Task ShowQrImageActionsAsync(bool includeCopy)
    {
        var bytes = Vm.QrCurrentPngBytes;
        if (bytes == null || bytes.Length == 0) return;

        var actions = includeCopy
            ? new[] { "Копировать изображение", "Сохранить в Фото", "Сохранить в Файлы", "Поделиться" }
            : new[] { "Сохранить в Фото", "Сохранить в Файлы", "Поделиться" };

        var action = await DisplayActionSheet("QR-код", "Отмена", null, actions);
        if (string.IsNullOrWhiteSpace(action) || action == "Отмена") return;

        try
        {
            switch (action)
            {
                case "Копировать изображение":
                    CopyQrImageToPasteboard(bytes);
                    Vm.SetStatusText("QR-код скопирован как изображение");
                    break;
                case "Сохранить в Фото":
#if IOS || MACCATALYST
                    await SaveQrImageToPhotosAsync(bytes);
                    Vm.SetStatusText("QR-код сохранён в Фото");
#else
                    await ShareQrImageAsync(bytes, Vm.QrPngFileName);
#endif
                    break;
                case "Сохранить в Файлы":
#if IOS || MACCATALYST
                    ExportQrImageToFiles(bytes, Vm.QrPngFileName);
                    Vm.SetStatusText("Выберите папку в Файлах для QR-кода");
#else
                    await ShareQrImageAsync(bytes, Vm.QrPngFileName);
#endif
                    break;
                case "Поделиться":
                    await ShareQrImageAsync(bytes, Vm.QrPngFileName);
                    break;
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Ошибка", $"Не удалось выполнить действие:\n{ex.Message}", "ОК");
        }
    }

    private static string WriteQrTempFile(byte[] bytes, string fileName)
    {
        var dir = Path.Combine(FileSystem.CacheDirectory, "QrCodes");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private async Task ShareQrImageAsync(byte[] bytes, string fileName)
    {
#if IOS || MACCATALYST
        ShareQrImageNative(bytes, fileName);
        await Task.CompletedTask;
#else
        var path = WriteQrTempFile(bytes, fileName);
        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "QR-код",
            File = new ShareFile(path)
        });
#endif
    }

    private void InstallQrImageInteractions()
    {
#if IOS || MACCATALYST
        if (QrCodeImage.Handler?.PlatformView is not UIImageView imageView) return;
        if (_qrInteractionImageView == imageView) return;

        _qrInteractionImageView = imageView;
        imageView.UserInteractionEnabled = true;

        _qrLongPressRecognizer = new UILongPressGestureRecognizer(OnQrImageLongPressed)
        {
            MinimumPressDuration = 0.45,
            CancelsTouchesInView = false
        };
        imageView.AddGestureRecognizer(_qrLongPressRecognizer);

        _qrDragDelegate ??= new QrDragInteractionDelegate(() => Vm.QrCurrentPngBytes);
        _qrDragInteraction = new UIDragInteraction(_qrDragDelegate)
        {
            Enabled = true
        };
        imageView.AddInteraction(_qrDragInteraction);
#endif
    }

#if IOS || MACCATALYST
    private async void OnQrImageLongPressed(UILongPressGestureRecognizer recognizer)
    {
        if (recognizer.State != UIGestureRecognizerState.Ended) return;
        await ShowQrImageActionsAsync(includeCopy: true);
    }

    private static UIImage? CreateQrUiImage(byte[] bytes)
    {
        using var data = NSData.FromArray(bytes);
        return UIImage.LoadFromData(data);
    }

    private static void CopyQrImageToPasteboard(byte[] bytes)
    {
        var image = CreateQrUiImage(bytes);
        if (image == null)
            throw new InvalidOperationException("QR изображение не создано");
        UIPasteboard.General.Image = image;
    }

    private async Task SaveQrImageToPhotosAsync(byte[] bytes)
    {
        var image = CreateQrUiImage(bytes);
        if (image == null)
            throw new InvalidOperationException("QR изображение не создано");

        var tcs = new TaskCompletionSource<string?>();
        image.SaveToPhotosAlbum((_, error) =>
        {
            tcs.TrySetResult(error?.LocalizedDescription);
        });

        var errorMessage = await tcs.Task;
        if (!string.IsNullOrWhiteSpace(errorMessage))
            throw new InvalidOperationException(errorMessage);
    }

    private void ExportQrImageToFiles(byte[] bytes, string fileName)
    {
        var path = WriteQrTempFile(bytes, fileName);
        var picker = new UIDocumentPickerViewController(
            new[] { NSUrl.FromFilename(path) },
            true);
        PresentIosController(picker);
    }

    private void ShareQrImageNative(byte[] bytes, string fileName)
    {
        var path = WriteQrTempFile(bytes, fileName);
        var url = NSUrl.FromFilename(path);
        var image = CreateQrUiImage(bytes);
        var items = image == null
            ? new NSObject[] { url }
            : new NSObject[] { image, url };
        var controller = new UIActivityViewController(items, null);
        PresentIosController(controller);
    }

    private void PresentIosController(UIViewController controller)
    {
        if (QrCodeImage.Handler?.PlatformView is UIView source &&
            controller.PopoverPresentationController != null)
        {
            controller.PopoverPresentationController.SourceView = source;
            controller.PopoverPresentationController.SourceRect = source.Bounds;
        }

        var presenter = GetTopViewController();
        presenter?.PresentViewController(controller, true, null);
    }

    private UIViewController? GetTopViewController()
    {
        if (Handler?.PlatformView is not UIView view) return null;
        var controller = view.Window?.RootViewController;
        while (controller?.PresentedViewController != null)
            controller = controller.PresentedViewController;
        return controller;
    }

    private sealed class QrDragInteractionDelegate : UIDragInteractionDelegate
    {
        private readonly Func<byte[]?> _getBytes;

        public QrDragInteractionDelegate(Func<byte[]?> getBytes)
        {
            _getBytes = getBytes;
        }

        public override UIDragItem[] GetItemsForBeginningSession(UIDragInteraction interaction, IUIDragSession session)
        {
            var bytes = _getBytes();
            if (bytes == null || bytes.Length == 0)
                return Array.Empty<UIDragItem>();

            var image = CreateQrUiImage(bytes);
            if (image == null)
                return Array.Empty<UIDragItem>();

            var itemProvider = new NSItemProvider(image);
            return new[] { new UIDragItem(itemProvider) { LocalObject = image } };
        }
    }
#else
    private static void CopyQrImageToPasteboard(byte[] bytes)
    {
    }
#endif

    // При открытии: синхронизируем здание и этаж из пользовательского режима
    protected override void OnAppearing()
    {
        base.OnAppearing();
        Services.EdgeColorService.SetEdgeColor(this, "#0F172A");
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
        Services.EdgeColorService.SetEdgeColor(this, "#F1F5F9");
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
#if IOS || MACCATALYST
        ApplySafeAreaPadding();
        InstallQrImageInteractions();
#endif
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
