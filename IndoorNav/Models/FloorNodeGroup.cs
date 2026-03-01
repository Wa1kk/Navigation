using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace IndoorNav.Models;

/// <summary>
/// A floor section in the node-picker popup.
/// Supports collapse/expand (IsExpanded) and notifies bindings via INotifyPropertyChanged.
/// </summary>
public class FloorNodeGroup : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private bool _isExpanded;
    private readonly List<NavNode> _allNodes;

    /// <summary>Display name shown in the section header.</summary>
    public string FloorName { get; }

    /// <summary>Full node list regardless of expansion state.</summary>
    public IReadOnlyList<NavNode> AllNodes => _allNodes;

    /// <summary>Returns all nodes when expanded, an empty enumerable when collapsed.</summary>
    public IEnumerable<NavNode> VisibleNodes =>
        _isExpanded ? (IEnumerable<NavNode>)_allNodes : Array.Empty<NavNode>();

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VisibleNodes));
            OnPropertyChanged(nameof(ChevronText));
        }
    }

    /// <summary>▼ when expanded, ▶ when collapsed — used by the header row.</summary>
    public string ChevronText => _isExpanded ? "▼" : "▶";

    /// <summary>Toggles <see cref="IsExpanded"/>. Bound to the header row tap.</summary>
    public ICommand ToggleExpandCommand { get; }

    public FloorNodeGroup(string floorName, IEnumerable<NavNode> nodes, bool isExpanded = false)
    {
        FloorName  = floorName;
        _allNodes  = nodes.ToList();
        _isExpanded = isExpanded;
        ToggleExpandCommand = new Command(() => IsExpanded = !IsExpanded);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
