using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using IndoorNav.Models;
using IndoorNav.Services;
using Microsoft.Maui.Storage;

namespace IndoorNav.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly NavGraphService _graphService;
    private readonly AuthService _authService;
    private readonly EmergencyService _emergencyService;
    private readonly ScheduleService _scheduleService;
    private readonly DepartmentService _departmentService;

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

    // --- QR anchor visibility ---
    // IDs of QR-anchor nodes that should currently be visible on the user map
    private readonly HashSet<string> _qrVisibleSet = new();
    private ObservableCollection<string> _qrAnchorNodeIds = new();
    public IEnumerable<string> QrAnchorNodeIds => _qrAnchorNodeIds;

    // --- ЧС: blocked-node rerouting ---
    private readonly HashSet<string> _blockedNodeIds = new();
    private bool _isBlockingMode;
    private NavNode? _pendingBlockNode;

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
            if (value == null) NodesByFloor = new List<FloorNodeGroup>();
            // Update emergency state for the newly selected building
            IsEmergencyActive = _emergencyService != null &&
                                 _emergencyService.IsActiveForBuilding(value?.Id);
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
    public bool IsAdminUser   => _authService?.CurrentRole == UserRole.Admin;
    public bool IsStudentUser  => _authService?.CurrentRole == UserRole.Student;
    public string CurrentUserName => _authService?.CurrentUser?.DisplayName ?? "Гость";

    private bool _isEmergencyActive;
    public bool IsEmergencyActive
    {
        get => _isEmergencyActive;
        private set { _isEmergencyActive = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowFireExtinguishers)); OnPropertyChanged(nameof(EmergencyMessage)); OnPropertyChanged(nameof(IsNormalMode)); OnPropertyChanged(nameof(ShowSearchPanel)); OnPropertyChanged(nameof(HasEmergencyRoute)); OnPropertyChanged(nameof(IsEmergencyBannerVisible)); }
    }
    public string EmergencyMessage => _emergencyService?.EmergencyMessage ?? string.Empty;
    /// <summary>True when the emergency banner should be shown (ЧС active but not in blocking-mode where hint replaces it).</summary>
    public bool IsEmergencyBannerVisible => _isEmergencyActive && !_isBlockingMode;
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

    /// <summary>Node where the user is currently located.</summary>
    public NavNode? StartNode
    {
        get => _startNode;
        set
        {
            // Track QR anchor visibility: show when set as start, hide when cleared
            if (_startNode?.IsQrAnchor == true && _startNode != value)
            {
                _qrVisibleSet.Remove(_startNode.Id);
                _qrAnchorNodeIds.Remove(_startNode.Id);
            }
            _startNode = value;
            if (_startNode?.IsQrAnchor == true && _qrVisibleSet.Add(_startNode.Id))
                _qrAnchorNodeIds.Add(_startNode.Id);
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
    public string StartNodeDisplay =>
        _startNode == null ? "Откуда..." :
        _startNode.IsQrAnchor ? "QR-код" : _startNode.DisplayName;

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
        { IsEvacuationExit: true }              => "🚪 Эвак. выход",
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
        private set { _routeStatus = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasRoute)); OnPropertyChanged(nameof(ShowSearchPanel)); }
    }

    /// <summary>True when a route has been successfully calculated.</summary>
    public bool HasRoute => _routeStepsList.Count > 0;

    /// <summary>True in emergency mode after a route has been built — shows the "⛔ Маршрут недоступен" button.</summary>
    public bool HasEmergencyRoute => HasRoute && IsEmergencyActive;

    /// <summary>True while the user is picking a node to mark as blocked.</summary>
    public bool IsBlockingMode
    {
        get => _isBlockingMode;
        private set { _isBlockingMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsEmergencyBannerVisible)); RefreshFloorOverlay(); }
    }

    /// <summary>IDs of nodes the user has marked as impassable.</summary>
    public IEnumerable<string> BlockedNodeIds => _blockedNodeIds;

    /// <summary>Node selected during blocking mode, waiting for confirmation.</summary>
    public NavNode? PendingBlockNode
    {
        get => _pendingBlockNode;
        private set
        {
            _pendingBlockNode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PendingBlockNodeName));
            OnPropertyChanged(nameof(IsBlockPending));
        }
    }
    public string PendingBlockNodeName => _pendingBlockNode?.DisplayName ?? string.Empty;
    public bool   IsBlockPending       => _pendingBlockNode != null;

    private bool _showNoRoutePopup;
    public bool ShowNoRoutePopup
    {
        get => _showNoRoutePopup;
        set { _showNoRoutePopup = value; OnPropertyChanged(); }
    }

    /// <summary>Скрыть панель поиска когда маршрут построен (единый дизайн для всех режимов).</summary>
    public bool ShowSearchPanel => !HasRoute;

    /// <summary>Список точек-индикаторов шагов для отображения в маршрутной карточке.</summary>
    public IReadOnlyList<RouteDotVm> RouteStepDots =>
        Enumerable.Range(0, _routeStepsList.Count)
                  .Select(i => new RouteDotVm(i == _currentStepIndex))
                  .ToList();

    public RouteStep? CurrentStep =>
        _routeStepsList.Count > 0 ? _routeStepsList[_currentStepIndex] : null;

    /// <summary>Текст текущего шага (плоское свойство для XAML-биндинга).</summary>
    public string CurrentStepText => CurrentStep?.Text ?? string.Empty;
    public string CurrentStepIcon => CurrentStep?.Icon ?? "🚶";
    public string CurrentStepFloorLabel =>
        CurrentStep?.FloorLabel ?? (CurrentStep?.TargetFloor != null ? $"{CurrentStep.TargetFloor.Number} Этаж" : string.Empty);

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
    public ICommand ChangePasswordCommand   { get; }
    public ICommand ConfirmEmergencyLocationCommand  { get; }
    public ICommand CancelEmergencyConfirmationCommand { get; }
    public ICommand BuildEmergencyRouteCommand { get; }
    public ICommand ChangeGroupCommand          { get; }
    public ICommand ScanQrCommand               { get; }
    public ICommand MarkRouteBlockedCommand     { get; }
    public ICommand CancelBlockingModeCommand   { get; }
    public ICommand ConfirmBlockNodeCommand     { get; }
    public ICommand CancelBlockNodeConfirmCommand { get; }
    public ICommand DismissNoRoutePopupCommand  { get; }

    // ── Constructor ─────────────────────────────────────────────────────────

    public MainViewModel(NavGraphService graphService, AuthService authService, EmergencyService emergencyService, ScheduleService scheduleService, DepartmentService departmentService)
    {
        _graphService       = graphService;
        _authService        = authService;
        _emergencyService   = emergencyService;
        _scheduleService    = scheduleService;
        _departmentService  = departmentService;

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
        ChangePasswordCommand = new Command(async () =>
        {
            var user = _authService.CurrentUser;
            if (user == null) return;
            var page = Application.Current!.Windows[0].Page!;
            var oldPwd = await page.DisplayPromptAsync(
                "🔑 Изменение пароля", "Текущий пароль:");
            if (string.IsNullOrWhiteSpace(oldPwd)) return;
            var verified = await _authService.LoginAsync(user.Username, oldPwd);
            if (verified == null)
            {
                await page.DisplayAlert("Ошибка", "Неверный текущий пароль.", "ОК"); return;
            }
            var newPwd = await page.DisplayPromptAsync(
                "🔑 Новый пароль", "Введите новый пароль:");
            if (string.IsNullOrWhiteSpace(newPwd)) return;
            var confirmPwd = await page.DisplayPromptAsync(
                "🔑 Подтверждение", "Повторите новый пароль:");
            if (newPwd != confirmPwd)
            {
                await page.DisplayAlert("Ошибка", "Пароли не совпадают.", "ОК"); return;
            }
            await _authService.ChangePasswordAsync(user.Id, newPwd);
            await page.DisplayAlert("Готово", "Пароль успешно изменён.", "ОК");
        });
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
            // В режиме ЧС выходы нельзя выбрать как точку отправки
            if (_pickerTarget == "start" && _isEmergencyActive && (n.IsExit || n.IsEvacuationExit)) return;
            if (_pickerTarget == "start") StartNode = n;
            else                          EndNode   = n;
            IsPickerOpen = false;
        });
        ClosePickerCommand = new Command(() => IsPickerOpen = false);

        SetTappedAsStartCommand = new Command(() =>
        {
            if (_tappedNode == null) return;
            // В режиме ЧС выходы нельзя выбрать как точку отправки
            if (_isEmergencyActive && (_tappedNode.IsExit || _tappedNode.IsEvacuationExit)) return;
            StartNode = _tappedNode;
            IsNodePopupOpen = false;
            if (_isEmergencyActive)
                ExecuteBuildEmergencyRoute();
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

        ChangeGroupCommand = new Command(async () =>
        {
            var user = _authService.CurrentUser;
            if (user == null) return;

            // Collect all groups from all departments
            var allGroups = _departmentService.Departments
                .SelectMany(d => d.Groups)
                .OrderBy(g => g.Name)
                .ToList();

            var page = Application.Current!.Windows[0].Page!;
            if (allGroups.Count == 0)
            {
                await page.DisplayAlert(
                    "Группы", "Групп пока нет. Обратитесь к администратору.", "ОК");
                return;
            }

            // Find current group name for subtitle
            var currentGroup = allGroups.FirstOrDefault(g => g.Id == user.GroupId);
            var subtitle = currentGroup != null
                ? $"Текущая группа: {currentGroup.Name}"
                : "Группа не выбрана";

            var groupNames = allGroups.Select(g => g.Name).ToArray();
            var selected = await page.DisplayActionSheet(
                $"Сменить группу\n{subtitle}", "Отмена", null, groupNames);

            if (string.IsNullOrEmpty(selected) || selected == "Отмена") return;

            var chosenGroup = allGroups.FirstOrDefault(g => g.Name == selected);
            if (chosenGroup == null) return;

            user.GroupId = chosenGroup.Id;
            await _authService.UpdateUserAsync();
        });

        ScanQrCommand = new Command(async () =>
        {
#if ANDROID || IOS
            // On mobile: open the camera QR scanner modal page
            var page = IPlatformApplication.Current?.Services.GetService<IndoorNav.Pages.QrScanPage>();
            if (page != null)
                await Shell.Current.Navigation.PushModalAsync(page);
#else
            // On Windows: manual text entry fallback
            var content = await Shell.Current.DisplayPromptAsync(
                "QR-код",
                "Введите или вставьте содержимое QR-метки:",
                placeholder: "indoornav://node/...");
            if (string.IsNullOrWhiteSpace(content)) return;

            var nodeId = DeepLinkService.ParseUri(content.Trim());
            if (nodeId == null)
            {
                await Shell.Current.DisplayAlert("QR", "Нераспознанный формат QR-кода.", "ОК");
                return;
            }
            HandleDeepLinkNode(nodeId);
#endif
        });

        MarkRouteBlockedCommand = new Command(() =>
        {
            PendingBlockNode = null;
            IsBlockingMode = true;
        }, () => HasEmergencyRoute);

        CancelBlockingModeCommand = new Command(() =>
        {
            PendingBlockNode = null;
            IsBlockingMode = false;
        });

        ConfirmBlockNodeCommand = new Command(() =>
        {
            if (_pendingBlockNode == null) return;

            // Find the node immediately before the blocked one on the current route.
            // That becomes the new start — the user can reach it without crossing
            // the impassable segment.
            NavNode? newStart = null;
            if (_currentFullRoute != null)
            {
                int idx = _currentFullRoute.FindIndex(n => n.Id == _pendingBlockNode.Id);
                if (idx > 0)
                    newStart = _currentFullRoute[idx - 1];
            }

            _blockedNodeIds.Add(_pendingBlockNode.Id);
            PendingBlockNode = null;
            IsBlockingMode = false;
            OnPropertyChanged(nameof(BlockedNodeIds));

            if (newStart != null)
                StartNode = newStart;

            ExecuteBuildEmergencyRoute();
        });

        CancelBlockNodeConfirmCommand = new Command(() =>
        {
            // Return to selection mode so the user can pick a different node
            PendingBlockNode = null;
        });

        DismissNoRoutePopupCommand = new Command(() =>
        {
            ShowNoRoutePopup = false;
            // Clear blocked nodes so the user can start fresh in ЧС mode
            _blockedNodeIds.Clear();
            IsBlockingMode = false;
            PendingBlockNode = null;
            OnPropertyChanged(nameof(BlockedNodeIds));
        });

        // Subscribe to deep-link and in-app scan results
        DeepLinkService.NodeRequested += HandleDeepLinkNode;

        _ = InitializeAsync();
    }

    // ── Deep-link / QR scan handler ─────────────────────────────────────────────

    /// <summary>
    /// Called from <see cref="DeepLinkService"/> whenever a node ID arrives
    /// (OS deep link or in-app camera scan).  Safe to call from any thread.
    /// </summary>
    private void HandleDeepLinkNode(string nodeId)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var node = _graphService.Graph.Nodes.FirstOrDefault(n => n.Id == nodeId);
            if (node == null) return;

            StartNode = node;
            var floor = _selectedBuilding?.Floors.FirstOrDefault(f => f.Number == node.FloorNumber);
            if (floor != null) SelectedFloor = floor;
        });
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
            await _departmentService.LoadAsync();
            await _emergencyService.LoadAsync();

            var tasks = BuildingConfig.Select(cfg => DiscoverBuildingAsync(cfg.Id, cfg.Name, _graphService));
            var buildings = await Task.WhenAll(tasks);
            foreach (var b in buildings)
                Buildings.Add(b);

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
                // В режиме ЧС выходы и запасные выходы нельзя выбрать как точку отправки
                bool excludeExitsForStart = _isEmergencyActive && _pickerTarget == "start";

                var nodes = _graphService.Graph.Nodes
                    .Where(n => n.BuildingId == _selectedBuilding.Id
                                && n.FloorNumber == f.Number
                                && !n.IsWaypoint
                                && !n.IsFireExtinguisher
                                && !n.IsQrAnchor
                                && !(excludeExitsForStart && (n.IsExit || n.IsEvacuationExit)))
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

        var baseNodes = _graphService.Graph
            .GetNodesForFloor(_selectedBuilding.Id, _selectedFloor.Number)
            .Where(n => !n.IsWaypoint);

        // In blocking mode also expose corridor/waypoint nodes that are part of the
        // current route so the user can tap them as the impassable point.
        if (_isBlockingMode && _currentFullRoute != null)
        {
            var routeIds = new HashSet<string>(_currentFullRoute.Select(n => n.Id));
            var routeWaypoints = _graphService.Graph
                .GetNodesForFloor(_selectedBuilding.Id, _selectedFloor.Number)
                .Where(n => n.IsWaypoint && routeIds.Contains(n.Id));
            NodesOnCurrentFloor = new ObservableCollection<NavNode>(baseNodes.Concat(routeWaypoints));
        }
        else
        {
            NodesOnCurrentFloor = new ObservableCollection<NavNode>(baseNodes);
        }

        // Пользовательский режим: линии (рёбра) не показываем — только точки аудиторий
        EdgesOnCurrentFloor = new ObservableCollection<NavEdge>();
    }

    private void RefreshRoute()
    {
        if (_selectedFloor == null || _currentFullRoute == null)
        {
            RouteNodesOnFloor = new ObservableCollection<NavNode>();
            RouteBreaksOnFloor = Array.Empty<int>();
            return;
        }

        var onFloor = _currentFullRoute
            .Select((n, fullIdx) => (node: n, fullIdx))
            .Where(t => t.node.BuildingId == _selectedBuilding?.Id && t.node.FloorNumber == _selectedFloor.Number)
            .ToList();

        RouteNodesOnFloor = new ObservableCollection<NavNode>(onFloor.Select(t => t.node));

        // Compute break indices: index i in the per-floor list is a break if it is not
        // directly adjacent to index i-1 in the full route (route went through another floor).
        var breaks = new List<int>();
        for (int i = 1; i < onFloor.Count; i++)
        {
            if (onFloor[i].fullIdx != onFloor[i - 1].fullIdx + 1)
                breaks.Add(i);
        }
        RouteBreaksOnFloor = breaks;
    }

    private IEnumerable<int> _routeBreaksOnFloor = Array.Empty<int>();
    public IEnumerable<int> RouteBreaksOnFloor
    {
        get => _routeBreaksOnFloor;
        private set { _routeBreaksOnFloor = value; OnPropertyChanged(); }
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
            OnPropertyChanged(nameof(ShowSearchPanel));
        }
        else
        {
            _currentFullRoute = path;
            BuildRouteSteps(path);
        }

        RefreshRoute();
        ((Command)BuildRouteCommand).ChangeCanExecute();

        // Автопереключение на этаж первого шага маршрута.
        // BuildRouteSteps уже выбрал нужный этаж (для sameTransitionGroup — это этаж назначения),
        // поэтому переключаем только если BuildRouteSteps не смог это сделать (TargetFloor был null).
        if (_currentFullRoute != null && CurrentStep?.TargetFloor == null && StartNode != null)
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

        int distinctFloors = segments.Select(s => s.FloorNum).Distinct().Count();
        RouteStatus = distinctFloors > 1
            ? $"Маршрут найден ({segments.Count} участков, {distinctFloors} этажей)"
            : $"Маршрут найден ({path.Count(n => !n.IsWaypoint)} точки)";

        Floor? GetFloor(int num) =>
            _selectedBuilding?.Floors.FirstOrDefault(f => f.Number == num);

        // Проверяем: весь маршрут — одна пара узлов одного перехода (sameTransitionGroup)
        var startNode = path[0];
        var endNode   = path[^1];
        bool sameTransitionGroup = startNode.IsTransition && endNode.IsTransition
            && !string.IsNullOrEmpty(startNode.TransitionGroupId)
            && startNode.TransitionGroupId == endNode.TransitionGroupId;

        if (sameTransitionGroup)
        {
            bool up = endNode.FloorNumber > startNode.FloorNumber;
            bool elev = startNode.IsElevator || startNode.Name.Contains("лифт", StringComparison.OrdinalIgnoreCase);
            _routeStepsList.Add(new RouteStep
            {
                Text        = $"{(up ? "Поднимайтесь" : "Спускайтесь")} {(elev ? "на лифте" : "по лестнице")} до {endNode.FloorNumber} этажа",
                Icon        = elev ? "🛛" : "🪜",
                TargetFloor = GetFloor(endNode.FloorNumber),
                FocusNode   = endNode,
                FloorLabel  = up ? "Подъём" : "Спуск"
            });
        }
        else if (segments.Count == 1)
        {
            // Весь маршрут на одном этаже
            var nodes = segments[0].Nodes;
            _routeStepsList.Add(new RouteStep
            {
                Text        = $"Идите по {FloorNameInstrumental(segments[0].FloorNum)} до места назначения",
                Icon        = "🚶",
                TargetFloor = GetFloor(segments[0].FloorNum),
                FocusRect   = (nodes.Min(n => n.X), nodes.Min(n => n.Y), nodes.Max(n => n.X), nodes.Max(n => n.Y))
            });
        }
        else
        {
            // «Промежуточный» сегмент — состоит ТОЛЬКО из узлов-переходов (лестниц/лифтов)
            // и не содержит коридорных waypoint-ов, т.е. пользователь не идёт никуда на этом этаже.
            // Такие сегменты пропускаем; шаг перехода указывает сразу на конечный «реальный» этаж.
            bool IsPureTrans(List<NavNode> nodes) =>
                nodes.All(n => n.IsTransition);

            var skipSet = new HashSet<int>();
            for (int si = 1; si < segments.Count - 1; si++)
                if (IsPureTrans(segments[si].Nodes)) skipSet.Add(si);

            for (int si = 0; si < segments.Count; si++)
            {
                if (skipSet.Contains(si)) continue;

                var (floorNum, nodes) = segments[si];
                bool isFirst = si == 0;

                // Ближайший следующий не-пропускаемый сегмент
                int nextSi = si + 1;
                while (nextSi < segments.Count && skipSet.Contains(nextSi)) nextSi++;
                bool hasNext = nextSi < segments.Count;

                // Узел перехода в конце текущего сегмента
                NavNode? exitTransition = hasNext
                    ? (nodes.LastOrDefault(n => n.IsTransition) ?? nodes.Last())
                    : null;

                // ── Шаг «идти по этажу» ──────────────────────────────────────
                bool segStartsAtTransition = isFirst && startNode.IsTransition;
                bool segIsJustTransition   = nodes.All(n => n.IsTransition);

                if (!segStartsAtTransition && !segIsJustTransition)
                {
                    string walkTarget = !hasNext
                        ? "места назначения"
                        : (exitTransition is { IsElevator: true } ||
                           exitTransition?.Name.Contains("лифт", StringComparison.OrdinalIgnoreCase) == true
                            ? "лифта" : "лестницы");

                    _routeStepsList.Add(new RouteStep
                    {
                        Text        = $"Идите по {FloorNameInstrumental(floorNum)} до {walkTarget}",
                        Icon        = "🚶",
                        TargetFloor = GetFloor(floorNum),
                        FocusRect   = (nodes.Min(n => n.X), nodes.Min(n => n.Y),
                                       nodes.Max(n => n.X), nodes.Max(n => n.Y))
                    });
                }

                // ── Шаг «переход на целевой этаж» (пропускаем промежуточные) ──
                if (hasNext && exitTransition != null)
                {
                    int targetFloor = segments[nextSi].FloorNum;
                    bool up  = targetFloor > floorNum;
                    bool elev = exitTransition.IsElevator
                             || exitTransition.Name.Contains("лифт", StringComparison.OrdinalIgnoreCase);

                    _routeStepsList.Add(new RouteStep
                    {
                        Text        = $"{(up ? "Поднимайтесь" : "Спускайтесь")} {(elev ? "на лифте" : "по лестнице")} до {targetFloor} этажа",
                        Icon        = elev ? "🛛" : "🪜",
                        TargetFloor = GetFloor(floorNum),
                        FocusNode   = exitTransition,
                        FloorLabel  = up ? "Подъём" : "Спуск"
                    });
                }
            }
        }

        // Уведомляем об изменении всех свойств шагов
        OnPropertyChanged(nameof(HasRoute));
        OnPropertyChanged(nameof(HasEmergencyRoute));
        ((Command)MarkRouteBlockedCommand).ChangeCanExecute();
        OnPropertyChanged(nameof(CurrentStep));
        OnPropertyChanged(nameof(CurrentStepText));
        OnPropertyChanged(nameof(CurrentStepIcon));
        OnPropertyChanged(nameof(StepCounterText));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(HasNextStep));
        OnPropertyChanged(nameof(HasPreviousStep));
        OnPropertyChanged(nameof(IsMultiStepRoute));
        OnPropertyChanged(nameof(RouteStepDots));
        OnPropertyChanged(nameof(ShowSearchPanel));
        OnPropertyChanged(nameof(CurrentStepFloorLabel));
        ((Command)NextStepCommand).ChangeCanExecute();
        ((Command)PreviousStepCommand).ChangeCanExecute();

        // Переключаем на этаж первого шага
        var firstFloor = CurrentStep?.TargetFloor;
        if (firstFloor != null && firstFloor != _selectedFloor)
            SelectedFloor = firstFloor;
    }

    // Исключаем узлы-выходы, если они не являются стартом или финишем,
    // а также все узлы, отмеченные пользователем как недоступные.
    private ISet<string> BuildExcludeSet(NavNode start, NavNode end)
    {
        var exclude = new HashSet<string>(
            _graphService.Graph.Nodes
                .Where(n => (n.IsExit || n.IsEvacuationExit) && n.Id != start.Id && n.Id != end.Id)
                .Select(n => n.Id));
        // Blocked nodes must never appear in the route again
        foreach (var id in _blockedNodeIds)
            exclude.Add(id);
        return exclude;
    }

    private static string FloorNameInstrumental(int n) => $"{n} этажу";

    private void ExecuteClearRoute()
    {
        // Also reset blocking state when the route is cleared
        _blockedNodeIds.Clear();
        IsBlockingMode = false;
        PendingBlockNode = null;
        OnPropertyChanged(nameof(BlockedNodeIds));

        _currentFullRoute = null;
        RouteNodesOnFloor.Clear();
        _routeStepsList.Clear();
        _currentStepIndex = 0;
        RouteStatus = string.Empty;
        // В режиме ЧС сохраняем точку отправки — пользователь уже указал своё местоположение
        if (!_isEmergencyActive)
            StartNode = null;
        EndNode = null;
        OnPropertyChanged(nameof(HasRoute));
        OnPropertyChanged(nameof(HasEmergencyRoute));
        OnPropertyChanged(nameof(CurrentStep));
        OnPropertyChanged(nameof(CurrentStepText));
        OnPropertyChanged(nameof(CurrentStepIcon));
        OnPropertyChanged(nameof(StepCounterText));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(HasNextStep));
        OnPropertyChanged(nameof(HasPreviousStep));
        OnPropertyChanged(nameof(IsMultiStepRoute));
        OnPropertyChanged(nameof(RouteStepDots));
        OnPropertyChanged(nameof(ShowSearchPanel));
        OnPropertyChanged(nameof(CurrentStepFloorLabel));
        ((Command)NextStepCommand).ChangeCanExecute();
        ((Command)PreviousStepCommand).ChangeCanExecute();
        ((Command)MarkRouteBlockedCommand).ChangeCanExecute();
    }

    private void ExecuteNextStep()
    {
        if (!HasNextStep) return;
        _currentStepIndex++;
        OnPropertyChanged(nameof(CurrentStep));
        OnPropertyChanged(nameof(CurrentStepText));
        OnPropertyChanged(nameof(CurrentStepIcon));
        OnPropertyChanged(nameof(CurrentStepFloorLabel));
        OnPropertyChanged(nameof(StepCounterText));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(HasNextStep));
        OnPropertyChanged(nameof(HasPreviousStep));
        OnPropertyChanged(nameof(RouteStepDots));
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
        OnPropertyChanged(nameof(CurrentStepFloorLabel));
        OnPropertyChanged(nameof(StepCounterText));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(HasNextStep));
        OnPropertyChanged(nameof(HasPreviousStep));
        OnPropertyChanged(nameof(RouteStepDots));
        ((Command)NextStepCommand).ChangeCanExecute();
        ((Command)PreviousStepCommand).ChangeCanExecute();
        // Автопереключение на этаж текущего шага
        var floor = CurrentStep?.TargetFloor;
        if (floor != null) SelectedFloor = floor;
    }

    /// <summary>Call when a node on the canvas is tapped in user mode.</summary>
    public void OnCanvasNodeTapped(NavNode node)
    {
        // Blocking-selection mode: выходы (IsExit/IsEvacuationExit) можно выбирать всегда,
        // остальные узлы — только если входят в текущий маршрут ЧС
        if (_isBlockingMode)
        {
            bool isExit = node.IsExit || node.IsEvacuationExit;
            if (!isExit && (_currentFullRoute == null || !_currentFullRoute.Any(n => n.Id == node.Id)))
                return;
            PendingBlockNode = node;   // show confirmation overlay
            return;
        }

        if (node.IsFireExtinguisher) return;
        // В обычном режиме ЧС выходы нельзя выбрать как точку отправки — popup не открываем
        if (_isEmergencyActive && (node.IsExit || node.IsEvacuationExit)) return;
        TappedNode = node;
        IsNodePopupOpen = true;
    }

    private void OnUserChanged(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            OnPropertyChanged(nameof(IsAdminUser));
            OnPropertyChanged(nameof(IsStudentUser));
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

    private void OnEmergencyChanged(object? sender, EmergencyChangedArgs e)
    {
        // Recompute whether THIS building is now in emergency
        bool myBuildingAffected = e.BuildingId == null || e.BuildingId == _selectedBuilding?.Id;
        IsEmergencyActive = _emergencyService.IsActiveForBuilding(_selectedBuilding?.Id);

        OnPropertyChanged(nameof(IsAdminUser));
        ((Command)GoToAdminCommand).ChangeCanExecute();
        ((Command)BuildEmergencyRouteCommand).ChangeCanExecute();
        ((Command)MarkRouteBlockedCommand).ChangeCanExecute();

        if (!e.IsActive)
        {
            // Emergency lifted — clear blocking state, route and start node
            _blockedNodeIds.Clear();
            IsBlockingMode = false;
            PendingBlockNode = null;
            OnPropertyChanged(nameof(BlockedNodeIds));
            if (!_emergencyService.IsEmergencyActive)
            {
                ShowEmergencyConfirmation = false;
                // Сбрасываем маршрут и точку отправки при снятии ЧС
                MainThread.BeginInvokeOnMainThread(ExecuteClearRoute);
            }
            return;
        }

        // If this building is not affected, skip confirmation
        if (!myBuildingAffected) return;

        // Emergency became active for this building — try to auto-detect student's room from schedule
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
                        var candidate = _graphService.Graph.GetNode(entry.RoomNodeId);
                        // Only use this node if it belongs to the affected building
                        if (candidate != null &&
                            (e.BuildingId == null || candidate.BuildingId == e.BuildingId))
                        {
                            autoRoomNode = candidate;
                        }
                    }
                }
            }

            if (autoRoomNode != null)
            {
                StartNode = autoRoomNode;
                EmergencyAutoLocationName = autoRoomNode.DisplayName;
                ShowEmergencyConfirmation = true;
            }
            else
            {
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

        // Pass any user-marked blocked nodes so the router avoids them
        ISet<string>? blocked = _blockedNodeIds.Count > 0 ? _blockedNodeIds : null;
        var path = _emergencyService.FindNearestExitRoute(StartNode, _graphService.Graph, blocked);
        if (path.Count > 1)
        {
            EndNode = path.Last();
            ExecuteBuildRoute();
        }
        else
        {
            RouteStatus = "Маршрут до выхода не найден.";
            // Clear route so the UI returns to search-panel state
            _currentFullRoute = null;
            _routeStepsList.Clear();
            _currentStepIndex = 0;
            RefreshRoute();
            OnPropertyChanged(nameof(HasRoute));
            OnPropertyChanged(nameof(ShowSearchPanel));
            ShowNoRoutePopup = true;
        }

        OnPropertyChanged(nameof(HasEmergencyRoute));
        ((Command)MarkRouteBlockedCommand).ChangeCanExecute();
    }


    /// <summary>Строит здание из данных графа — мгновенно, без обращений к файловой системе.</summary>
    private static async Task<Building> DiscoverBuildingAsync(string id, string name, NavGraphService graphService)
    {
        var building = new Building(id, name);

        static async Task<bool> FloorExistsAsync(string id, int num)
        {
            var svgRelativePath = $"floor{num}.svg";
            // Try pre-generated WebP first
            var cacheKey = $"{id}_{svgRelativePath}".Replace('/', '_');
            try
            {
                using var s = await FileSystem.OpenAppPackageFileAsync($"FloorImages/{cacheKey}.webp").ConfigureAwait(false);
                return true;
            }
            catch { }
            // Fallback: SVG source
            try
            {
                using var s = await FileSystem.OpenAppPackageFileAsync($"SvgFloors/{id}/{svgRelativePath}").ConfigureAwait(false);
                return true;
            }
            catch { return false; }
        }

        // Basement
        if (await FloorExistsAsync(id, -1).ConfigureAwait(false))
            building.Floors.Add(new Floor(-1, $"{id}/floor-1.svg"));

        // Floors 1..50
        for (int i = 1; i <= 50; i++)
        {
            if (await FloorExistsAsync(id, i).ConfigureAwait(false))
                building.Floors.Add(new Floor(i, $"{id}/floor{i}.svg"));
            else
                break;
        }

        // If no floor files found, fall back to graph-derived floors so building stays visible
        if (building.Floors.Count == 0)
        {
            var graphFloors = graphService.Graph.Nodes
                .Where(n => n.BuildingId == id)
                .Select(n => n.FloorNumber)
                .Distinct()
                .OrderBy(n => n);
            foreach (var num in graphFloors)
                building.Floors.Add(new Floor(num, $"{id}/floor{num}.svg"));

            // Always show at least floor 1 so the building appears
            if (building.Floors.Count == 0)
                building.Floors.Add(new Floor(1, $"{id}/floor1.svg"));
        }

        return building;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Элемент-индикатор шага маршрута (точка).</summary>
public record RouteDotVm(bool IsActive);
