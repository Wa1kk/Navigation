using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using IndoorNav.Models;
using IndoorNav.Services;
using SkiaSharp;

namespace IndoorNav.ViewModels;

public enum AdminMainTab  { Map, Management }
public enum ManagementTab { Users, Departments }

public enum AdminAction { None, AddNode, AddTransition, AddElevator, AddStaircase, AddExit, AddOther, AddFireExtinguisher, ConnectNode, DisconnectNode, DrawCorridor, MoveNode, DrawBoundary }


public class AdminViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly NavGraphService _graphService;
    private readonly EmergencyService _emergencyService;
    private readonly AuthService _authService;
    private readonly DepartmentService _departmentService;

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
        set { _managementTab = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsUsersTab)); OnPropertyChanged(nameof(IsDepartmentsTab)); }
    }
    public bool IsUsersTab       => _managementTab == ManagementTab.Users;
    public bool IsDepartmentsTab => _managementTab == ManagementTab.Departments;

    public Command SwitchToMapCommand        { get; }
    public Command SwitchToManagementCommand { get; }
    public Command SwitchToUsersTabCommand        { get; }
    public Command SwitchToDepartmentsTabCommand  { get; }

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
    public bool IsExitMode       => CurrentAction == AdminAction.AddExit;
    public bool IsOtherMode      => CurrentAction == AdminAction.AddOther;
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
    public Command<(int idx, SKPoint svgPos)> BoundaryVertexMovedCommand { get; }
    public Command<int> BoundaryVertexTappedCommand { get; }
    public Command RenameSelectedCommand         { get; }
    public Command DeleteSelectedCommand         { get; }
    public Command DeleteBoundaryVertexCommand   { get; }
    public Command EditInnerLabelCommand         { get; }
    public Command EditSearchTagsCommand         { get; }
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
    public Command SetFireExtinguisherModeCommand { get; }
    public Command ToggleEmergencyCommand         { get; }
    public Command AddStudentCommand              { get; }
    public Command<AuthUser> RemoveStudentCommand { get; }
    public Command<string> SetNodeColorCommand    { get; }

    // ── Students ──
    public ObservableCollection<AuthUser> Students { get; } = new();
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
    public ObservableCollection<StudyGroup> NewStudentDeptGroups =>
        new(_newStudentDept?.Groups ?? []);

    // ── Departments ──
    public ObservableCollection<Department> Departments { get; } = new();

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
    public Command ResetNodeStyleCommand          { get; }
    public Command ResetGraphCommand             { get; }
    public Command CopyNodeCommand               { get; }
    public Command PasteNodeCommand              { get; }
    public Command SetBoundaryModeCommand        { get; }
    public Command FinishBoundaryCommand         { get; }
    public Command ClearBoundaryCommand          { get; }
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

    public AdminViewModel(NavGraphService graphService, EmergencyService emergencyService, AuthService authService, DepartmentService departmentService, MainViewModel mainViewModel)
    {
        _graphService      = graphService;
        _emergencyService  = emergencyService;
        _authService       = authService;
        _departmentService = departmentService;

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
        BoundaryVertexMovedCommand = new Command<(int idx, SKPoint svgPos)>(args =>
        {
            if (SelectedNode?.Boundary == null || args.idx < 0 || args.idx >= SelectedNode.Boundary.Count) return;
            SelectedNode.Boundary[args.idx][0] = args.svgPos.X;
            SelectedNode.Boundary[args.idx][1] = args.svgPos.Y;
            RefreshOverlay();
        });
        BoundaryVertexTappedCommand = new Command<int>(idx =>
        {
            SelectedBoundaryVertexIndex = SelectedBoundaryVertexIndex == idx ? -1 : idx;
        });
        RenameSelectedCommand = new Command(async () => await RenameSelectedAsync(), () => SelectedNode != null);
        DeleteSelectedCommand = new Command(DeleteSelected, () => SelectedNode != null);
        DeleteBoundaryVertexCommand = new Command(() =>
        {
            if (SelectedNode?.Boundary == null || _selectedBoundaryVertexIndex < 0 ||
                _selectedBoundaryVertexIndex >= SelectedNode.Boundary.Count) return;
            SelectedNode.Boundary.RemoveAt(_selectedBoundaryVertexIndex);
            SelectedBoundaryVertexIndex = -1;
            RefreshOverlay();
            StatusText = $"Вершина удалена [{SelectedNode.Name}]. Точек: {SelectedNode.Boundary?.Count ?? 0}";
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
                placeholder: "Например: кафедра ритм, ДеканатСтрой",
                maxLength: 200);
            if (val == null) return; // cancel
            SelectedSearchTags = val.Trim();
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

        // Load departments
        _ = LoadDepartmentsAsync();

        // Students
        foreach (var s in _authService.GetStudents())
            Students.Add(s);

        AddStudentCommand = new Command(async () =>
        {
            var user = new AuthUser
            {
                Username     = NewStudentLogin.Trim(),
                PasswordHash = NewStudentPassword.Trim(),
                Role         = UserRole.Student,
                DisplayName  = NewStudentName.Trim(),
                GroupId      = NewStudentGroup?.Id ?? string.Empty,
            };
            await _authService.AddUserAsync(user);
            Students.Add(user);
            NewStudentName = NewStudentLogin = NewStudentPassword = string.Empty;
            NewStudentGroup = null;
            NewStudentDept  = null;
        }, () => !string.IsNullOrWhiteSpace(NewStudentLogin) &&
                 !string.IsNullOrWhiteSpace(NewStudentPassword) &&
                 !string.IsNullOrWhiteSpace(NewStudentName) &&
                 NewStudentGroup != null);

        RemoveStudentCommand = new Command<AuthUser>(async user =>
        {
            if (user == null) return;
            await _authService.RemoveUserAsync(user.Id);
            Students.Remove(user);
        });

        // Departments
        AddDepartmentCommand = new Command(async () =>
        {
            if (string.IsNullOrWhiteSpace(NewDeptName)) return;
            var dept = await _departmentService.AddDepartmentAsync(NewDeptName);
            Departments.Add(dept);
            NewDeptName = string.Empty;
        }, () => !string.IsNullOrWhiteSpace(NewDeptName));

        RemoveDepartmentCommand = new Command<Department>(async dept =>
        {
            if (dept == null) return;
            bool ok = await Shell.Current.DisplayAlert(
                "Удалить кафедру",
                $"Удалить \u00ab{dept.Name}\u00bb и все её группы?",
                "Удалить", "Отмена");
            if (!ok) return;
            await _departmentService.RemoveDepartmentAsync(dept.Id);
            Departments.Remove(dept);
            if (SelectedDept?.Id == dept.Id) SelectedDept = null;
        });

        RenameDepartmentCommand = new Command<Department>(async dept =>
        {
            if (dept == null) return;
            var newName = await Shell.Current.DisplayPromptAsync(
                "Переименовать кафедру", "Новое название:",
                initialValue: dept.Name, maxLength: 80);
            if (string.IsNullOrWhiteSpace(newName)) return;
            await _departmentService.RenameDepartmentAsync(dept.Id, newName);
            dept.Name = newName.Trim();
            OnPropertyChanged(nameof(Departments));
        });

        AddGroupCommand = new Command(async () =>
        {
            if (SelectedDept == null || string.IsNullOrWhiteSpace(NewGroupName)) return;
            var group = await _departmentService.AddGroupAsync(SelectedDept.Id, NewGroupName);
            SelectedDept.Groups.Add(group);
            OnPropertyChanged(nameof(SelectedDeptGroups));
            // also refresh new-student picker
            if (NewStudentDept?.Id == SelectedDept.Id)
                OnPropertyChanged(nameof(NewStudentDeptGroups));
            NewGroupName = string.Empty;
        }, () => SelectedDept != null && !string.IsNullOrWhiteSpace(NewGroupName));

        RemoveGroupCommand = new Command<StudyGroup>(async group =>
        {
            if (group == null || SelectedDept == null) return;
            await _departmentService.RemoveGroupAsync(SelectedDept.Id, group.Id);
            SelectedDept.Groups.Remove(group);
            OnPropertyChanged(nameof(SelectedDeptGroups));
            if (NewStudentDept?.Id == SelectedDept.Id)
                OnPropertyChanged(nameof(NewStudentDeptGroups));
        });

        RenameGroupCommand = new Command<StudyGroup>(async group =>
        {
            if (group == null || SelectedDept == null) return;
            var newName = await Shell.Current.DisplayPromptAsync(
                "Переименовать группу", "Новое название:",
                initialValue: group.Name, maxLength: 40);
            if (string.IsNullOrWhiteSpace(newName)) return;
            await _departmentService.RenameGroupAsync(SelectedDept.Id, group.Id, newName);
            group.Name = newName.Trim();
            OnPropertyChanged(nameof(SelectedDeptGroups));
            if (NewStudentDept?.Id == SelectedDept.Id)
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
         || CurrentAction == AdminAction.AddFireExtinguisher)
        {
            bool isElevatorAction        = CurrentAction == AdminAction.AddElevator;
            bool isStaircaseAction       = CurrentAction == AdminAction.AddStaircase;
            bool isExitAction            = CurrentAction == AdminAction.AddExit;
            bool isOtherAction           = CurrentAction == AdminAction.AddOther;
            bool isFireExtinguisherAction = CurrentAction == AdminAction.AddFireExtinguisher;
            bool forceTransit            = CurrentAction == AdminAction.AddTransition || isElevatorAction || isStaircaseAction;

            string title, promptMsg, placeholder;
            if (isFireExtinguisherAction)
            {
                title = "Огнетушитель"; promptMsg = "Название/номер огнетушителя:"; placeholder = "Например: Огнетушитель 1, ОП-5";
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
                IsHidden           = isHidden,
                IsFireExtinguisher = isFireExtinguisherAction,
                NodeColorHex       = isFireExtinguisherAction ? "4CAF50" : null,
                NodeRadiusScale    = isFireExtinguisherAction ? 0.6f : 1f,
                InnerLabel         = isFireExtinguisherAction ? "🧯" : null,
            };

            _graphService.AddNode(node);
            RefreshOverlay();
            CurrentAction = AdminAction.None;
            string typeLabel = isFireExtinguisherAction ? " (огнетушитель)" : isExitAction ? " (выход)" : isElevator ? " (лифт)" : isTransit ? " (лестница)" : isOtherAction ? " (прочее)" : " (аудитория)";
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
        foreach (var d in _departmentService.Departments)
            Departments.Add(d);
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

/// <summary>Элемент списка рёбер выбранного узла для панели снизу.</summary>
public sealed class SelectedEdgeItem
{
    public NavEdge Edge          { get; init; } = null!;
    public string  OtherNodeName { get; init; } = "";
}
