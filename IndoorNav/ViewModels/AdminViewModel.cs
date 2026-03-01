using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using IndoorNav.Models;
using IndoorNav.Services;
using SkiaSharp;

namespace IndoorNav.ViewModels;

public enum AdminAction { None, AddNode, AddTransition, ConnectNode, DisconnectNode, DrawCorridor, MoveNode, DrawBoundary }

public class AdminViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly NavGraphService _graphService;

    // ---- Навигация по зданиям/этажам ----
    private Building?      _selectedBuilding;
    private Floor?         _selectedFloor;
    private AdminAction    _currentAction;
    private NavNode?       _selectedNode;
    private string         _statusText = "";

    public ObservableCollection<Building> Buildings { get; } = new();

    public Building? SelectedBuilding
    {
        get => _selectedBuilding;
        set
        {
            if (_selectedBuilding == value) return;
            _selectedBuilding = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Floors));
            SelectedFloor = value?.Floors.FirstOrDefault(f => f.Number == 1)
                         ?? value?.Floors.FirstOrDefault();
        }
    }

    public ObservableCollection<Floor> Floors
    {
        get
        {
            var result = new ObservableCollection<Floor>();
            foreach (var f in _selectedBuilding?.Floors ?? [])
                result.Add(f);
            return result;
        }
    }

    public Floor? SelectedFloor
    {
        get => _selectedFloor;
        set
        {
            if (_selectedFloor == value) return;
            _selectedFloor = value;
            OnPropertyChanged();
            RefreshOverlay();
        }
    }

    // ---- Граф (текущий этаж) ----
    private ObservableCollection<NavNode> _currentNodes = new();
    private ObservableCollection<NavEdge> _currentEdges = new();

    public ObservableCollection<NavNode> CurrentNodes
    {
        get => _currentNodes;
        private set { _currentNodes = value; OnPropertyChanged(); }
    }
    public ObservableCollection<NavEdge> CurrentEdges
    {
        get => _currentEdges;
        private set { _currentEdges = value; OnPropertyChanged(); }
    }

    public NavNode? SelectedNode
    {
        get => _selectedNode;
        // RefreshOverlay НЕ вызываем здесь: SvgView сам перерисовывается
        // через биндинг SelectedNode → InvalidateSurface. Вызов RefreshOverlay
        // при каждом тапе пересоздавал все коллекции и вызывал заметную задержку.
        set { _selectedNode = value; OnPropertyChanged(); }
    }

    public AdminAction CurrentAction
    {
        get => _currentAction;
        set
        {
            _currentAction = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAddMode));
            OnPropertyChanged(nameof(IsConnectMode));
            OnPropertyChanged(nameof(IsDisconnectMode));
            OnPropertyChanged(nameof(IsCorridorMode));
            OnPropertyChanged(nameof(IsMoveMode));
            OnPropertyChanged(nameof(IsTransitionMode));
            OnPropertyChanged(nameof(IsBoundaryMode));
            UpdateStatus();
        }
    }

    public bool IsAddMode        => CurrentAction == AdminAction.AddNode;
    public bool IsTransitionMode => CurrentAction == AdminAction.AddTransition;
    public bool IsConnectMode    => CurrentAction == AdminAction.ConnectNode;
    public bool IsDisconnectMode => CurrentAction == AdminAction.DisconnectNode;
    public bool IsCorridorMode   => CurrentAction == AdminAction.DrawCorridor;
    public bool IsMoveMode       => CurrentAction == AdminAction.MoveNode;
    public bool IsBoundaryMode   => CurrentAction == AdminAction.DrawBoundary;

    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; OnPropertyChanged(); }
    }

    // ---- Commands ----

    public Command<SKPoint> CanvasTappedCommand  { get; }
    public Command<NavNode> NodeTappedCommand    { get; }
    public Command<(NavNode, SKPoint)> NodeMovedCommand { get; }
    public Command RenameSelectedCommand         { get; }
    public Command DeleteSelectedCommand         { get; }
    public Command SaveCommand                   { get; }
    public Command CancelActionCommand           { get; }
    public Command SetAddModeCommand             { get; }
    public Command SetConnectModeCommand         { get; }
    public Command SetDisconnectModeCommand      { get; }
    public Command SetCorridorModeCommand        { get; }
    public Command FinishCorridorCommand         { get; }
    public Command SetMoveModeCommand            { get; }
    public Command ConfirmMoveCommand            { get; }
    public Command SetTransitionModeCommand      { get; }
    public Command ResetGraphCommand             { get; }
    public Command SetBoundaryModeCommand        { get; }
    public Command FinishBoundaryCommand         { get; }
    public Command ClearBoundaryCommand          { get; }

    // Граница аудитории (черновик)
    private readonly List<SkiaSharp.SKPoint> _boundaryPoints = new();

    /// <summary>Точки в процессе рисования — передаются в SvgView для превью.
    /// Возвращаем новый экземпляр, чтобы привязка MAUI фиксировала изменение ссылки
    /// и вызывала InvalidateSurface() при каждом OnPropertyChanged.</summary>
    public IList<SkiaSharp.SKPoint> BoundaryPreview => new List<SkiaSharp.SKPoint>(_boundaryPoints);

    // Последний узел в цепочке коридора
    private NavNode? _corridorLastNode;
    private int      _corridorStepCount;

    public AdminViewModel(NavGraphService graphService, MainViewModel mainViewModel)
    {
        _graphService = graphService;

        foreach (var b in mainViewModel.Buildings)
            Buildings.Add(b);

        // Если здания ещё не загрузились — подпишемся на их появление
        mainViewModel.Buildings.CollectionChanged += (_, e) =>
        {
            if (e.NewItems == null) return;
            foreach (Building b in e.NewItems)
                if (!Buildings.Any(x => x.Id == b.Id))
                    Buildings.Add(b);
        };

        CanvasTappedCommand   = new Command<SKPoint>(OnCanvasTapped);
        NodeTappedCommand     = new Command<NavNode>(OnNodeTapped);
        NodeMovedCommand      = new Command<(NavNode, SKPoint)>(OnNodeMoved);
        RenameSelectedCommand = new Command(async () => await RenameSelectedAsync(), () => SelectedNode != null);
        DeleteSelectedCommand = new Command(DeleteSelected, () => SelectedNode != null);
        SaveCommand           = new Command(async () => await _graphService.SaveAsync());
        CancelActionCommand   = new Command(() =>
        {
            _corridorLastNode  = null;
            _corridorStepCount = 0;
            _boundaryPoints.Clear();
            OnPropertyChanged(nameof(BoundaryPreview));
            CurrentAction      = AdminAction.None;
            SelectedNode       = null;
        });
        SetAddModeCommand        = new Command(() => CurrentAction = AdminAction.AddNode);
        SetConnectModeCommand    = new Command(() => CurrentAction = AdminAction.ConnectNode);
        SetDisconnectModeCommand = new Command(() => CurrentAction = AdminAction.DisconnectNode);
        SetCorridorModeCommand   = new Command(() =>
        {
            _corridorLastNode  = null;
            _corridorStepCount = 0;
            CurrentAction      = AdminAction.DrawCorridor;
        });
        FinishCorridorCommand = new Command(() =>
        {
            _corridorLastNode  = null;
            _corridorStepCount = 0;
            CurrentAction      = AdminAction.None;
            StatusText         = "Коридор завершён. Не забудьте сохранить граф.";
        });
        SetMoveModeCommand = new Command(() =>
        {
            CurrentAction = AdminAction.MoveNode;
            StatusText    = "Режим перемещения: зажмите узел и перетащите. Нажмите [✓ Подтвердить] для сохранения.";
        });
        ConfirmMoveCommand = new Command(async () =>
        {
            await _graphService.SaveAsync();
            CurrentAction = AdminAction.None;
            StatusText    = "Положения узлов сохранены.";
        });
        SetTransitionModeCommand = new Command(() =>
        {
            CurrentAction = AdminAction.AddTransition;
            StatusText    = "Режим перехода: нажмите на план — будет добавлена точка лестницы/лифта.";
        });
        ResetGraphCommand = new Command(async () =>
        {
            bool ok = await Shell.Current.DisplayAlert(
                "Сброс графа",
                "Удалить ВСЕ узлы и рёбра? Это действие нельзя отменить.",
                "Да, удалить", "Отмена");
            if (!ok) return;
            await _graphService.ResetAsync();
            RefreshOverlay();
            StatusText = "Граф очищен. Начните заново.";
        });

        SetBoundaryModeCommand = new Command(() =>
        {
            if (SelectedNode == null)
            {
                StatusText = "Сначала выберите узел (аудиторию), затем нажмите [Граница]."; return;
            }
            _boundaryPoints.Clear();
            CurrentAction = AdminAction.DrawBoundary;
            StatusText = $"Граница [{SelectedNode.Name}]: нажмите углы области на плане. Нажмите [✓ Замкнуть] по завершении.";
            OnPropertyChanged(nameof(BoundaryPreview));
        }, () => SelectedNode != null);

        FinishBoundaryCommand = new Command(async () =>
        {
            if (SelectedNode == null || _boundaryPoints.Count < 3)
            {
                StatusText = "Нужно не менее 3 точек для замыкания полигона."; return;
            }
            SelectedNode.Boundary = _boundaryPoints
                .Select(p => new float[] { p.X, p.Y })
                .ToList();
            _boundaryPoints.Clear();
            CurrentAction = AdminAction.None;
            await _graphService.SaveAsync();
            StatusText = $"Граница [{SelectedNode.Name}] сохранена ({SelectedNode.Boundary.Count} углов).";
            OnPropertyChanged(nameof(BoundaryPreview));
            RefreshOverlay();
        });

        ClearBoundaryCommand = new Command(async () =>
        {
            if (SelectedNode == null) return;
            bool ok = await Shell.Current.DisplayAlert(
                "Очистить границу",
                $"Удалить границу аудитории [{SelectedNode.Name}]?",
                "Удалить", "Отмена");
            if (!ok) return;
            SelectedNode.Boundary = null;
            await _graphService.SaveAsync();
            StatusText = $"Граница [{SelectedNode.Name}] удалена."; 
            RefreshOverlay();
        }, () => SelectedNode?.Boundary != null);

        SelectedBuilding = Buildings.FirstOrDefault();  // SelectedFloor auto-set to floor 1 via setter
    }

    // ---- Canvas interactions ----

    private async void OnCanvasTapped(SKPoint svgPos)
    {
        if (SelectedFloor == null || SelectedBuilding == null) return;

        if (CurrentAction == AdminAction.DrawBoundary)
        {
            if (SelectedNode == null) return;
            _boundaryPoints.Add(svgPos);
            StatusText = $"Граница [{SelectedNode.Name}]: {_boundaryPoints.Count} точек. [✓ Замкнуть] когда закончите.";
            OnPropertyChanged(nameof(BoundaryPreview));
            return;
        }

        if (CurrentAction == AdminAction.AddNode || CurrentAction == AdminAction.AddTransition)
        {
            bool forceTransit = CurrentAction == AdminAction.AddTransition;
            string title   = forceTransit ? "Точка перехода" : "Новый узел";
            string promptMsg = forceTransit
                ? "Название лестницы / лифта:"
                : "Введите название аудитории / точки:";
            string placeholder = forceTransit ? "Например: Лестница А, Лифт 1" : "Например: 101, Лифт, Лестница А";

            var name = await Shell.Current.DisplayPromptAsync(title, promptMsg,
                placeholder: placeholder, maxLength: 40);

            if (string.IsNullOrWhiteSpace(name)) return;

            bool isTransit = forceTransit
                          || name.Contains("лифт",     StringComparison.OrdinalIgnoreCase)
                          || name.Contains("лестниц", StringComparison.OrdinalIgnoreCase);

            var node = new NavNode
            {
                Name         = name.Trim(),
                BuildingId   = SelectedBuilding.Id,
                FloorNumber  = SelectedFloor.Number,
                X            = svgPos.X,
                Y            = svgPos.Y,
                IsTransition = isTransit
            };

            _graphService.AddNode(node);
            RefreshOverlay();
            CurrentAction = AdminAction.None;
            StatusText    = $"Добавлен: {node.Name}{(isTransit ? " (переход)" : string.Empty)}";
        }
        else if (CurrentAction == AdminAction.DrawCorridor)
        {
            await AddCorridorNodeAsync(svgPos);
        }
    }

    private Task AddCorridorNodeAsync(SKPoint svgPos)
    {
        _corridorStepCount++;

        bool isTransit = false; // промежуточные точки коридора никогда не являются переходами
        string nodeName = $"wp{_corridorStepCount}";

        var node = new NavNode
        {
            Name         = nodeName,
            BuildingId   = SelectedBuilding!.Id,
            FloorNumber  = SelectedFloor!.Number,
            X            = svgPos.X,
            Y            = svgPos.Y,
            IsTransition = isTransit,
            // ВСЕ точки коридора — вспомогательные waypoints:
            // пользователь их не видит и не может выбрать.
            // Аудитории — через кнопку "+ Добавить узел".
            // Лестницы/лифты — через "🚶 Лестница / Лифт".
            IsWaypoint   = true
        };

        _graphService.AddNode(node);

        if (_corridorLastNode != null)
            _graphService.AddEdge(_corridorLastNode.Id, node.Id, false);

        _corridorLastNode = node;
        SelectedNode      = node;   // выделяем оранжевым — визуальная подсказка
        RefreshOverlay();
        StatusText = $"Коридор: {_corridorStepCount} точек. Продолжайте или нажмите [Завершить коридор].";
        return Task.CompletedTask;
    }

    private void OnNodeTapped(NavNode node)
    {
        switch (CurrentAction)
        {
            case AdminAction.DrawCorridor:
                // Тап на существующий узел:
                if (_corridorLastNode == null)
                {
                    // Начать/продолжить коридор с этого узла
                    _corridorLastNode  = node;
                    _corridorStepCount = 1;
                    SelectedNode = node;
                    StatusText = $"[Коридор] Начало: [{node.Name}]. Нажмите на плане по коридору.";
                }
                else if (_corridorLastNode.Id != node.Id)
                {
                    // Завершить цепочку, присоединив к уже существующему узлу
                    bool cross = _corridorLastNode.FloorNumber != node.FloorNumber
                              || _corridorLastNode.BuildingId  != node.BuildingId;
                    _graphService.AddEdge(_corridorLastNode.Id, node.Id, cross);
                    _corridorStepCount++;
                    StatusText = $"Коридор присоединён к [{node.Name}]. Цепочка {_corridorStepCount} точек.";
                    _corridorLastNode  = null;
                    _corridorStepCount = 0;
                    CurrentAction      = AdminAction.None;
                    RefreshOverlay();
                }
                break;
            case AdminAction.ConnectNode:
                if (SelectedNode != null && SelectedNode.Id != node.Id)
                {
                    bool cross = SelectedNode.FloorNumber != node.FloorNumber
                              || SelectedNode.BuildingId  != node.BuildingId;
                    _graphService.AddEdge(SelectedNode.Id, node.Id, cross);
                    StatusText    = $"Соединено: {SelectedNode.Name} ↔ {node.Name}";
                    CurrentAction = AdminAction.None;
                    SelectedNode  = null;
                    RefreshOverlay();
                }
                else
                {
                    SelectedNode = node;
                    StatusText   = $"Выбран: {node.Name}. Нажмите на второй узел для соединения.";
                }
                break;

            case AdminAction.DisconnectNode:
                if (SelectedNode != null && SelectedNode.Id != node.Id)
                {
                    _graphService.RemoveEdge(SelectedNode.Id, node.Id);
                    StatusText    = $"Разъединено: {SelectedNode.Name} ↔ {node.Name}";
                    CurrentAction = AdminAction.None;
                    SelectedNode  = null;
                    RefreshOverlay();
                }
                else
                {
                    SelectedNode = node;
                    StatusText   = $"Выбран: {node.Name}. Нажмите на второй узел для разъединения.";
                }
                break;

            default:
                SelectedNode = node;
                StatusText   = $"Выбран: {node.Name} ({node.X:F0}, {node.Y:F0})";
                DeleteSelectedCommand.ChangeCanExecute();
                RenameSelectedCommand.ChangeCanExecute();
                SetBoundaryModeCommand.ChangeCanExecute();
                ClearBoundaryCommand.ChangeCanExecute();
                break;
        }
    }

    private void OnNodeMoved((NavNode node, SKPoint svgPos) arg)
    {
        arg.node.X = arg.svgPos.X;
        arg.node.Y = arg.svgPos.Y;
        StatusText = $"{arg.node.Name}: ({arg.svgPos.X:F0}, {arg.svgPos.Y:F0})";
    }

    private async Task RenameSelectedAsync()
    {
        if (SelectedNode == null) return;
        var newName = await Shell.Current.DisplayPromptAsync(
            "Переименовать узел",
            "Введите новое название:",
            initialValue: SelectedNode.Name,
            maxLength: 40);
        if (string.IsNullOrWhiteSpace(newName)) return;
        SelectedNode.Name = newName.Trim();
        StatusText = $"Переименован: {SelectedNode.Name}";
        // Перерисовываем оверлей, чтобы подпись на точке обновилась.
        RefreshOverlay();
        // Восстанавливаем выбор оранжевой подсветки после RefreshOverlay.
        SelectedNode = CurrentNodes.FirstOrDefault(n => n.Id == SelectedNode?.Id) ?? SelectedNode;
    }

    private void DeleteSelected()
    {
        if (SelectedNode == null) return;
        StatusText = $"Удалён: {SelectedNode.Name}";
        _graphService.RemoveNode(SelectedNode.Id);
        SelectedNode = null;
        RefreshOverlay();
        DeleteSelectedCommand.ChangeCanExecute();
        RenameSelectedCommand.ChangeCanExecute();
    }

    // ---- Helpers ----

    public void RefreshOverlay()
    {
        if (SelectedFloor == null || SelectedBuilding == null)
        {
            CurrentNodes = new ObservableCollection<NavNode>();
            CurrentEdges = new ObservableCollection<NavEdge>();
            return;
        }

        var graph = _graphService.Graph;

        // Присваиваем новые коллекции целиком — одно PropertyChanged вместо
        // N×CollectionChanged, которые вызывали InvalidateSurface на каждый элемент.
        CurrentNodes = new ObservableCollection<NavNode>(
            graph.GetNodesForFloor(SelectedBuilding.Id, SelectedFloor.Number));

        // Используем Admin-метод: включает межэтажные рёбра.
        // HashSet для O(1) проверки дублей обратного ребра.
        var seenPairs = new HashSet<(string, string)>();
        var edges = new List<NavEdge>();
        foreach (var e in graph.GetEdgesForFloorAdmin(SelectedBuilding.Id, SelectedFloor.Number))
        {
            var canonical = string.CompareOrdinal(e.FromId, e.ToId) <= 0
                ? (e.FromId, e.ToId)
                : (e.ToId, e.FromId);
            if (seenPairs.Add(canonical))
                edges.Add(e);
        }
        CurrentEdges = new ObservableCollection<NavEdge>(edges);
    }

    private void UpdateStatus()
    {
        StatusText = CurrentAction switch
        {
            AdminAction.AddNode        => "Нажмите на карту для добавления узла. Совет: добавляйте узлы вдоль коридоров для точного маршрута",
            AdminAction.ConnectNode    => "Нажмите первый, затем второй узел для соединения",
            AdminAction.DisconnectNode => "Нажмите первый, затем второй узел для разъединения",
            AdminAction.DrawCorridor   => _corridorLastNode == null
                ? "[Коридор] Нажмите на старт коридора или аудиторию"
                : $"[Коридор] Точек: {_corridorStepCount}. Нажмите следующий поворот, тап по узлу — завершить цепочку",
            AdminAction.DrawBoundary   => $"Режим границ: нажимайте углы области [{SelectedNode?.Name}] на плане ({_boundaryPoints.Count} точ.ек)",
            _ => SelectedNode != null ? $"Выбран: {SelectedNode.Name}" : "Выберите узел или действие"
        };
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
