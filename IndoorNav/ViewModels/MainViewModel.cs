using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using IndoorNav.Models;
using IndoorNav.Services;
using Microsoft.Maui;
using Microsoft.Maui.Storage;

namespace IndoorNav.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly NavGraphService _graphService;
    private readonly AuthService _authService;
    private readonly EmergencyService _emergencyService;
    private readonly ScheduleService _scheduleService;

    private Building? _selectedBuilding;
    private Floor? _selectedFloor;
    private bool _isLoading;

    // --- Routing ---
    private NavNode? _startNode;
    private NavNode? _endNode;
    private string _routeStatus = string.Empty;
    private bool _isPickerOpen;
    private NavNode? _tappedNode;
    private bool _isNodePopupOpen;
    private string _pickerTarget = "start"; // "start" or "end"
    private string _pickerSearchText = string.Empty;
    private List<FloorNodeGroup> _nodesByFloor = new();
    private ObservableCollection<NavNode> _routeNodesOnFloor = new();
    private ObservableCollection<NavNode> _nodesOnCurrentFloor = new();
    private ObservableCollection<NavEdge> _edgesOnCurrentFloor = new();
    private ObservableCollection<NavNode> _allNodesForBuilding = new();

    // --- Пошаговые инструкции ---
    private readonly List<RouteStep> _routeStepsList = new();
    private int _currentStepIndex;

    // Конфигурация зданий: Id → отображаемое название.
    // Чтобы добавить новое здание — просто допишите строку сюда.
    private static readonly (string Id, string Name)[] BuildingConfig =
    [
        ("BuildingA", "Верхний корпус"),
        ("BuildingB", "Нижний корпус"),
    ];

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
            // Предпочитаем 1-й этаж, иначе первый доступный
            SelectedFloor = value?.Floors.FirstOrDefault(f => f.Number == 1)
                         ?? value?.Floors.FirstOrDefault();
            RefreshNodesForBuilding();
        }
    }

    public ObservableCollection<Floor> Floors
    {
        get
        {
            var floors = new ObservableCollection<Floor>();
            if (_selectedBuilding != null)
                foreach (var f in _selectedBuilding.Floors)
                    floors.Add(f);
            return floors;
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
            RefreshFloorOverlay();
            RefreshRoute();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set { _isLoading = value; OnPropertyChanged(); }
    }

    private string _loadError = string.Empty;
    public string LoadError
    {
        get => _loadError;
        private set { _loadError = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasLoadError)); }
    }
    public bool HasLoadError => !string.IsNullOrEmpty(_loadError);

    // ── Auth / Emergency ──────────────────────────────────────────────────────
    public bool IsAdminUser => _authService?.CurrentRole == UserRole.Admin;
    public string CurrentUserName => _authService?.CurrentUser?.DisplayName ?? "Гость";

    private bool _isEmergencyActive;
    public bool IsEmergencyActive
    {
        get => _isEmergencyActive;
        private set { _isEmergencyActive = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowFireExtinguishers)); OnPropertyChanged(nameof(EmergencyMessage)); OnPropertyChanged(nameof(IsNormalMode)); }
    }
    public string EmergencyMessage => _emergencyService?.EmergencyMessage ?? string.Empty;
    /// <summary>Передаётся в SvgView.ShowFireExtinguishers — огнетушители видны только при ЧС.</summary>
    public bool ShowFireExtinguishers => _isEmergencyActive;
    /// <summary>Инверсия для скрытия поля ДО в обычном режиме.</summary>
    public bool IsNormalMode => !_isEmergencyActive;

    // ── Emergency confirmation UX ────────────────────────────────────────────
    private bool _showEmergencyConfirmation;
    /// <summary>Visible when emergency is active and an auto-location was detected from schedule.</summary>
    public bool ShowEmergencyConfirmation
    {
        get => _showEmergencyConfirmation;
        private set { _showEmergencyConfirmation = value; OnPropertyChanged(); }
    }

    private string _emergencyAutoLocationName = string.Empty;
    /// <summary>Display name of the room auto-detected for the student via schedule.</summary>
    public string EmergencyAutoLocationName
    {
        get => _emergencyAutoLocationName;
        private set { _emergencyAutoLocationName = value; OnPropertyChanged(); }
    }

    // ── Routing properties ──────────────────────────────────────────────────

    /// <summary>All nodes that belong to the currently selected building.</summary>
    public ObservableCollection<NavNode> AllNodesForBuilding
    {
        get => _allNodesForBuilding;
        private set { _allNodesForBuilding = value; OnPropertyChanged(); }
    }

    /// <summary>Node where the user is currently located.</summary>
    public NavNode? StartNode
    {
        get => _startNode;
        set
        {
            _startNode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StartNodeDisplay));
            OnPropertyChanged(nameof(HasAnySelection));
            ((Command)BuildRouteCommand).ChangeCanExecute();
            ((Command)BuildEmergencyRouteCommand).ChangeCanExecute();
        }
    }

    /// <summary>Destination node.</summary>
    public NavNode? EndNode
    {
        get => _endNode;
        set
        {
            _endNode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EndNodeDisplay));
            OnPropertyChanged(nameof(HasAnySelection));
            ((Command)BuildRouteCommand).ChangeCanExecute();
        }
    }

    /// <summary>Text shown in the Откуда field (node name or placeholder).</summary>
    public string StartNodeDisplay => _startNode?.DisplayName ?? "Откуда...";

    /// <summary>Text shown in the Куда field (node name or placeholder).</summary>
    public string EndNodeDisplay => _endNode?.DisplayName ?? "Куда...";

    /// <summary>True when at least one of start/end nodes is selected — drives Clear button color.</summary>
    public bool HasAnySelection => _startNode != null || _endNode != null;

    /// <summary>Whether the node-picker popup is currently visible.</summary>
    public bool IsPickerOpen
    {
        get => _isPickerOpen;
        set { _isPickerOpen = value; OnPropertyChanged(); }
    }

    /// <summary>Node the user has tapped on the canvas.</summary>
    public NavNode? TappedNode
    {
        get => _tappedNode;
        private set { _tappedNode = value; OnPropertyChanged(); OnPropertyChanged(nameof(TappedNodeName)); OnPropertyChanged(nameof(TappedNodeTypeLabel)); OnPropertyChanged(nameof(TappedNodeInfo)); OnPropertyChanged(nameof(TappedNodeCategory)); }
    }

    /// <summary>Display name of the tapped node for the popup header.</summary>
    public string TappedNodeName => _tappedNode?.DisplayName ?? string.Empty;

    /// <summary>"Аудитория" / "Лифт" / "Лестница" depending on node type, empty for others.</summary>
    public string TappedNodeTypeLabel => _tappedNode switch
    {
        { IsRoom: true }                        => "Аудитория",
        { IsTransition: true, IsElevator: true } => "Лифт",
        { IsTransition: true }                  => "Лестница",
        _                                       => string.Empty
    };

    /// <summary>Category of the tapped node; empty when not set or hidden.</summary>
    public string TappedNodeCategory =>
        (_tappedNode == null || _tappedNode.IsCategoryHidden) ? string.Empty : _tappedNode.Category;

    /// <summary>Meta info line: "Name · N этаж".</summary>
    public string TappedNodeInfo =>
        _tappedNode == null ? string.Empty
        : $"{_tappedNode.Name} · {_tappedNode.FloorNumber} этаж";

    /// <summary>Whether the node-tap popup is visible.</summary>
    public bool IsNodePopupOpen
    {
        get => _isNodePopupOpen;
        set { _isNodePopupOpen = value; OnPropertyChanged(); }
    }

    /// <summary>Title shown in the picker popup header.</summary>
    public string PickerTitle => _pickerTarget == "start"
        ? "📍 Выберите начальную точку"
        : "🏁 Выберите пункт назначения";

    /// <summary>All building nodes grouped by floor for the picker popup.</summary>
    public List<FloorNodeGroup> NodesByFloor
    {
        get => _nodesByFloor;
        private set { _nodesByFloor = value; OnPropertyChanged(); }
    }

    /// <summary>Live search text typed in the picker popup.</summary>
    public string PickerSearchText
    {
        get => _pickerSearchText;
        set
        {
            _pickerSearchText = value;
            OnPropertyChanged();
            RebuildPickerList();
        }
    }

    /// <summary>Filtered route nodes for the current floor (for SvgView overlay).</summary>
    public ObservableCollection<NavNode> RouteNodesOnFloor
    {
        get => _routeNodesOnFloor;
        private set { _routeNodesOnFloor = value; OnPropertyChanged(); }
    }

    /// <summary>Graph nodes visible on the current floor (for SvgView overlay).</summary>
    public ObservableCollection<NavNode> NodesOnCurrentFloor
    {
        get => _nodesOnCurrentFloor;
        private set { _nodesOnCurrentFloor = value; OnPropertyChanged(); }
    }

    /// <summary>Graph edges visible on the current floor (for SvgView overlay).</summary>
    public ObservableCollection<NavEdge> EdgesOnCurrentFloor
    {
        get => _edgesOnCurrentFloor;
        private set { _edgesOnCurrentFloor = value; OnPropertyChanged(); }
    }

    /// <summary>Human-readable route status message.</summary>
    public string RouteStatus
    {
        get => _routeStatus;
        private set { _routeStatus = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasRoute)); }
    }

    /// <summary>True when a route has been successfully calculated.</summary>
    public bool HasRoute => _routeStepsList.Count > 0;

    /// <summary>Ordered list of floors that are part of the current route (for floor-jump buttons).</summary>
    public ObservableCollection<Floor> RouteFloors { get; } = new();

    /// <summary>True when the current route spans more than one floor.</summary>
    public bool IsMultiFloorRoute => RouteFloors.Count > 1;

    public RouteStep? CurrentStep =>
        _routeStepsList.Count > 0 ? _routeStepsList[_currentStepIndex] : null;

    /// <summary>Текст текущего шага (плоское свойство для XAML-биндинга).</summary>
    public string CurrentStepText => CurrentStep?.Text ?? string.Empty;
    public string CurrentStepIcon => CurrentStep?.Icon ?? "🚶";

    /// <summary>"Шаг N из M" — пусто для одношагового маршрута.</summary>
    public string StepCounterText =>
        _routeStepsList.Count > 1
            ? $"Шаг {_currentStepIndex + 1} из {_routeStepsList.Count}"
            : string.Empty;

    public bool HasNextStep     => _routeStepsList.Count > 0 && _currentStepIndex < _routeStepsList.Count - 1;
    public bool HasPreviousStep  => _routeStepsList.Count > 0 && _currentStepIndex > 0;
    public bool IsLastStep       => _routeStepsList.Count > 0 && _currentStepIndex == _routeStepsList.Count - 1;
    public bool IsMultiStepRoute => _routeStepsList.Count > 1;

    // ── Commands ──────────────────────────────────────────────────────────────────

    public ICommand BuildRouteCommand       { get; }
    public ICommand ClearRouteCommand       { get; }
    public ICommand GoToAdminCommand        { get; }
    public ICommand GoToFloorCommand        { get; }
    public ICommand NextStepCommand         { get; }
    public ICommand PreviousStepCommand     { get; }
    public ICommand OpenStartPickerCommand  { get; }
    public ICommand OpenEndPickerCommand    { get; }
    public ICommand SelectPickerNodeCommand { get; }
    public ICommand ClosePickerCommand      { get; }
    public ICommand SetTappedAsStartCommand { get; }
    public ICommand SetTappedAsEndCommand   { get; }
    public ICommand CloseNodePopupCommand   { get; }
    public ICommand LogoutCommand           { get; }
    public ICommand ConfirmEmergencyLocationCommand  { get; }
    public ICommand CancelEmergencyConfirmationCommand { get; }
    public ICommand BuildEmergencyRouteCommand { get; }

    // ── Constructor ─────────────────────────────────────────────────────────

    public MainViewModel(NavGraphService graphService, AuthService authService, EmergencyService emergencyService, ScheduleService scheduleService)
    {
        _graphService     = graphService;
        _authService      = authService;
        _emergencyService = emergencyService;
        _scheduleService  = scheduleService;

        // Subscribe to emergency state changes
        _emergencyService.EmergencyChanged += OnEmergencyChanged;
        // Subscribe to auth changes to refresh role-dependent UI
        _authService.UserChanged += OnUserChanged;

        BuildRouteCommand = new Command(ExecuteBuildRoute,
            () => StartNode != null && EndNode != null);
        ClearRouteCommand = new Command(ExecuteClearRoute);
        GoToAdminCommand  = new Command(async () =>
            await Shell.Current.GoToAsync("admin"),
            () => IsAdminUser);
        LogoutCommand = new Command(() =>
        {
            _authService.Logout();
            // Navigate back to login page
            var loginPage = IPlatformApplication.Current?.Services.GetService<IndoorNav.Pages.LoginPage>();
            if (loginPage != null && Application.Current?.Windows.Count > 0)
                Application.Current.Windows[0].Page = loginPage;
        });
        GoToFloorCommand  = new Command<Floor>(f => { if (f != null) SelectedFloor = f; });
        NextStepCommand     = new Command(ExecuteNextStep, () => HasNextStep);
        PreviousStepCommand = new Command(ExecutePreviousStep, () => HasPreviousStep);

        OpenStartPickerCommand  = new Command(() =>
        {
            _pickerTarget = "start";
            OnPropertyChanged(nameof(PickerTitle));
            _pickerSearchText = string.Empty;
            OnPropertyChanged(nameof(PickerSearchText));
            RebuildPickerList();
            IsPickerOpen = true;
        });
        OpenEndPickerCommand    = new Command(() =>
        {
            _pickerTarget = "end";
            OnPropertyChanged(nameof(PickerTitle));
            _pickerSearchText = string.Empty;
            OnPropertyChanged(nameof(PickerSearchText));
            RebuildPickerList();
            IsPickerOpen = true;
        });
        SelectPickerNodeCommand = new Command<NavNode>(n =>
        {
            if (n == null) return;
            if (_pickerTarget == "start") StartNode = n;
            else                          EndNode   = n;
            IsPickerOpen = false;
        });
        ClosePickerCommand = new Command(() => IsPickerOpen = false);

        SetTappedAsStartCommand = new Command(() =>
        {
            if (_tappedNode == null) return;
            StartNode = _tappedNode;
            IsNodePopupOpen = false;
        });
        SetTappedAsEndCommand = new Command(() =>
        {
            if (_tappedNode == null) return;
            EndNode = _tappedNode;
            IsNodePopupOpen = false;
        });
        CloseNodePopupCommand = new Command(() => IsNodePopupOpen = false);

        ConfirmEmergencyLocationCommand = new Command(() =>
        {
            ShowEmergencyConfirmation = false;
            // StartNode already set from schedule detection — route to nearest exit
            ExecuteBuildEmergencyRoute();
        });

        CancelEmergencyConfirmationCommand = new Command(() =>
        {
            ShowEmergencyConfirmation = false;
            // Clear auto-set start node so user picks manually
            StartNode = null;
        });

        BuildEmergencyRouteCommand = new Command(() => ExecuteBuildEmergencyRoute(),
            () => StartNode != null);

        _ = InitializeAsync();
    }

    // ── Private methods ─────────────────────────────────────────────────────

    private async Task InitializeAsync()
    {
        IsLoading = true;
        LoadError = string.Empty;
        try
        {
            await _graphService.LoadAsync();
            await _scheduleService.LoadAsync();

            var tasks = BuildingConfig.Select(cfg => Task.Run(() => DiscoverBuildingAsync(cfg.Id, cfg.Name)));
            var buildings = await Task.WhenAll(tasks);

            foreach (var b in buildings)
                Buildings.Add(b);

            if (Buildings.Count == 0)
            {
                var diags = buildings
                    .Where(b => b.LoadDiagnostic != null)
                    .Select(b => b.LoadDiagnostic!);
                LoadError = "Здания не найдены.\n" + string.Join("\n", diags);
                return;
            }

            SelectedBuilding = Buildings.FirstOrDefault();
        }
        catch (Exception ex)
        {
            LoadError = $"Ошибка загрузки: {ex.GetType().Name}\n{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
    private void RefreshNodesForBuilding()
    {
        // Заменяем коллекцию целиком — один PropertyChanged вместо N штук CollectionChanged.
        if (_selectedBuilding == null)
        {
            AllNodesForBuilding = new ObservableCollection<NavNode>();
            NodesByFloor = new List<FloorNodeGroup>();
            return;
        }
        AllNodesForBuilding = new ObservableCollection<NavNode>(
            _graphService.Graph.Nodes
                .Where(n => n.BuildingId == _selectedBuilding.Id && !n.IsWaypoint));

        RebuildPickerList();
    }

    /// <summary>
    /// Rebuilds <see cref="NodesByFloor"/> applying current search text.
    /// Groups are initially collapsed; when a query is active all are expanded and empty floors hidden.
    /// </summary>
    private void RebuildPickerList()
    {
        if (_selectedBuilding == null)
        {
            NodesByFloor = new List<FloorNodeGroup>();
            return;
        }

        bool searching = !string.IsNullOrWhiteSpace(_pickerSearchText);

        NodesByFloor = _selectedBuilding.Floors
            .Select(f =>
            {
                var nodes = _graphService.Graph.Nodes
                    .Where(n => n.BuildingId == _selectedBuilding.Id
                                && n.FloorNumber == f.Number
                                && !n.IsWaypoint)
                    .OrderBy(n => n.Name);

                var filtered = searching
                    ? nodes.Where(n => NodeMatchesSearch(n, _pickerSearchText))
                    : nodes;

                // When searching: expand all non-empty groups; otherwise start collapsed.
                return new FloorNodeGroup(f.Name, filtered, isExpanded: searching);
            })
            .Where(g => !searching || g.AllNodes.Count > 0)
            .ToList();
    }

    /// <summary>
    /// Smart node search: returns true when the node name matches the query.
    /// Supports substring, multi-word, abbreviation (initials) and numeric room matching.
    /// </summary>
    private static bool NodeMatchesSearch(NavNode node, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;

        var q    = query.Trim().ToLowerInvariant();

        // Проверяем и основное имя, и доп теги
        bool MatchText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var t = text.ToLowerInvariant();

            // 1) Direct substring
            if (t.Contains(q)) return true;

            // 2) All query words appear somewhere
            var qWords = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (qWords.Length > 1 && qWords.All(w => t.Contains(w))) return true;

            // 3) Abbreviation: each query char matches first char of a word
            var tWords = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tWords.Length >= q.Length)
            {
                var initials = string.Concat(tWords.Select(w => w[0]));
                if (initials.Contains(q)) return true;
            }

            // 4) Translit / partial without spaces
            if (t.Replace(" ", "").Contains(q.Replace(" ", ""))) return true;

            return false;
        }

        if (MatchText(node.Name))       return true;
        if (MatchText(node.SearchTags)) return true;
        return false;
    }

    private void RefreshFloorOverlay()
    {
        if (_selectedBuilding == null || _selectedFloor == null)
        {
            NodesOnCurrentFloor = new ObservableCollection<NavNode>();
            EdgesOnCurrentFloor = new ObservableCollection<NavEdge>();
            return;
        }

        // Пользовательский режим: показываем только значимые узлы (не waypoint)
        NodesOnCurrentFloor = new ObservableCollection<NavNode>(
            _graphService.Graph
                .GetNodesForFloor(_selectedBuilding.Id, _selectedFloor.Number)
                .Where(n => !n.IsWaypoint));

        // Пользовательский режим: линии (рёбра) не показываем — только точки аудиторий
        EdgesOnCurrentFloor = new ObservableCollection<NavEdge>();
    }

    private void RefreshRoute()
    {
        if (_selectedFloor == null || _currentFullRoute == null)
        {
            RouteNodesOnFloor = new ObservableCollection<NavNode>();
            return;
        }
        RouteNodesOnFloor = new ObservableCollection<NavNode>(
            _currentFullRoute.Where(n =>
                n.BuildingId == _selectedBuilding?.Id && n.FloorNumber == _selectedFloor.Number));
    }

    private List<NavNode>? _currentFullRoute;

    private void ExecuteBuildRoute()
    {
        if (StartNode == null || EndNode == null) return;

        var path = _graphService.Graph.FindPath(StartNode.Id, EndNode.Id,
            BuildExcludeSet(StartNode, EndNode));
        _routeStepsList.Clear();
        _currentStepIndex = 0;

        if (path is null || path.Count == 0)
        {
            _currentFullRoute = null;
            RouteStatus = "Маршрут не найден.";
            OnPropertyChanged(nameof(HasRoute));
        }
        else
        {
            _currentFullRoute = path;
            BuildRouteSteps(path);
        }

        RefreshRoute();
        ((Command)BuildRouteCommand).ChangeCanExecute();

        // Заполняем перечень этажей маршрута для кнопок быстрого перехода
        PopulateRouteFloors(_currentFullRoute);
        OnPropertyChanged(nameof(IsMultiFloorRoute));

        // Автопереключение на начальный этаж маршрута
        if (_currentFullRoute != null && StartNode != null)
        {
            var startFloor = _selectedBuilding?.Floors
                .FirstOrDefault(f => f.Number == StartNode.FloorNumber);
            if (startFloor != null && startFloor != _selectedFloor)
                SelectedFloor = startFloor;
        }
    }

    private void BuildRouteSteps(List<NavNode> path)
    {
        // Группируем последовательные узлы одного этажа в сегменты
        var segments = new List<(int FloorNum, List<NavNode> Nodes)>();
        int cur = path[0].FloorNumber;
        var seg = new List<NavNode> { path[0] };
        for (int i = 1; i < path.Count; i++)
        {
            if (path[i].FloorNumber == cur) { seg.Add(path[i]); }
            else { segments.Add((cur, seg)); cur = path[i].FloorNumber; seg = new List<NavNode> { path[i] }; }
        }
        segments.Add((cur, seg));

        int distinctFloors = segments.Count;
        RouteStatus = distinctFloors > 1
            ? $"Маршрут найден ({distinctFloors} этажа)"
            : $"Маршрут найден ({path.Count(n => !n.IsWaypoint)} точки)";

        Floor? GetFloor(int num) =>
            _selectedBuilding?.Floors.FirstOrDefault(f => f.Number == num);

        var destination = "места назначения";

        if (segments.Count == 1)
        {
            var singleNodes = segments[0].Nodes;
            float sMinX = singleNodes.Min(n => n.X);
            float sMinY = singleNodes.Min(n => n.Y);
            float sMaxX = singleNodes.Max(n => n.X);
            float sMaxY = singleNodes.Max(n => n.Y);

            _routeStepsList.Add(new RouteStep
            {
                Text = $"Идите по {FloorNameInstrumental(segments[0].FloorNum)} до {destination}",
                Icon = "🚶",
                TargetFloor = GetFloor(segments[0].FloorNum),
                FocusRect   = (sMinX, sMinY, sMaxX, sMaxY)
            });
        }
        else
        {
            // Первый сегмент: идти до первого перехода
            var (firstFloorNum, firstNodes) = segments[0];
            var firstTransition = firstNodes.LastOrDefault(n => n.IsTransition) ?? firstNodes.Last();
            bool isElevator = firstTransition.IsElevator
                           || (!firstTransition.IsTransition && firstTransition.Name.Contains("лифт", StringComparison.OrdinalIgnoreCase));

            string transitKindAcc = isElevator ? "лифта" : "лестницы"; // до лифта / до лестницы
            string transitKindVia = isElevator ? "на лифте" : "по лестнице";

            // Bounding box всех узлов первого сегмента (включая WP) — для зума шага 1
            float seg1MinX = firstNodes.Min(n => n.X);
            float seg1MinY = firstNodes.Min(n => n.Y);
            float seg1MaxX = firstNodes.Max(n => n.X);
            float seg1MaxY = firstNodes.Max(n => n.Y);

            _routeStepsList.Add(new RouteStep
            {
                Text = $"Идите по {FloorNameInstrumental(firstFloorNum)} до {transitKindAcc}",
                Icon = "🚶",
                TargetFloor = GetFloor(firstFloorNum),
                FocusRect   = (seg1MinX, seg1MinY, seg1MaxX, seg1MaxY)
            });

            // Один шаг перехода: зумировать на узел перехода (остаёмся на том же этаже)
            var lastFloorNum = segments[^1].FloorNum;
            string dir = lastFloorNum > firstFloorNum ? "Поднимайтесь" : "Спускайтесь";
            _routeStepsList.Add(new RouteStep
            {
                Text = $"{dir} {transitKindVia} до {FloorNameAccusative(lastFloorNum)}",
                Icon = isElevator ? "🛛" : "🪜",
                TargetFloor = GetFloor(firstFloorNum),  // Остаёмся на текущем этаже
                FocusNode   = firstTransition           // Приближаемся к лестнице/лифту
            });

            // Bounding box всех узлов последнего сегмента (включая WP) — для зума шага 3
            var lastNodes = segments[^1].Nodes;
            float segLMinX = lastNodes.Min(n => n.X);
            float segLMinY = lastNodes.Min(n => n.Y);
            float segLMaxX = lastNodes.Max(n => n.X);
            float segLMaxY = lastNodes.Max(n => n.Y);

            // Последний шаг: идти до пункта назначения
            _routeStepsList.Add(new RouteStep
            {
                Text = $"Идите по {FloorNameInstrumental(lastFloorNum)} до {destination}",
                Icon = "🚶",
                TargetFloor = GetFloor(lastFloorNum),
                FocusRect   = (segLMinX, segLMinY, segLMaxX, segLMaxY)
            });
        }

        // Уведомляем об изменении всех свойств шагов
        OnPropertyChanged(nameof(HasRoute));
        OnPropertyChanged(nameof(CurrentStep));
        OnPropertyChanged(nameof(CurrentStepText));
        OnPropertyChanged(nameof(CurrentStepIcon));
        OnPropertyChanged(nameof(StepCounterText));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(HasNextStep));
        OnPropertyChanged(nameof(HasPreviousStep));
        OnPropertyChanged(nameof(IsMultiStepRoute));
        ((Command)NextStepCommand).ChangeCanExecute();
        ((Command)PreviousStepCommand).ChangeCanExecute();

        // Переключаем на этаж первого шага
        var firstFloor = CurrentStep?.TargetFloor;
        if (firstFloor != null && firstFloor != _selectedFloor)
            SelectedFloor = firstFloor;
    }

    // Исключаем узлы-выходы, если они не являются стартом или финишем.
    private ISet<string> BuildExcludeSet(NavNode start, NavNode end)
    {
        var exclude = new HashSet<string>(
            _graphService.Graph.Nodes
                .Where(n => n.IsExit && n.Id != start.Id && n.Id != end.Id)
                .Select(n => n.Id));
        return exclude;
    }

    private static string FloorNameInstrumental(int n) => $"{n} этажу";

    private static string FloorNameAccusative(int n) => $"{n} этажа";

    private void ExecuteClearRoute()
    {
        _currentFullRoute = null;
        RouteNodesOnFloor.Clear();
        _routeStepsList.Clear();
        _currentStepIndex = 0;
        RouteFloors.Clear();
        RouteStatus = string.Empty;
        StartNode = null;
        EndNode = null;
        OnPropertyChanged(nameof(HasRoute));
        OnPropertyChanged(nameof(IsMultiFloorRoute));
        OnPropertyChanged(nameof(CurrentStep));
        OnPropertyChanged(nameof(CurrentStepText));
        OnPropertyChanged(nameof(CurrentStepIcon));
        OnPropertyChanged(nameof(StepCounterText));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(HasNextStep));
        OnPropertyChanged(nameof(HasPreviousStep));
        OnPropertyChanged(nameof(IsMultiStepRoute));
        ((Command)NextStepCommand).ChangeCanExecute();
        ((Command)PreviousStepCommand).ChangeCanExecute();
    }

    private void ExecuteNextStep()
    {
        if (!HasNextStep) return;
        _currentStepIndex++;
        OnPropertyChanged(nameof(CurrentStep));
        OnPropertyChanged(nameof(CurrentStepText));
        OnPropertyChanged(nameof(CurrentStepIcon));
        OnPropertyChanged(nameof(StepCounterText));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(HasNextStep));
        OnPropertyChanged(nameof(HasPreviousStep));
        ((Command)NextStepCommand).ChangeCanExecute();
        ((Command)PreviousStepCommand).ChangeCanExecute();
        // Автопереключение на этаж текущего шага
        var floor = CurrentStep?.TargetFloor;
        if (floor != null) SelectedFloor = floor;
    }

    private void ExecutePreviousStep()
    {
        if (!HasPreviousStep) return;
        _currentStepIndex--;
        OnPropertyChanged(nameof(CurrentStep));
        OnPropertyChanged(nameof(CurrentStepText));
        OnPropertyChanged(nameof(CurrentStepIcon));
        OnPropertyChanged(nameof(StepCounterText));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(HasNextStep));
        OnPropertyChanged(nameof(HasPreviousStep));
        ((Command)NextStepCommand).ChangeCanExecute();
        ((Command)PreviousStepCommand).ChangeCanExecute();
        // Автопереключение на этаж текущего шага
        var floor = CurrentStep?.TargetFloor;
        if (floor != null) SelectedFloor = floor;
    }

    private void PopulateRouteFloors(List<NavNode>? path)
    {
        RouteFloors.Clear();
        if (path == null || _selectedBuilding == null) return;
        var seen = new HashSet<int>();
        foreach (var node in path)
            if (seen.Add(node.FloorNumber))
            {
                var floor = _selectedBuilding.Floors.FirstOrDefault(f => f.Number == node.FloorNumber);
                if (floor != null) RouteFloors.Add(floor);
            }
    }

    /// <summary>Call when a node on the canvas is tapped in user mode.</summary>
    public void OnCanvasNodeTapped(NavNode node)
    {
        TappedNode = node;
        IsNodePopupOpen = true;
    }

    private void OnUserChanged(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            OnPropertyChanged(nameof(IsAdminUser));
            OnPropertyChanged(nameof(CurrentUserName));
            ((Command)GoToAdminCommand).ChangeCanExecute();

            // If emergency is already active when a student logs in, trigger auto-detection
            if (_isEmergencyActive && _authService.CurrentRole == UserRole.Student)
            {
                var groupId = _authService.CurrentUser?.GroupId;
                if (!string.IsNullOrEmpty(groupId))
                {
                    var entry = _scheduleService.GetCurrentEntryForGroup(groupId);
                    if (entry != null)
                    {
                        var autoRoomNode = _graphService.Graph.GetNode(entry.RoomNodeId);
                        if (autoRoomNode != null)
                        {
                            StartNode = autoRoomNode;
                            EmergencyAutoLocationName = autoRoomNode.DisplayName;
                            ShowEmergencyConfirmation = true;
                        }
                    }
                }
            }
        });
    }

    private void OnEmergencyChanged(object? sender, bool isActive)
    {
        IsEmergencyActive = isActive;
        OnPropertyChanged(nameof(IsAdminUser));
        ((Command)GoToAdminCommand).ChangeCanExecute();
        ((Command)BuildEmergencyRouteCommand).ChangeCanExecute();

        if (!isActive)
        {
            ShowEmergencyConfirmation = false;
            return;
        }

        // Emergency became active — try to auto-detect student's room from schedule
        MainThread.BeginInvokeOnMainThread(() =>
        {
            NavNode? autoRoomNode = null;

            if (_authService.CurrentRole == UserRole.Student)
            {
                var groupId = _authService.CurrentUser?.GroupId;
                if (!string.IsNullOrEmpty(groupId))
                {
                    var entry = _scheduleService.GetCurrentEntryForGroup(groupId);
                    if (entry != null)
                    {
                        autoRoomNode = _graphService.Graph.GetNode(entry.RoomNodeId);
                    }
                }
            }

            if (autoRoomNode != null)
            {
                // Pre-fill start node and show confirmation card
                StartNode = autoRoomNode;
                EmergencyAutoLocationName = autoRoomNode.DisplayName;
                ShowEmergencyConfirmation = true;
            }
            else
            {
                // No schedule entry — user picks manually, no confirmation card
                ShowEmergencyConfirmation = false;
            }
        });
    }

    private void ExecuteBuildEmergencyRoute()
    {
        if (StartNode == null) return;

        // Switch to the building that contains the start node BEFORE building
        // the route, so that BuildRouteSteps can find the correct floors.
        if (!string.IsNullOrEmpty(StartNode.BuildingId) &&
            _selectedBuilding?.Id != StartNode.BuildingId)
        {
            var targetBuilding = Buildings.FirstOrDefault(b => b.Id == StartNode.BuildingId);
            if (targetBuilding != null)
                SelectedBuilding = targetBuilding;
        }

        var path = _emergencyService.FindNearestExitRoute(StartNode, _graphService.Graph);
        if (path.Count > 1)
        {
            EndNode = path.Last();
            ExecuteBuildRoute();
        }
        else
        {
            RouteStatus = "Выход не найден. Обратитесь к сотрудникам.";
        }
    }


    private static async Task<Building> DiscoverBuildingAsync(string id, string name)
    {
        var building = new Building(id, name);

        // Проверяем существование этажа по WebP-файлу в FloorImages/
        // (SVG-источники необязательны — могут отсутствовать после клонирования репозитория)
        static async Task<bool> FloorExistsAsync(string id, string svgRelativePath)
        {
            // Сначала ищем предгенерированный WebP (всегда есть в репозитории)
            var cacheKey    = $"{id}_{svgRelativePath}".Replace('/', '_');
            var webpBundle  = $"FloorImages/{cacheKey}.webp";
            try
            {
                using var s = await FileSystem.OpenAppPackageFileAsync(webpBundle).ConfigureAwait(false);
                return true;
            }
            catch { /* нет WebP — пробуем SVG */ }

            // Запасной вариант: SVG (для разработчика с полными исходниками)
            try
            {
                using var s = await FileSystem.OpenAppPackageFileAsync($"SvgFloors/{id}/{svgRelativePath}").ConfigureAwait(false);
                return true;
            }
            catch { return false; }
        }

        // Подвал
        if (await FloorExistsAsync(id, "floor-1.svg").ConfigureAwait(false))
            building.Floors.Add(new Floor(-1, $"{id}/floor-1.svg"));

        // floor1, floor2, …
        for (int i = 1; i <= 50; i++)
        {
            if (await FloorExistsAsync(id, $"floor{i}.svg").ConfigureAwait(false))
                building.Floors.Add(new Floor(i, $"{id}/floor{i}.svg"));
            else
            {
                if (i == 1)
                    building.LoadDiagnostic = $"[{id}] floor1 не найден (ни WebP, ни SVG)";
                break;
            }
        }

        return building;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
