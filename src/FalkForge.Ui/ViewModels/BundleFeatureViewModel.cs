using System.ComponentModel;
using FalkForge.Engine.Protocol;

namespace FalkForge.Ui.ViewModels;

/// <summary>A selectable bundle feature. Required features cannot be cleared.</summary>
public sealed class BundleFeatureViewModel : INotifyPropertyChanged
{
    private readonly Action<string, bool> _onChanged;
    private bool _isSelected;

    internal BundleFeatureViewModel(FeatureState feature, bool canChange, Action<string, bool> onChanged)
    {
        FeatureId = feature.FeatureId;
        Title = feature.Title;
        Description = feature.Description;
        DiskSpaceRequired = feature.DiskSpaceRequired;
        IsRequired = feature.IsRequired;
        IsEnabled = canChange && !IsRequired;
        _isSelected = IsRequired || feature.IsSelected;
        _onChanged = onChanged;
    }

    public string FeatureId { get; }
    public string Title { get; }
    public string? Description { get; }
    public long DiskSpaceRequired { get; }
    public bool IsRequired { get; }
    public bool IsEnabled { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!IsEnabled || _isSelected == value)
                return;
            _onChanged(FeatureId, value);
            _isSelected = value;
            PropertyChanged?.Invoke(this, SelectionChanged);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private static readonly PropertyChangedEventArgs SelectionChanged = new(nameof(IsSelected));
}
