using System.ComponentModel;
using System.Windows.Input;
using IndoorNav.Models;

namespace IndoorNav.ViewModels;

/// <summary>Wrapper-ViewModel для одного этажа в селекторе этажей на карте.</summary>
public class FloorSelectorVm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public Floor Floor { get; }

    /// <summary>Команда выбора этого этажа — устанавливается родительским VM.</summary>
    public ICommand TapCommand { get; set; } = new Command(() => { });

    /// <summary>Число для отображения: "-1", "1", "2" и т.д.</summary>
    public string Display => Floor.Number.ToString();

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CircleColor)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TextColor)));
        }
    }

    /// <summary>Цвет фона кружка: синий для выбранного, прозрачный для остальных.</summary>
    public Color CircleColor => _isSelected ? Color.FromArgb("#2563EB") : Colors.Transparent;

    /// <summary>Цвет текста: белый для выбранного, тёмно-серый для остальных.</summary>
    public Color TextColor => _isSelected ? Colors.White : Color.FromArgb("#475569");

    public FloorSelectorVm(Floor floor) => Floor = floor;
}

/// <summary>Wrapper-ViewModel для строки в списке выбора корпуса.</summary>
public class BuildingPickerItemVm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public Building Building { get; }

    /// <summary>Команда выбора этого корпуса — устанавливается родительским VM.</summary>
    public ICommand TapCommand { get; set; } = new Command(() => { });

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RowBgColor)));
        }
    }

    /// <summary>Цвет фона строки: светло-голубой для выбранного корпуса.</summary>
    public Color RowBgColor => _isSelected ? Color.FromArgb("#eff6ffe8") : Colors.White;

    public BuildingPickerItemVm(Building building) => Building = building;
}
