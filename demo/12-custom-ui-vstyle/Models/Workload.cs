using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CustomUiVsStyle.Models;

public sealed class Workload : INotifyPropertyChanged
{
    private bool _isSelected;
    private double _progress;

    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Size { get; init; }
    public required List<WorkloadComponent> Components { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            _isSelected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedSize));
        }
    }

    public string SelectedSize => IsSelected ? Size : "0 MB";

    public double Progress
    {
        get => _progress;
        set
        {
            _progress = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}