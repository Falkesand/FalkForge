using System.ComponentModel;

namespace FalkForge.Ui.ViewModels;

/// <summary>
/// A single selectable node in a per-package MSI feature tree. Wraps one visible
/// <c>Feature</c> row: a display label, its children, and a two-way bindable
/// <see cref="IsChecked"/> state. Toggling <see cref="IsChecked"/> raises
/// <see cref="INotifyPropertyChanged.PropertyChanged"/> (so the checkbox stays in sync) and
/// invokes the toggle callback supplied at construction so the owning section can recompute and
/// send the package's selection.
/// </summary>
public sealed class PackageFeatureNodeViewModel : INotifyPropertyChanged
{
    private readonly Action<PackageFeatureNodeViewModel> _onToggled;
    private bool _isChecked;

    internal PackageFeatureNodeViewModel(
        string featureId,
        string displayName,
        string? description,
        IReadOnlyList<PackageFeatureNodeViewModel> children,
        bool isChecked,
        Action<PackageFeatureNodeViewModel> onToggled)
    {
        FeatureId = featureId;
        DisplayName = displayName;
        Description = description;
        Children = children;
        _isChecked = isChecked;
        _onToggled = onToggled;
    }

    /// <summary>The MSI <c>Feature</c> primary key this node represents.</summary>
    public string FeatureId { get; }

    /// <summary>Label shown in the tree — the feature's title, or its id when untitled.</summary>
    public string DisplayName { get; }

    /// <summary>Optional longer description surfaced as a tooltip / secondary text.</summary>
    public string? Description { get; }

    /// <summary>Child feature nodes nested under this one.</summary>
    public IReadOnlyList<PackageFeatureNodeViewModel> Children { get; }

    /// <summary>
    /// Whether this feature is selected for install. Setting it raises change notification and
    /// notifies the owning section so it can push the updated selection to the engine.
    /// </summary>
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value)
                return;
            _isChecked = value;
            PropertyChanged?.Invoke(this, IsCheckedChangedArgs);
            _onToggled(this);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private static readonly PropertyChangedEventArgs IsCheckedChangedArgs = new(nameof(IsChecked));
}
