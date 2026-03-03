using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using IndoorNav.Models;
using IndoorNav.Services;
using SkiaSharp;

namespace IndoorNav.ViewModels;

public enum AdminMainTab  { Map, Management }
public enum ManagementTab { Users, Departments, Schedule }

public enum AdminAction { None, AddNode, AddTransition, AddElevator, AddStaircase, AddExit, AddEvacuationExit, AddOther, AddFireExtinguisher, ConnectNode, DisconnectNode, DrawCorridor, MoveNode, DrawBoundary }


public class AdminViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly NavGraphService _graphService;
    private readonly EmergencyService _emergencyService;
    private readonly AuthService _authService;
    private readonly DepartmentService _departmentService;
    private readonly ScheduleService _scheduleService;

    // ── Main tabs ──────────────────────────────────────────────────────────────
    private AdminMainTab _activeTab = AdminMainTab.Map;
    public AdminMainTab ActiveTab
    {
        get => _activeTab;
        set { _activeTab = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsMapMode)); OnPropertyChanged(nameof(IsManagementMode)); }
    }
    public bool IsMapMode        => _activeTab == AdminMainTab.Map;
    public bool IsManagementMode => _activeTab == AdminMainTab.Management;

    private ManagementTab _managementTab = ManagementTab.Users;
    public ManagementTab ActiveManagementTab
    {
        get => _managementTab;
        set { _managementTab = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsUsersTab)); OnPropertyChanged(nameof(IsDepartmentsTab)); OnPropertyChanged(nameof(IsScheduleTab)); }
    }
    public bool IsUsersTab       => _managementTab == ManagementTab.Users;
    public bool IsDepartmentsTab => _managementTab == ManagementTab.Departments;
    public bool IsScheduleTab    => _managementTab == ManagementTab.Schedule;

    public Command SwitchToMapCommand        { get; }
    public Command SwitchToManagementCommand { get; }
    public Command SwitchToUsersTabCommand        { get; }
    public Command SwitchToDepartmentsTabCommand  { get; }
    public Command SwitchToScheduleTabCommand     { get; }

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

    // Рёбра выбранного узла (для панели снизу)
    public ObservableCollection<SelectedEdgeItem> SelectedNodeEdges { get; } = new();
    public bool HasSelectedNodeEdges => SelectedNodeEdges.Count > 0;

    public NavNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            _selectedNode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedHidden));
            OnPropertyChanged(nameof(SelectedLabelHidden));
            OnPropertyChanged(nameof(SelectedNodeScale));
            OnPropertyChanged(nameof(SelectedLabelScale));
            OnPropertyChanged(nameof(SelectedNodeColor));
            OnPropertyChanged(nameof(SelectedInnerLabel));
            OnPropertyChanged(nameof(SelectedSearchTags));
            OnPropertyChanged(nameof(SelectedCategory));
            OnPropertyChanged(nameof(SelectedCategoryHidden));
            OnPropertyChanged(nameof(SelectedNodeIsRoom));
            OnPropertyChanged(nameof(SelectedNodeIsRoomText));
            SelectedBoundaryVertexIndex = -1;
            CopyNodeCommand?.ChangeCanExecute();
            RefreshSelectedNodeEdges();
        }
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
            OnPropertyChanged(nameof(IsElevatorMode));
            OnPropertyChanged(nameof(IsStaircaseMode));
            OnPropertyChanged(nameof(IsExitMode));
            OnPropertyChanged(nameof(IsEvacuationExitMode));
            OnPropertyChanged(nameof(IsOtherMode));
            OnPropertyChanged(nameof(IsFireExtinguisherMode));
            OnPropertyChanged(nameof(IsBoundaryMode));
            UpdateStatus();
        }
    }

    public bool IsAddMode        => CurrentAction == AdminAction.AddNode;
    public bool IsTransitionMode => CurrentAction == AdminAction.AddTransition;
    public bool IsElevatorMode   => CurrentAction == AdminAction.AddElevator;
    public bool IsStaircaseMode  => CurrentAction == AdminAction.AddStaircase;
    public bool IsExitMode           => CurrentAction == AdminAction.AddExit;
    public bool IsEvacuationExitMode  => CurrentAction == AdminAction.AddEvacuationExit;
    public bool IsOtherMode           => CurrentAction == AdminAction.AddOther;
    public bool IsFireExtinguisherMode => CurrentAction == AdminAction.AddFireExtinguisher;
    public bool IsConnectMode    => CurrentAction == AdminAction.ConnectNode;
    public bool IsDisconnectMode => CurrentAction == AdminAction.DisconnectNode;
    public bool IsCorridorMode   => CurrentAction == AdminAction.DrawCorridor;
    public bool IsMoveMode       => CurrentAction == AdminAction.MoveNode;
    public bool IsBoundaryMode   => CurrentAction == AdminAction.DrawBoundary;

    public bool IsEmergencyActive   => _emergencyService.IsEmergencyActive;
    public bool IsEmergencyInactive => !_emergencyService.IsEmergencyActive;

    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; OnPropertyChanged(); }
    }

    // ---- Commands ----

    public Command<SKPoint> CanvasTappedCommand  { get; }
    public Command<NavNode> NodeTappedCommand    { get; }
    public Command<(NavNode, SKPoint)> NodeMovedCommand { get; }
    public Command<(int polyIdx, int vtxIdx, SKPoint svgPos)> BoundaryVertexMovedCommand { get; }
    public Command<(int polyIdx, int vtxIdx)> BoundaryVertexTappedCommand { get; }
    public Command RenameSelectedCommand         { get; }
    public Command DeleteSelectedCommand         { get; }
    public Command DeleteBoundaryVertexCommand   { get; }
    public Command EditInnerLabelCommand         { get; }
    public Command EditSearchTagsCommand         { get; }
    public Command EditCategoryCommand           { get; }
    public Command ToggleIsRoomCommand           { get; }
    public Command SaveCommand                   { get; }
    public Command CancelActionCommand           { get; }
    public Command SetAddModeCommand             { get; }
    public Command SetOtherModeCommand           { get; }
    public Command SetConnectModeCommand         { get; }
    public Command SetDisconnectModeCommand      { get; }
    public Command SetCorridorModeCommand        { get; }
    public Command FinishCorridorCommand         { get; }
    public Command SetMoveModeCommand            { get; }
    public Command ConfirmMoveCommand            { get; }
    public Command SetTransitionModeCommand      { get; }
    public Command SetElevatorModeCommand         { get; }
    public Command SetStaircaseModeCommand        { get; }
    public Command SetExitModeCommand             { get; }
    public Command SetEvacuationExitModeCommand    { get; }
    public Command SetFireExtinguisherModeCommand { get; }
    public Command ToggleEmergencyCommand         { get; }
    public Command AddStudentCommand              { get; }
    public Command<StudentRowVm> RemoveStudentCommand { get; }
    public Command<StudentRowVm> EditStudentCommand   { get; }
    public Command<string> SetNodeColorCommand    { get; }

    // ── Students ──
    public ObservableCollection<StudentRowVm> Students { get; } = new();
    public ObservableCollection<StudentFacultyVm> StudentFaculties { get; } = new();
    private string _newStudentName     = string.Empty;
    private string _newStudentLogin    = string.Empty;
    private string _newStudentPassword = string.Empty;
    public string NewStudentName
    {
        get => _newStudentName;
        set { _newStudentName = value; OnPropertyChanged(); AddStudentCommand?.ChangeCanExecute(); }
    }
    public string NewStudentLogin
    {
        get => _newStudentLogin;
        set { _newStudentLogin = value; OnPropertyChanged(); AddStudentCommand?.ChangeCanExecute(); }
    }
    public string NewStudentPassword
    {
        get => _newStudentPassword;
        set { _newStudentPassword = value; OnPropertyChanged(); AddStudentCommand?.ChangeCanExecute(); }
    }

    // Department picker for new student
    private Department? _newStudentDept;
    public Department? NewStudentDept
    {
        get => _newStudentDept;
        set
        {
            _newStudentDept = value;
            NewStudentGroup = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NewStudentDeptGroups));
        }
    }
    private StudyGroup? _newStudentGroup;
    public StudyGroup? NewStudentGroup
    {
        get => _newStudentGroup;
        set { _newStudentGroup = value; OnPropertyChanged(); AddStudentCommand?.ChangeCanExecute(); }
    }
    // Returns the actual ObservableCollection so the picker reflects live group changes
    public ObservableCollection<StudyGroup>? NewStudentDeptGroups => _newStudentDept?.Groups;

    // ── Departments ──
    public ObservableCollection<Department> Departments { get; } = new();
    public ObservableCollection<DeptViewVm>  DeptViewItems { get; } = new();

    private Department? _selectedDept;
    public Department? SelectedDept
    {
        get => _selectedDept;
        set
        {
            _selectedDept = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedDeptGroups));
            OnPropertyChanged(nameof(HasSelectedDept));
            AddGroupCommand?.ChangeCanExecute();
        }
    }
    public bool HasSelectedDept => _selectedDept != null;
    public ObservableCollection<StudyGroup> SelectedDeptGroups =>
        new(_selectedDept?.Groups ?? []);

    private string _newDeptName = string.Empty;
    public string NewDeptName
    {
        get => _newDeptName;
        set { _newDeptName = value; OnPropertyChanged(); AddDepartmentCommand?.ChangeCanExecute(); }
    }
    private string _newGroupName = string.Empty;
    public string NewGroupName
    {
        get => _newGroupName;
        set { _newGroupName = value; OnPropertyChanged(); AddGroupCommand?.ChangeCanExecute(); }
    }

    public Command AddDepartmentCommand            { get; }
    public Command<Department> RemoveDepartmentCommand { get; }
    public Command<Department> RenameDepartmentCommand { get; }
    public Command AddGroupCommand                 { get; }
    public Command<StudyGroup> RemoveGroupCommand  { get; }
    public Command<StudyGroup> RenameGroupCommand  { get; }

    // ── Schedule ──────────────────────────────────────────────────────────────
    public ObservableCollection<ScheduleEntry> ScheduleEntries { get; } = new();

    private DateTime _newEntryDate = DateTime.Today;
    public DateTime NewEntryDate
    {
        get => _newEntryDate;
        set { _newEntryDate = value; OnPropertyChanged(); }
    }

    private string _newEntryStart = "08:00";
    public string NewEntryStart
    {
        get => _newEntryStart;
        set { _newEntryStart = value; OnPropertyChanged(); AddScheduleEntryCommand?.ChangeCanExecute(); }
    }

    private string _newEntryEnd = "09:30";
    public string NewEntryEnd
    {
        get => _newEntryEnd;
        set { _newEntryEnd = value; OnPropertyChanged(); AddScheduleEntryCommand?.ChangeCanExecute(); }
    }

    private NavNode? _newEntrySelectedRoom;
    public NavNode? NewEntrySelectedRoom
    {
        get => _newEntrySelectedRoom;
        set { _newEntrySelectedRoom = value; OnPropertyChanged(); OnPropertyChanged(nameof(NewEntryRoomDisplay)); AddScheduleEntryCommand?.ChangeCanExecute(); }
    }
    public string NewEntryRoomDisplay => _newEntrySelectedRoom?.Name ?? "Не выбрана";

    private StudyGroup? _newEntrySelectedGroup;
    public StudyGroup? NewEntrySelectedGroup
    {
        get => _newEntrySelectedGroup;
        set { _newEntrySelectedGroup = value; OnPropertyChanged(); OnPropertyChanged(nameof(NewEntryGroupDisplay)); OnPropertyChanged(nameof(NewEntryPersonCountAuto)); AddScheduleEntryCommand?.ChangeCanExecute(); }
    }
    public string NewEntryGroupDisplay => _newEntrySelectedGroup?.Name ?? "Не выбрана";
    public int NewEntryPersonCountAuto =>
        _newEntrySelectedGroup == null ? 0
        : _authService.GetStudents().Count(s => s.GroupId == _newEntrySelectedGroup.Id);

    // Tree models for room picker: Building → Floor → Rooms
    public ObservableCollection<SchedBuildingGroup> ScheduleRoomTree { get; } = new();
    // Tree models for group picker: Department → Groups
    public ObservableCollection<SchedDeptGroup> ScheduleDeptGroups { get; } = new();

    // Entry display trees
    public ObservableCollection<EntryBuildingGroup> ScheduleEntriesViewByRoom  { get; } = new();
    public ObservableCollection<EntryFacultyGroup>  ScheduleEntriesViewByGroup { get; } = new();

    private int _scheduleViewMode = 0;
    public int ScheduleViewMode
    {
        get => _scheduleViewMode;
        set { _scheduleViewMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsScheduleViewAll)); OnPropertyChanged(nameof(IsScheduleViewRoom)); OnPropertyChanged(nameof(IsScheduleViewGroup)); }
    }
    public bool IsScheduleViewAll   => _scheduleViewMode == 0;
    public bool IsScheduleViewRoom  => _scheduleViewMode == 1;
    public bool IsScheduleViewGroup => _scheduleViewMode == 2;

    public Command AddScheduleEntryCommand                   { get; }
    public Command<ScheduleEntry> RemoveScheduleEntryCommand  { get; }
    public Command<NavNode> SelectScheduleRoomCommand         { get; }
    public Command<StudyGroup> SelectScheduleGroupCommand     { get; }
    public Command<string>     SwitchScheduleViewCommand      { get; }
    public Command ResetNodeStyleCommand          { get; }
    public Command ResetGraphCommand             { get; }
    public Command CopyNodeCommand               { get; }
    public Command PasteNodeCommand              { get; }
    public Command SetBoundaryModeCommand        { get; }
    public Command FinishBoundaryCommand         { get; }
    public Command ClearBoundaryCommand          { get; }
    public Command RemoveLastBoundaryCommand     { get; }
    public Command<SelectedEdgeItem> RemoveEdgeCommand { get; }

    // ──── Wrapper-свойства для кастомизации выбранного узла ────
    public bool SelectedHidden
    {
        get => SelectedNode?.IsHidden ?? false;
        set { if (SelectedNode != null) { SelectedNode.IsHidden = value; OnPropertyChanged(); RefreshOverlay(); } }
    }
    public bool SelectedLabelHidden
    {
        get => SelectedNode?.IsLabelHidden ?? false;
        set { if (SelectedNode != null) { SelectedNode.IsLabelHidden = value; OnPropertyChanged(); RefreshOverlay(); } }
    }
    public float SelectedNodeScale
    {
        get => SelectedNode?.NodeRadiusScale ?? 1f;
        set { if (SelectedNode != null) { SelectedNode.NodeRadiusScale = value; OnPropertyChanged(); RefreshOverlay(); } }
    }
    public float SelectedLabelScale
    {
        get => SelectedNode?.LabelScale ?? 1f;
        set { if (SelectedNode != null) { SelectedNode.LabelScale = value; OnPropertyChanged(); RefreshOverlay(); } }
    }
    public string SelectedNodeColor
    {
        get => SelectedNode?.NodeColorHex ?? string.Empty;
        set { if (SelectedNode != null) { SelectedNode.NodeColorHex = value; OnPropertyChanged(); RefreshOverlay(); } }
    }
    public string SelectedInnerLabel
    {
        get => SelectedNode?.InnerLabel ?? string.Empty;
        set { if (SelectedNode != null) { SelectedNode.InnerLabel = value; OnPropertyChanged(); RefreshOverlay(); } }
    }
    public string SelectedSearchTags
    {
        get => SelectedNode?.SearchTags ?? string.Empty;
        set { if (SelectedNode != null) { SelectedNode.SearchTags = value; OnPropertyChanged(); } }
    }
    public string SelectedCategory
    {
        get => SelectedNode?.Category ?? string.Empty;
        set { if (SelectedNode != null) { SelectedNode.Category = value; OnPropertyChanged(); } }
    }
    public bool SelectedCategoryHidden
    {
        get => SelectedNode?.IsCategoryHidden ?? false;
        set { if (SelectedNode != null) { SelectedNode.IsCategoryHidden = value; OnPropertyChanged(); } }
    }
    public bool SelectedNodeIsRoom
    {
        get => SelectedNode?.IsRoom ?? false;
        set { if (SelectedNode != null) { SelectedNode.IsRoom = value; OnPropertyChanged(); OnPropertyChanged(nameof(SelectedNodeIsRoomText)); } }
    }
    public string SelectedNodeIsRoomText => SelectedNodeIsRoom ? "🏠 Аудитория: Да" : "🏠 Аудитория: Нет";

    private int _selectedBoundaryVertexIndex = -1;
    public int SelectedBoundaryVertexIndex
    {
        get => _selectedBoundaryVertexIndex;
        set
        {
            _selectedBoundaryVertexIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasBoundaryVertexSelected));
            DeleteBoundaryVertexCommand?.ChangeCanExecute();
        }
    }
    public bool HasBoundaryVertexSelected => _selectedBoundaryVertexIndex >= 0;

    private int _selectedBoundaryPolygonIndex = -1;
    public int SelectedBoundaryPolygonIndex
    {
        get => _selectedBoundaryPolygonIndex;
        set
        {
            _selectedBoundaryPolygonIndex = value;
            OnPropertyChanged();
        }
    }

    // Буфер копирования
    private NavNode? _copiedNode;

    // Граница аудитории (черновик)
    private readonly List<SkiaSharp.SKPoint> _boundaryPoints = new();

    /// <summary>Точки в процессе рисования — передаются в SvgView для превью.
    /// Возвращаем новый экземпляр, чтобы привязка MAUI фиксировала изменение ссылки
    /// и вызывала InvalidateSurface() при каждом OnPropertyChanged.</summary>
    public IList<SkiaSharp.SKPoint> BoundaryPreview => new List<SkiaSharp.SKPoint>(_boundaryPoints);

    // Последний узел в цепочке коридора
    private NavNode? _corridorLastNode;
    private int      _corridorStepCount;

    public AdminViewModel(NavGraphService graphService, EmergencyService emergencyService, AuthService authService, DepartmentService departmentService, ScheduleService scheduleService, MainViewModel mainViewModel)
    {
        _graphService      = graphService;
        _emergencyService  = emergencyService;
        _authService       = authService;
        _departmentService = departmentService;
        _scheduleService   = scheduleService;

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
        BoundaryVertexMovedCommand = new Command<(int polyIdx, int vtxIdx, SKPoint svgPos)>(args =>
        {
            if (SelectedNode?.Boundaries == null) return;
            if (args.polyIdx < 0 || args.polyIdx >= SelectedNode.Boundaries.Count) return;
            var poly = SelectedNode.Boundaries[args.polyIdx];
            if (args.vtxIdx < 0 || args.vtxIdx >= poly.Count) return;
            poly[args.vtxIdx][0] = args.svgPos.X;
            poly[args.vtxIdx][1] = args.svgPos.Y;
            RefreshOverlay();
        });
        BoundaryVertexTappedCommand = new Command<(int polyIdx, int vtxIdx)>(args =>
        {
            if (SelectedBoundaryPolygonIndex == args.polyIdx && SelectedBoundaryVertexIndex == args.vtxIdx)
            {
                SelectedBoundaryPolygonIndex = -1;
                SelectedBoundaryVertexIndex  = -1;
            }
            else
            {
                SelectedBoundaryPolygonIndex = args.polyIdx;
                SelectedBoundaryVertexIndex  = args.vtxIdx;
            }
        });
        RenameSelectedCommand = new Command(async () => await RenameSelectedAsync(), () => SelectedNode != null);
        DeleteSelectedCommand = new Command(DeleteSelected, () => SelectedNode != null);
        DeleteBoundaryVertexCommand = new Command(() =>
        {
            if (SelectedNode?.Boundaries == null) return;
            if (_selectedBoundaryPolygonIndex < 0 || _selectedBoundaryPolygonIndex >= SelectedNode.Boundaries.Count) return;
            var poly = SelectedNode.Boundaries[_selectedBoundaryPolygonIndex];
            if (_selectedBoundaryVertexIndex < 0 || _selectedBoundaryVertexIndex >= poly.Count) return;
            poly.RemoveAt(_selectedBoundaryVertexIndex);
            // Удалить пустой полигон
            if (poly.Count == 0)
                SelectedNode.Boundaries.RemoveAt(_selectedBoundaryPolygonIndex);
            SelectedBoundaryPolygonIndex = -1;
            SelectedBoundaryVertexIndex  = -1;
            RefreshOverlay();
            StatusText = $"Вершина удалена [{SelectedNode.Name}].\u00a0Границ: {SelectedNode.Boundaries?.Count ?? 0}";
        }, () => _selectedBoundaryVertexIndex >= 0);
        EditInnerLabelCommand = new Command(async () =>
        {
            if (SelectedNode == null) return;
            var val = await Shell.Current.DisplayPromptAsync(
                "Надпись на точке",
                "Введите надпись, которая будет отображаться внутри кружка.",
                initialValue: SelectedNode.InnerLabel,
                placeholder: "Пусто = нет надписи",
                maxLength: 10);
            if (val == null) return; // cancel
            SelectedInnerLabel = val.Trim();
        });
        EditSearchTagsCommand = new Command(async () =>
        {
            if (SelectedNode == null) return;
            var val = await Shell.Current.DisplayPromptAsync(
                "Доп. ключевые слова для поиска",
                "Слова, по которым можно найти эту точку. Не отображаются на карте.",
                initialValue: SelectedNode.SearchTags,
                placeholder: "Например: факультет ИТИТ, ФизМат",
                maxLength: 200);
            if (val == null) return; // cancel
            SelectedSearchTags = val.Trim();
        });
        EditCategoryCommand = new Command(async () =>
        {
            if (SelectedNode == null) return;
            var val = await Shell.Current.DisplayPromptAsync(
                "Категория точки",
                "Категория отображается пользователю при выборе точки (например: Кафедра, Туалет, Столовая)",
                initialValue: SelectedNode.Category,
                placeholder: "Пусто = без категории",
                maxLength: 100);
            if (val == null) return; // cancel
            SelectedCategory = val.Trim();
        });
        ToggleIsRoomCommand = new Command(() =>
        {
            if (SelectedNode == null) return;
            SelectedNodeIsRoom = !SelectedNodeIsRoom;
        });
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
        SetOtherModeCommand      = new Command(() => CurrentAction = AdminAction.AddOther);
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
        SetElevatorModeCommand = new Command(() =>
        {
            CurrentAction = AdminAction.AddElevator;
            StatusText    = "Режим лифта: нажмите на план — будет добавлена точка лифта.";
        });
        SetStaircaseModeCommand = new Command(() =>
        {
            CurrentAction = AdminAction.AddStaircase;
            StatusText    = "Режим лестницы: нажмите на план — будет добавлена точка лестницы.";
        });
        SetExitModeCommand = new Command(() =>
        {
            CurrentAction = AdminAction.AddExit;
            StatusText    = "Режим выхода: нажмите на план — будет добавлена точка выхода.";
        });
        SetEvacuationExitModeCommand = new Command(() =>
        {
            CurrentAction = AdminAction.AddEvacuationExit;
            StatusText    = "Режим эвак. выхода: нажмите на план — будет добавлена точка эвак. выхода (видна только в режиме ЧС).";
        });
        SetFireExtinguisherModeCommand = new Command(() =>
        {
            CurrentAction = AdminAction.AddFireExtinguisher;
            StatusText    = "Режим огнетушителя: нажмите на план — будет добавлена точка огнетушителя (зелёная).";
        });
        ToggleEmergencyCommand = new Command(() =>
        {
            if (_emergencyService.IsEmergencyActive)
                _emergencyService.Deactivate();
            else
                _emergencyService.Activate();
            OnPropertyChanged(nameof(IsEmergencyActive));
            OnPropertyChanged(nameof(IsEmergencyInactive));
        });

        // Tab switching
        SwitchToMapCommand        = new Command(() => ActiveTab = AdminMainTab.Map);
        SwitchToManagementCommand = new Command(() => ActiveTab = AdminMainTab.Management);
        SwitchToUsersTabCommand        = new Command(() => ActiveManagementTab = ManagementTab.Users);
        SwitchToDepartmentsTabCommand  = new Command(() => ActiveManagementTab = ManagementTab.Departments);
        SwitchToScheduleTabCommand     = new Command(() =>
        {
            ActiveManagementTab = ManagementTab.Schedule;
            RebuildScheduleRoomTree();
            RebuildScheduleDeptGroups();
        });

        // Students are loaded after departments in LoadDepartmentsAsync (for name resolution)
        _ = LoadDepartmentsAsync();
        _ = LoadScheduleAsync();

        // Schedule commands
        SelectScheduleRoomCommand = new Command<NavNode>(node =>
        {
            NewEntrySelectedRoom = node;
        });

        SelectScheduleGroupCommand = new Command<StudyGroup>(group =>
        {
            NewEntrySelectedGroup = group;
        });

        SwitchScheduleViewCommand = new Command<string>(m => ScheduleViewMode = int.TryParse(m, out var i) ? i : 0);

        AddScheduleEntryCommand = new Command(async () =>
        {
            if (_newEntrySelectedRoom == null || _newEntrySelectedGroup == null) return;
            var entry = new ScheduleEntry
            {
                RoomName    = _newEntrySelectedRoom.Name,
                RoomNodeId  = _newEntrySelectedRoom.Id,
                GroupName   = _newEntrySelectedGroup.Name,
                GroupId     = _newEntrySelectedGroup.Id,
                PersonCount = NewEntryPersonCountAuto,
                Date        = _newEntryDate.ToString("yyyy-MM-dd"),
                TimeSlots   = new List<TimeSlot>
                {
                    new TimeSlot { StartTime = NewEntryStart.Trim(), EndTime = NewEntryEnd.Trim() }
                }
            };
            await _scheduleService.AddEntryAsync(entry);
            ScheduleEntries.Add(entry);
            RebuildScheduleEntriesView();
            NewEntrySelectedRoom  = null;
            NewEntrySelectedGroup = null;
            NewEntryDate  = DateTime.Today;
            NewEntryStart = "08:00";
            NewEntryEnd   = "09:30";
            OnPropertyChanged(nameof(NewEntryPersonCountAuto));
        }, () => _newEntrySelectedRoom != null && _newEntrySelectedGroup != null);

        RemoveScheduleEntryCommand = new Command<ScheduleEntry>(async entry =>
        {
            if (entry == null) return;
            await _scheduleService.RemoveEntryAsync(entry.Id);
            ScheduleEntries.Remove(entry);
            RebuildScheduleEntriesView();
        });

        AddStudentCommand = new Command(async () =>
        {
            var login = NewStudentLogin.Trim();
            // Проверка дубликата логина
            if (_authService.GetStudents().Any(u => u.Username.Equals(login, StringComparison.OrdinalIgnoreCase)))
            {
                await Shell.Current.DisplayAlert("Ошибка", $"Пользователь с логином «{login}» уже существует.", "ОК");
                return;
            }
            var user = new AuthUser
            {
                Username     = login,
                PasswordHash = NewStudentPassword.Trim(),
                Role         = UserRole.Student,
                DisplayName  = NewStudentName.Trim(),
                GroupId      = NewStudentGroup?.Id ?? string.Empty,
            };
            await _authService.AddUserAsync(user);
            Students.Add(MakeStudentRow(user));
            RebuildStudentTree();
            NewStudentName = NewStudentLogin = NewStudentPassword = string.Empty;
            NewStudentGroup = null;
            NewStudentDept  = null;
        }, () => !string.IsNullOrWhiteSpace(NewStudentLogin) &&
                 !string.IsNullOrWhiteSpace(NewStudentPassword) &&
                 !string.IsNullOrWhiteSpace(NewStudentName) &&
                 NewStudentGroup != null);

        RemoveStudentCommand = new Command<StudentRowVm>(async row =>
        {
            if (row == null) return;
            bool ok = await Shell.Current.DisplayAlert(
                "Удалить студента",
                $"Удалить «{row.DisplayName}»?",
                "Удалить", "Отмена");
            if (!ok) return;
            await _authService.RemoveUserAsync(row.User.Id);
            Students.Remove(row);
            RebuildStudentTree();
        });

        EditStudentCommand = new Command<StudentRowVm>(async row =>
        {
            if (row == null) return;
            var action = await Shell.Current.DisplayActionSheet(
                $"Редактировать: {row.DisplayName}", "Отмена", null,
                "✒ Изменить имя",
                "🔑 Изменить пароль",
                "🏛 Изменить факультет/группу");

            if (action == "✒ Изменить имя")
            {
                var name = await Shell.Current.DisplayPromptAsync(
                    "Имя", "Новое отображаемое имя:", initialValue: row.User.DisplayName);
                if (string.IsNullOrWhiteSpace(name)) return;
                row.User.DisplayName = name.Trim();
                row.RefreshNames();
                await _authService.UpdateUserAsync();
            }
            else if (action == "🔑 Изменить пароль")
            {
                var pwd = await Shell.Current.DisplayPromptAsync(
                    "Пароль", "Новый пароль:", maxLength: 100);
                if (string.IsNullOrWhiteSpace(pwd)) return;
                await _authService.ChangePasswordAsync(row.User.Id, pwd);
            }
            else if (action == "🏛 Изменить факультет/группу")
            {
                if (!Departments.Any())
                {
                    await Shell.Current.DisplayAlert("Факультеты", "Нет ни одного факультета.", "ОК"); return;
                }
                var deptChoice = await Shell.Current.DisplayActionSheet(
                    "Факультет", "Отмена", null,
                    Departments.Select(d => d.Name).ToArray());
                var dept = Departments.FirstOrDefault(d => d.Name == deptChoice);
                if (dept == null) return;

                if (!dept.Groups.Any())
                {
                    await Shell.Current.DisplayAlert("Группы", $"В «{dept.Name}» нет групп.", "ОК"); return;
                }
                var groupChoice = await Shell.Current.DisplayActionSheet(
                    "Группа", "Отмена", null,
                    dept.Groups.Select(g => g.Name).ToArray());
                var group = dept.Groups.FirstOrDefault(g => g.Name == groupChoice);
                if (group == null) return;

                row.User.GroupId = group.Id;
                row.FacultyName  = dept.Name;
                row.GroupName    = group.Name;
                await _authService.UpdateUserAsync();
                RebuildStudentTree();
            }
        });

        // Departments
        AddDepartmentCommand = new Command(async () =>
        {
            if (string.IsNullOrWhiteSpace(NewDeptName)) return;
            var dept = await _departmentService.AddDepartmentAsync(NewDeptName);
            Departments.Add(dept);
            DeptViewItems.Add(MakeDeptViewVm(dept));
            NewDeptName = string.Empty;
        }, () => !string.IsNullOrWhiteSpace(NewDeptName));

        RemoveDepartmentCommand = new Command<Department>(async dept =>
        {
            if (dept == null) return;
            bool ok = await Shell.Current.DisplayAlert(
                "Удалить факультет",
                $"Удалить \u00ab{dept.Name}\u00bb и все её группы?",
                "Удалить", "Отмена");
            if (!ok) return;
            await _departmentService.RemoveDepartmentAsync(dept.Id);
            Departments.Remove(dept);
            var vItem = DeptViewItems.FirstOrDefault(v => v.Dept.Id == dept.Id);
            if (vItem != null) DeptViewItems.Remove(vItem);
            if (SelectedDept?.Id == dept.Id) SelectedDept = null;
        });

        RenameDepartmentCommand = new Command<Department>(async dept =>
        {
            if (dept == null) return;
            var newName = await Shell.Current.DisplayPromptAsync(
                "Переименовать факультет", "Новое название:",
                initialValue: dept.Name, maxLength: 80);
            if (string.IsNullOrWhiteSpace(newName)) return;
            await _departmentService.RenameDepartmentAsync(dept.Id, newName);
            dept.Name = newName.Trim(); // INotifyPropertyChanged updates the label automatically
        });

        AddGroupCommand = new Command(async () =>
        {
            if (SelectedDept == null || string.IsNullOrWhiteSpace(NewGroupName)) return;
            // AddGroupAsync already adds the group to SelectedDept.Groups (ObservableCollection) — don't add again
            await _departmentService.AddGroupAsync(SelectedDept.Id, NewGroupName);
            if (NewStudentDept?.Id == SelectedDept.Id)
                OnPropertyChanged(nameof(NewStudentDeptGroups));
            NewGroupName = string.Empty;
        }, () => SelectedDept != null && !string.IsNullOrWhiteSpace(NewGroupName));

        RemoveGroupCommand = new Command<StudyGroup>(async group =>
        {
            if (group == null) return;
            // Use group.DepartmentId — independent of which dept is "selected" in the UI
            var dept = Departments.FirstOrDefault(d => d.Id == group.DepartmentId);
            if (dept == null) return;
            await _departmentService.RemoveGroupAsync(dept.Id, group.Id);
            dept.Groups.Remove(group);
            if (NewStudentDept?.Id == dept.Id)
                OnPropertyChanged(nameof(NewStudentDeptGroups));
        });

        RenameGroupCommand = new Command<StudyGroup>(async group =>
        {
            if (group == null) return;
            var newName = await Shell.Current.DisplayPromptAsync(
                "Переименовать группу", "Новое название:",
                initialValue: group.Name, maxLength: 40);
            if (string.IsNullOrWhiteSpace(newName)) return;
            var dept = Departments.FirstOrDefault(d => d.Id == group.DepartmentId);
            if (dept == null) return;
            await _departmentService.RenameGroupAsync(dept.Id, group.Id, newName);
            group.Name = newName.Trim(); // INotifyPropertyChanged updates the label automatically
            if (NewStudentDept?.Id == dept.Id)
                OnPropertyChanged(nameof(NewStudentDeptGroups));
        });
        SetNodeColorCommand = new Command<string>(hex =>
        {
            if (SelectedNode != null)
            {
                SelectedNode.NodeColorHex = string.IsNullOrEmpty(hex) ? null : hex;
                OnPropertyChanged(nameof(SelectedNodeColor));
                RefreshOverlay();
            }
        });
        ResetNodeStyleCommand = new Command(() =>
        {
            if (SelectedNode == null) return;
            SelectedNode.NodeRadiusScale = 1f;
            SelectedNode.LabelScale      = 1f;
            SelectedNode.NodeColorHex    = null;
            SelectedNode.IsHidden        = false;
            SelectedNode.IsLabelHidden   = false;
            OnPropertyChanged(nameof(SelectedHidden));
            OnPropertyChanged(nameof(SelectedLabelHidden));
            OnPropertyChanged(nameof(SelectedNodeScale));
            OnPropertyChanged(nameof(SelectedLabelScale));
            OnPropertyChanged(nameof(SelectedNodeColor));
            RefreshOverlay();
            StatusText = $"Стиль [{SelectedNode.Name}] сброшен.";
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

        CopyNodeCommand = new Command(() =>
        {
            if (SelectedNode == null) return;
            _copiedNode = SelectedNode;
            StatusText  = $"Скопирован: {SelectedNode.Name}. Нажмите Ctrl+V для вставки.";
        }, () => SelectedNode != null);

        PasteNodeCommand = new Command(() =>
        {
            if (_copiedNode == null || SelectedFloor == null || SelectedBuilding == null) return;
            var copy = new NavNode
            {
                Name            = _copiedNode.Name + " (копия)",
                BuildingId      = SelectedBuilding.Id,
                FloorNumber     = SelectedFloor.Number,
                X               = _copiedNode.X + 25f,
                Y               = _copiedNode.Y + 25f,
                IsTransition    = _copiedNode.IsTransition,
                IsElevator      = _copiedNode.IsElevator,
                IsExit          = _copiedNode.IsExit,
                IsHidden        = _copiedNode.IsHidden,
                IsLabelHidden   = _copiedNode.IsLabelHidden,
                NodeRadiusScale = _copiedNode.NodeRadiusScale,
                LabelScale      = _copiedNode.LabelScale,
                NodeColorHex    = _copiedNode.NodeColorHex,
            };
            _graphService.AddNode(copy);
            SelectedNode = copy;
            RefreshOverlay();
            StatusText = $"Вставлен: {copy.Name}";
        }, () => _copiedNode != null);

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
            var newPoly = _boundaryPoints
                .Select(p => new float[] { p.X, p.Y })
                .ToList();
            (SelectedNode.Boundaries ??= new List<List<float[]>>()).Add(newPoly);
            _boundaryPoints.Clear();
            CurrentAction = AdminAction.None;
            await _graphService.SaveAsync();
            int polyCount = SelectedNode.Boundaries.Count;
            StatusText = $"Граница [{SelectedNode.Name}] добавлена ({newPoly.Count} углов). Всего полигонов: {polyCount}.";
            OnPropertyChanged(nameof(BoundaryPreview));
            RefreshOverlay();
            ClearBoundaryCommand.ChangeCanExecute();
            RemoveLastBoundaryCommand.ChangeCanExecute();
        });

        ClearBoundaryCommand = new Command(async () =>
        {
            if (SelectedNode == null) return;
            bool ok = await Shell.Current.DisplayAlert(
                "Очистить границы",
                $"Удалить все границы аудитории [{SelectedNode.Name}]?",
                "Удалить", "Отмена");
            if (!ok) return;
            SelectedNode.Boundaries = null;
            await _graphService.SaveAsync();
            StatusText = $"Границы [{SelectedNode.Name}] удалены."; 
            RefreshOverlay();
            ClearBoundaryCommand.ChangeCanExecute();
            RemoveLastBoundaryCommand.ChangeCanExecute();
        }, () => SelectedNode?.Boundaries?.Count > 0);

        RemoveLastBoundaryCommand = new Command(async () =>
        {
            if (SelectedNode?.Boundaries == null || SelectedNode.Boundaries.Count == 0) return;
            SelectedNode.Boundaries.RemoveAt(SelectedNode.Boundaries.Count - 1);
            SelectedBoundaryPolygonIndex = -1;
            SelectedBoundaryVertexIndex  = -1;
            await _graphService.SaveAsync();
            StatusText = $"Последняя граница [{SelectedNode.Name}] удалена. Осталось: {SelectedNode.Boundaries.Count}.";
            RefreshOverlay();
            ClearBoundaryCommand.ChangeCanExecute();
            RemoveLastBoundaryCommand.ChangeCanExecute();
        }, () => SelectedNode?.Boundaries?.Count > 0);

        SelectedBuilding = Buildings.FirstOrDefault();  // SelectedFloor auto-set to floor 1 via setter

        RemoveEdgeCommand = new Command<SelectedEdgeItem>(item =>
        {
            _graphService.RemoveEdge(item.Edge.FromId, item.Edge.ToId);
            RefreshOverlay();
            RefreshSelectedNodeEdges();
        });
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

        if (CurrentAction == AdminAction.AddNode
         || CurrentAction == AdminAction.AddTransition
         || CurrentAction == AdminAction.AddElevator
         || CurrentAction == AdminAction.AddStaircase
         || CurrentAction == AdminAction.AddExit
         || CurrentAction == AdminAction.AddOther
         || CurrentAction == AdminAction.AddFireExtinguisher
         || CurrentAction == AdminAction.AddEvacuationExit)
        {
            bool isElevatorAction        = CurrentAction == AdminAction.AddElevator;
            bool isStaircaseAction       = CurrentAction == AdminAction.AddStaircase;
            bool isExitAction            = CurrentAction == AdminAction.AddExit;
            bool isEvacuationExitAction  = CurrentAction == AdminAction.AddEvacuationExit;
            bool isOtherAction           = CurrentAction == AdminAction.AddOther;
            bool isFireExtinguisherAction = CurrentAction == AdminAction.AddFireExtinguisher;
            bool forceTransit            = CurrentAction == AdminAction.AddTransition || isElevatorAction || isStaircaseAction;

            string title, promptMsg, placeholder;
            if (isFireExtinguisherAction)
            {
                title = "Огнетушитель"; promptMsg = "Название/номер огнетушителя:"; placeholder = "Например: Огнетушитель 1, ОП-5";
            }
            else if (isEvacuationExitAction)
            {
                title = "Эвак. выход"; promptMsg = "Название эвак. выхода:"; placeholder = "Например: Эвак. выход 1, Западный выход";
            }
            else if (isExitAction)
            {
                title = "Выход"; promptMsg = "Название выхода:"; placeholder = "Например: Главный выход, Выход 1";
            }
            else if (isElevatorAction)
            {
                title = "Лифт"; promptMsg = "Название лифта:"; placeholder = "Например: Лифт 1, Лифт А";
            }
            else if (isStaircaseAction)
            {
                title = "Лестница"; promptMsg = "Название лестницы:"; placeholder = "Например: Лестница А, Лестница главная";
            }
            else if (isOtherAction)
            {
                title = "Прочее"; promptMsg = "Введите название:"; placeholder = "Например: Кофемашина, Информационный стенд";
            }
            else if (forceTransit)
            {
                title = "Точка перехода"; promptMsg = "Название лестницы / лифта:"; placeholder = "Например: Лестница А, Лифт 1";
            }
            else
            {
                title = "Новый узел"; promptMsg = "Введите название аудитории:"; placeholder = "Например: 101, 202, Деканат";
            }

            var name = await Shell.Current.DisplayPromptAsync(title, promptMsg,
                placeholder: placeholder, maxLength: 40);

            if (string.IsNullOrWhiteSpace(name)) return;

            bool isTransit = forceTransit
                          || name.Contains("лифт",     StringComparison.OrdinalIgnoreCase)
                          || name.Contains("лестниц", StringComparison.OrdinalIgnoreCase);

            bool isElevator = isElevatorAction
                           || (!isStaircaseAction && name.Contains("лифт", StringComparison.OrdinalIgnoreCase));

            // Аудитории добавляются с IsHidden=true (видна только подпись под точкой)
            bool isHidden = CurrentAction == AdminAction.AddNode;

            var node = new NavNode
            {
                Name               = name.Trim(),
                BuildingId         = SelectedBuilding.Id,
                FloorNumber        = SelectedFloor.Number,
                X                  = svgPos.X,
                Y                  = svgPos.Y,
                IsTransition       = isTransit,
                IsElevator         = isElevator,
                IsExit             = isExitAction,
                IsEvacuationExit   = isEvacuationExitAction,
                IsHidden           = isHidden,
                IsRoom             = CurrentAction == AdminAction.AddNode,
                IsFireExtinguisher = isFireExtinguisherAction,
                NodeColorHex       = isEvacuationExitAction ? "DC2626" : isFireExtinguisherAction ? "4CAF50" : null,
                NodeRadiusScale    = (isFireExtinguisherAction || isEvacuationExitAction) ? 0.8f : 1f,
                InnerLabel         = isEvacuationExitAction ? "🚪" : isFireExtinguisherAction ? "🧯" : null,
            };

            _graphService.AddNode(node);
            RefreshOverlay();
            CurrentAction = AdminAction.None;
            string typeLabel = isFireExtinguisherAction ? " (огнетушитель)" : isEvacuationExitAction ? " (эвак. выход)" : isExitAction ? " (выход)" : isElevator ? " (лифт)" : isTransit ? " (лестница)" : isOtherAction ? " (прочее)" : " (аудитория)";
            StatusText    = $"Добавлен: {node.Name}{typeLabel}";
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
                RemoveLastBoundaryCommand.ChangeCanExecute();
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
            RefreshSelectedNodeEdges();
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
        RefreshSelectedNodeEdges();
    }

    private void UpdateStatus()
    {
        StatusText = CurrentAction switch
        {
            AdminAction.AddNode              => "Нажмите на карту — будет добавлена аудитория (только подпись, без точки)",
            AdminAction.AddOther             => "Нажмите на карту — будет добавлена точка категории Прочее",
            AdminAction.AddFireExtinguisher  => "Нажмите на карту — будет добавлена точка огнетушителя (зелёная, меньший размер)",
            AdminAction.ConnectNode          => "Нажмите первый, затем второй узел для соединения",
            AdminAction.DisconnectNode       => "Нажмите первый, затем второй узел для разъединения",
            AdminAction.DrawCorridor         => _corridorLastNode == null
                ? "[Коридор] Нажмите на старт коридора или аудиторию"
                : $"[Коридор] Точек: {_corridorStepCount}. Нажмите следующий поворот, тап по узлу — завершить цепочку",
            AdminAction.DrawBoundary         => $"Режим границ: нажимайте углы области [{SelectedNode?.Name}] на плане ({_boundaryPoints.Count} точ.ек)",
            _ => SelectedNode != null ? $"Выбран: {SelectedNode.Name}" : "Выберите узел или действие"
        };
    }

    private async Task LoadDepartmentsAsync()
    {
        await _departmentService.LoadAsync();
        Departments.Clear();
        DeptViewItems.Clear();
        foreach (var d in _departmentService.Departments)
        {
            Departments.Add(d);
            DeptViewItems.Add(MakeDeptViewVm(d));
        }
        // Load students after departments so faculty/group names resolve correctly
        Students.Clear();
        foreach (var s in _authService.GetStudents())
            Students.Add(MakeStudentRow(s));
        RebuildStudentTree();
    }

    private StudentRowVm MakeStudentRow(AuthUser user)
    {
        var group = _departmentService.FindGroup(user.GroupId);
        var dept  = group != null ? _departmentService.FindDepartmentByGroup(user.GroupId) : null;
        return new StudentRowVm(user, dept?.Name ?? "—", group?.Name ?? "—");
    }

    private void RebuildStudentTree()
    {
        StudentFaculties.Clear();
        var byFaculty = Students
            .GroupBy(s => s.FacultyName)
            .OrderBy(g => g.Key);
        foreach (var fg in byFaculty)
        {
            var fvm = new StudentFacultyVm(fg.Key);
            var byGroup = fg.GroupBy(s => s.GroupName).OrderBy(g => g.Key);
            foreach (var gg in byGroup)
            {
                var gvm = new StudentGroupVm(fg.Key, gg.Key);
                foreach (var s in gg)
                    gvm.Students.Add(s);
                fvm.Groups.Add(gvm);
            }
            StudentFaculties.Add(fvm);
        }
    }

    private DeptViewVm MakeDeptViewVm(Department dept) =>
        new DeptViewVm(dept, () => SelectedDept = dept);

    private async Task LoadScheduleAsync()
    {
        await _scheduleService.LoadAsync();
        ScheduleEntries.Clear();
        foreach (var e in _scheduleService.Entries)
            ScheduleEntries.Add(e);
        RebuildScheduleEntriesView();
    }

    private void RebuildScheduleRoomTree()
    {
        ScheduleRoomTree.Clear();
        foreach (var building in Buildings)
        {
            var floorGroups = building.Floors
                .Select(f =>
                {
                    var rooms = _graphService.Graph.Nodes
                        .Where(n => n.BuildingId == building.Id
                                 && n.FloorNumber == f.Number
                                 && !n.IsWaypoint
                                 && !n.IsTransition
                                 && !n.IsExit
                                 && !string.IsNullOrWhiteSpace(n.Name))
                        .OrderBy(n => n.Name)
                        .ToList();
                    return new SchedFloorGroup($"Этаж {f.Number}", rooms);
                })
                .Where(g => g.Rooms.Count > 0)
                .ToList();
            if (floorGroups.Count > 0)
                ScheduleRoomTree.Add(new SchedBuildingGroup(building.Name, floorGroups));
        }
    }

    private void RebuildScheduleDeptGroups()
    {
        ScheduleDeptGroups.Clear();
        foreach (var dept in Departments)
            ScheduleDeptGroups.Add(new SchedDeptGroup(dept));
    }

    private void RebuildScheduleEntriesView()
    {
        // ─── By Room: Building → Floor → Entries
        ScheduleEntriesViewByRoom.Clear();
        var nodeMap     = _graphService.Graph.Nodes.ToDictionary(n => n.Id);
        var buildingMap = Buildings.ToDictionary(b => b.Id, b => b.Name);

        var byBuilding = ScheduleEntries
            .GroupBy(e => nodeMap.TryGetValue(e.RoomNodeId, out var n) ? n.BuildingId : string.Empty)
            .Where(g => !string.IsNullOrEmpty(g.Key));

        foreach (var bGrp in byBuilding.OrderBy(g => buildingMap.TryGetValue(g.Key, out var bn) ? bn : g.Key))
        {
            var buildingName = buildingMap.TryGetValue(bGrp.Key, out var name) ? name : bGrp.Key;
            var byFloor = bGrp
                .GroupBy(e => nodeMap.TryGetValue(e.RoomNodeId, out var n) ? n.FloorNumber : 0)
                .OrderBy(g => g.Key)
                .Select(fg => new EntryFloorGroup($"\u042d\u0442\u0430\u0436 {fg.Key}", fg.ToList()))
                .ToList();
            ScheduleEntriesViewByRoom.Add(new EntryBuildingGroup(buildingName, byFloor));
        }

        // ─── By Group: Faculty → StudyGroup → Entries
        ScheduleEntriesViewByGroup.Clear();
        var groupToFaculty = new Dictionary<string, string>();
        foreach (var dept in Departments)
            foreach (var g in dept.Groups)
                groupToFaculty[g.Id] = dept.Name;

        var byFaculty = ScheduleEntries
            .GroupBy(e => groupToFaculty.TryGetValue(e.GroupId, out var f) ? f : "\u0411\u0435\u0437 \u0444\u0430\u043a\u0443\u043b\u044c\u0442\u0435\u0442\u0430");

        foreach (var fGrp in byFaculty.OrderBy(g => g.Key))
        {
            var byGroup = fGrp.GroupBy(e => e.GroupName)
                .OrderBy(g => g.Key)
                .Select(gg => new EntryGroupEntries(gg.Key, gg.ToList()))
                .ToList();
            ScheduleEntriesViewByGroup.Add(new EntryFacultyGroup(fGrp.Key, byGroup));
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void RefreshSelectedNodeEdges()
    {
        SelectedNodeEdges.Clear();
        var sel = _selectedNode;
        if (sel == null) { OnPropertyChanged(nameof(HasSelectedNodeEdges)); return; }

        var allEdges = _graphService.Graph.Edges;
        var allNodes = _graphService.Graph.Nodes;

        // Дедупликация: каноническая пара (min, max) — как в RefreshOverlay
        var seenPairs = new HashSet<(string, string)>();

        foreach (var edge in allEdges)
        {
            string otherId;
            if      (edge.FromId == sel.Id) otherId = edge.ToId;
            else if (edge.ToId   == sel.Id) otherId = edge.FromId;
            else continue;

            var canonical = string.CompareOrdinal(sel.Id, otherId) <= 0
                ? (sel.Id, otherId)
                : (otherId, sel.Id);
            if (!seenPairs.Add(canonical)) continue;

            var other = allNodes.FirstOrDefault(n => n.Id == otherId);
            SelectedNodeEdges.Add(new SelectedEdgeItem
            {
                Edge          = edge,
                OtherNodeName = other?.Name ?? otherId
            });
        }
        OnPropertyChanged(nameof(HasSelectedNodeEdges));
    }
}

public sealed class StudentRowVm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    void Notify([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    public AuthUser User { get; }

    private string _displayName;
    public string DisplayName { get => _displayName; private set { _displayName = value; Notify(); } }

    private string _facultyName;
    public string FacultyName { get => _facultyName; set { _facultyName = value; Notify(); } }

    private string _groupName;
    public string GroupName   { get => _groupName;   set { _groupName   = value; Notify(); } }

    private bool _showDetails;
    public bool ShowDetails   { get => _showDetails; set { _showDetails = value; Notify(); } }

    public string Login    => User.Username;
    public string Password  => User.PasswordHash;

    public Command ToggleDetailsCommand { get; }

    public StudentRowVm(AuthUser user, string facultyName, string groupName)
    {
        User          = user;
        _displayName  = user.DisplayName;
        _facultyName  = facultyName;
        _groupName    = groupName;
        ToggleDetailsCommand = new Command(() => ShowDetails = !ShowDetails);
    }

    public void RefreshNames() => DisplayName = User.DisplayName;
}

public sealed class SelectedEdgeItem
{
    public NavEdge Edge          { get; init; } = null!;
    public string  OtherNodeName { get; init; } = "";
}

// ── \u0414\u0435\u0440\u0435\u0432\u043e \u0441\u0442\u0443\u0434\u0435\u043d\u0442\u043e\u0432: \u0424\u0430\u043a\u0443\u043b\u044c\u0442\u0435\u0442 \u2192 \u0413\u0440\u0443\u043f\u043f\u0430 \u2192 \u0421\u0442\u0443\u0434\u0435\u043d\u0442 \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

public sealed class StudentGroupVm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    void Notify([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    public string FacultyName { get; }
    public string GroupName   { get; }
    public ObservableCollection<StudentRowVm> Students { get; } = new();

    private bool _expanded = false;
    public bool IsExpanded
    {
        get => _expanded;
        set { _expanded = value; Notify(); Notify(nameof(ChevronText)); }
    }
    public string ChevronText => IsExpanded ? "\u25be" : "\u25b8";
    public Command ToggleCommand { get; }

    public StudentGroupVm(string facultyName, string groupName)
    {
        FacultyName  = facultyName;
        GroupName    = groupName;
        ToggleCommand = new Command(() => IsExpanded = !IsExpanded);
    }
}

public sealed class StudentFacultyVm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    void Notify([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    public string FacultyName { get; }
    public ObservableCollection<StudentGroupVm> Groups { get; } = new();

    public int StudentCount => Groups.Sum(g => g.Students.Count);

    private bool _expanded = false; // \u0437\u0430\u043a\u0440\u044b\u0442 \u043f\u043e \u0443\u043c\u043e\u043b\u0447\u0430\u043d\u0438\u044e
    public bool IsExpanded
    {
        get => _expanded;
        set { _expanded = value; Notify(); Notify(nameof(ChevronText)); }
    }
    public string ChevronText => IsExpanded ? "\u25be" : "\u25b8";
    public Command ToggleCommand { get; }

    public StudentFacultyVm(string facultyName)
    {
        FacultyName  = facultyName;
        ToggleCommand = new Command(() => IsExpanded = !IsExpanded);
    }
}

// ── \u041e\u0431\u0451\u0440\u0442\u043a\u0430 \u0444\u0430\u043a\u0443\u043b\u044c\u0442\u0435\u0442\u0430 \u0441 \u043a\u043d\u043e\u043f\u043a\u043e\u0439 \u0440\u0430\u0437\u0432\u0451\u0440\u0442\u044b\u0432\u0430\u043d\u0438\u044f \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

public sealed class DeptViewVm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    void Notify([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    public Department Dept { get; }
    public string Name => Dept.Name;
    public ObservableCollection<StudyGroup> Groups => Dept.Groups;

    private bool _expanded = false; // \u0437\u0430\u043a\u0440\u044b\u0442 \u043f\u043e \u0443\u043c\u043e\u043b\u0447\u0430\u043d\u0438\u044e
    public bool IsExpanded
    {
        get => _expanded;
        set { _expanded = value; Notify(); Notify(nameof(ChevronText)); }
    }
    public string ChevronText => IsExpanded ? "\u25be" : "\u25b8";

    private readonly Action _selectAction;
    public Command ToggleCommand { get; }

    public DeptViewVm(Department dept, Action selectAction)
    {
        Dept          = dept;
        _selectAction = selectAction;
        ToggleCommand = new Command(() => { IsExpanded = !IsExpanded; _selectAction(); });
    }
}

// ── Schedule picker tree helpers ────────────────────────────────────────────────

public sealed class SchedFloorGroup : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public string FloorLabel { get; }
    public IReadOnlyList<NavNode> Rooms { get; }
    private bool _expanded;
    public bool IsExpanded { get => _expanded; set { _expanded = value; Notify(); Notify(nameof(ChevronText)); } }
    public string ChevronText => IsExpanded ? "▲" : "▼";
    public Command ToggleCommand { get; }
    public SchedFloorGroup(string label, IEnumerable<NavNode> rooms)
    {
        FloorLabel = label;
        Rooms = rooms.ToList();
        ToggleCommand = new Command(() => IsExpanded = !IsExpanded);
    }
    void Notify([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class SchedBuildingGroup : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public string BuildingName { get; }
    public IReadOnlyList<SchedFloorGroup> Floors { get; }
    private bool _expanded;
    public bool IsExpanded { get => _expanded; set { _expanded = value; Notify(); Notify(nameof(ChevronText)); } }
    public string ChevronText => IsExpanded ? "▲" : "▼";
    public Command ToggleCommand { get; }
    public SchedBuildingGroup(string name, IEnumerable<SchedFloorGroup> floors)
    {
        BuildingName = name;
        Floors = floors.ToList();
        ToggleCommand = new Command(() => IsExpanded = !IsExpanded);
    }
    void Notify([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class SchedDeptGroup : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public Department Dept { get; }
    public string Name => Dept.Name;
    public ObservableCollection<StudyGroup> Groups => Dept.Groups;
    private bool _expanded;
    public bool IsExpanded { get => _expanded; set { _expanded = value; Notify(); Notify(nameof(ChevronText)); } }
    public string ChevronText => IsExpanded ? "▲" : "▼";
    public Command ToggleCommand { get; }
    public SchedDeptGroup(Department dept)
    {
        Dept = dept;
        ToggleCommand = new Command(() => IsExpanded = !IsExpanded);
    }
    void Notify([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

// ── Entry display trees ────────────────────────────────────────────────────────

public sealed class EntryFloorGroup : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public string FloorLabel { get; }
    public List<ScheduleEntry> Entries { get; }
    private bool _expanded;
    public bool IsExpanded { get => _expanded; set { _expanded = value; Notify(); Notify(nameof(ChevronText)); } }
    public string ChevronText => IsExpanded ? "▾" : "▸";
    public Command ToggleCommand { get; }
    public EntryFloorGroup(string label, List<ScheduleEntry> entries)
    {
        FloorLabel = label; Entries = entries;
        ToggleCommand = new Command(() => IsExpanded = !IsExpanded);
    }
    void Notify([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class EntryBuildingGroup : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public string BuildingName { get; }
    public List<EntryFloorGroup> Floors { get; }
    private bool _expanded;
    public bool IsExpanded { get => _expanded; set { _expanded = value; Notify(); Notify(nameof(ChevronText)); } }
    public string ChevronText => IsExpanded ? "▾" : "▸";
    public Command ToggleCommand { get; }
    public EntryBuildingGroup(string name, List<EntryFloorGroup> floors)
    {
        BuildingName = name; Floors = floors;
        ToggleCommand = new Command(() => IsExpanded = !IsExpanded);
    }
    void Notify([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class EntryGroupEntries : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public string GroupName { get; }
    public List<ScheduleEntry> Entries { get; }
    private bool _expanded;
    public bool IsExpanded { get => _expanded; set { _expanded = value; Notify(); Notify(nameof(ChevronText)); } }
    public string ChevronText => IsExpanded ? "▾" : "▸";
    public Command ToggleCommand { get; }
    public EntryGroupEntries(string groupName, List<ScheduleEntry> entries)
    {
        GroupName = groupName; Entries = entries;
        ToggleCommand = new Command(() => IsExpanded = !IsExpanded);
    }
    void Notify([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class EntryFacultyGroup : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public string FacultyName { get; }
    public List<EntryGroupEntries> Groups { get; }
    private bool _expanded;
    public bool IsExpanded { get => _expanded; set { _expanded = value; Notify(); Notify(nameof(ChevronText)); } }
    public string ChevronText => IsExpanded ? "▾" : "▸";
    public Command ToggleCommand { get; }
    public EntryFacultyGroup(string facultyName, List<EntryGroupEntries> groups)
    {
        FacultyName = facultyName; Groups = groups;
        ToggleCommand = new Command(() => IsExpanded = !IsExpanded);
    }
    void Notify([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
