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

    private Building? _selectedBuilding;
    private Floor? _selectedFloor;
    private bool _isLoading;

    // --- Routing ---
    private NavNode? _startNode;
    private NavNode? _endNode;
    private string _routeStatus = string.Empty;
    private bool _isPickerOpen;
    private string _pickerTarget = "start"; // "start" or "end"
    private string _pickerSearchText = string.Empty;
    private List<FloorNodeGroup> _nodesByFloor = new();
    private ObservableCollection<NavNode> _routeNodesOnFloor = new();
    private ObservableCollection<NavNode> _nodesOnCurrentFloor = new();
    private ObservableCollection<NavEdge> _edgesOnCurrentFloor = new();
    private ObservableCollection<NavNode> _allNodesForBuilding = new();

    // Конфигурация зданий: Id → отображаемое название.
    // Чтобы добавить новое здание — просто допишите строку сюда.
    private static readonly (string Id, string Name)[] BuildingConfig =
    [
        ("BuildingA", "Здание А"),
        ("BuildingB", "Здание Б"),
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
    public string StartNodeDisplay => _startNode?.Name ?? "Откуда...";

    /// <summary>Text shown in the Куда field (node name or placeholder).</summary>
    public string EndNodeDisplay => _endNode?.Name ?? "Куда...";

    /// <summary>True when at least one of start/end nodes is selected — drives Clear button color.</summary>
    public bool HasAnySelection => _startNode != null || _endNode != null;

    /// <summary>Whether the node-picker popup is currently visible.</summary>
    public bool IsPickerOpen
    {
        get => _isPickerOpen;
        set { _isPickerOpen = value; OnPropertyChanged(); }
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
    public bool HasRoute => _currentFullRoute != null && _currentFullRoute.Count > 0;

    /// <summary>Step-by-step floor instructions shown below the map.</summary>
    public ObservableCollection<string> RouteInstructions { get; } = new();

    /// <summary>Ordered list of floors that are part of the current route (for floor-jump buttons).</summary>
    public ObservableCollection<Floor> RouteFloors { get; } = new();

    /// <summary>True when the current route spans more than one floor.</summary>
    public bool IsMultiFloorRoute => RouteFloors.Count > 1;

    // ── Commands ──────────────────────────────────────────────────────────────────

    public ICommand BuildRouteCommand       { get; }
    public ICommand ClearRouteCommand       { get; }
    public ICommand GoToAdminCommand        { get; }
    public ICommand GoToFloorCommand        { get; }
    public ICommand OpenStartPickerCommand  { get; }
    public ICommand OpenEndPickerCommand    { get; }
    public ICommand SelectPickerNodeCommand { get; }
    public ICommand ClosePickerCommand      { get; }

    // ── Constructor ─────────────────────────────────────────────────────────

    public MainViewModel(NavGraphService graphService)
    {
        _graphService = graphService;

        BuildRouteCommand = new Command(ExecuteBuildRoute,
            () => StartNode != null && EndNode != null);
        ClearRouteCommand = new Command(ExecuteClearRoute);
        GoToAdminCommand  = new Command(async () =>
            await Shell.Current.GoToAsync("admin"));
        GoToFloorCommand  = new Command<Floor>(f => { if (f != null) SelectedFloor = f; });

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

        var name = node.Name.ToLowerInvariant();
        var q    = query.Trim().ToLowerInvariant();

        // 1) Direct substring
        if (name.Contains(q)) return true;

        // 2) All query words appear somewhere in the name
        var qWords = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (qWords.Length > 1 && qWords.All(w => name.Contains(w))) return true;

        // 3) Abbreviation: each query char matches first char of a word in the name
        //    e.g. "кф" → "Кафедра Физики"
        var nameWords = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (nameWords.Length >= q.Length)
        {
            var initials = string.Concat(nameWords.Select(w => w[0]));
            if (initials.Contains(q)) return true;
        }

        // 4) Translit / partial: query without spaces inside the name without spaces
        //    e.g. "ауд101" matches "Аудитория 101"
        var nameNoSpaces = name.Replace(" ", "");
        var qNoSpaces    = q.Replace(" ", "");
        if (nameNoSpaces.Contains(qNoSpaces)) return true;

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

        var path = _graphService.Graph.FindPath(StartNode.Id, EndNode.Id);
        RouteInstructions.Clear();

        if (path is null || path.Count == 0)
        {
            _currentFullRoute = null;            // сначала null, потом уведомляем
            RouteStatus = "Маршрут не найден.";  // триггерит HasRoute=false
        }
        else
        {
            _currentFullRoute = path;
            BuildRouteInstructions(path);
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

    private void BuildRouteInstructions(List<NavNode> path)
    {
        int distinctFloors = path.Select(n => n.FloorNumber).Distinct().Count();
        RouteStatus = distinctFloors > 1
            ? $"Маршрут найден ({distinctFloors} этажа)"
            : $"Маршрут найден ({path.Count(n => !n.IsWaypoint)} точки)";

        int curFloor = path[0].FloorNumber;
        RouteInstructions.Add($"📍 Этаж {curFloor}:");

        for (int i = 0; i < path.Count; i++)
        {
            var n = path[i];

            if (n.FloorNumber != curFloor)
            {
                // Нашли переход на другой этаж
                var prev = path[i - 1];
                string via   = prev.IsTransition ? prev.Name : "переход";
                string dir   = n.FloorNumber > curFloor ? "поднимитесь" : "спуститесь";
                RouteInstructions.Add($"🔼 Пройдите к [{via}] и {dir} на {n.FloorNumber}-й этаж");
                curFloor = n.FloorNumber;
                RouteInstructions.Add($"📍 Этаж {curFloor}:");
            }

            // Показываем только значимые узлы (не скрытые waypoint)
            if (!n.IsWaypoint)
                RouteInstructions.Add($"  → {n.Name}");
        }
    }

    private void ExecuteClearRoute()
    {
        _currentFullRoute = null;
        RouteNodesOnFloor.Clear();
        RouteInstructions.Clear();
        RouteFloors.Clear();
        RouteStatus = string.Empty;
        StartNode = null;
        EndNode = null;
        OnPropertyChanged(nameof(HasRoute));
        OnPropertyChanged(nameof(IsMultiFloorRoute));
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
        if (StartNode == null)
        {
            StartNode = node;
            RouteStatus = $"Старт: {node.Name}. Выберите пункт назначения.";
        }
        else if (EndNode == null && node != StartNode)
        {
            EndNode = node;
            RouteStatus = $"Назначение: {node.Name}. Нажмите «Построить».";
            ((Command)BuildRouteCommand).ChangeCanExecute();
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
