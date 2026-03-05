using System.Collections.ObjectModel;

namespace FalkForge.Studio.Navigation;

public sealed class TreeNodeViewModel : ViewModelBase
{
    private bool _isSelected;
    private bool _isExpanded;

    public string Label { get; }
    public string NodeKey { get; }
    public ObservableCollection<TreeNodeViewModel> Children { get; } = [];

    public TreeNodeViewModel(string label, string nodeKey)
    {
        Label = label;
        NodeKey = nodeKey;
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }
}
