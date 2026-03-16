using System.Collections.ObjectModel;
using System.Windows.Input;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.BundleChain;

public sealed class BundleChainViewModel : ViewModelBase
{
    private readonly StudioProject _project;
    private ChainItemViewModel? _selectedItem;

    public static readonly string[] PackageTypes =
        ["MsiPackage", "ExePackage", "NetRuntime", "MsuPackage", "MspPackage", "BundlePackage"];

    public ObservableCollection<ChainItemViewModel> ChainItems { get; } = [];

    public ChainItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set => SetProperty(ref _selectedItem, value);
    }

    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand AddRollbackBoundaryCommand { get; }
    public ICommand RemoveItemCommand { get; }

    public BundleChainViewModel(StudioProject project)
    {
        _project = project;
        MoveUpCommand = new RelayCommand(MoveUp, CanMoveUp);
        MoveDownCommand = new RelayCommand(MoveDown, CanMoveDown);
        AddRollbackBoundaryCommand = new RelayCommand(AddRollbackBoundary);
        RemoveItemCommand = new RelayCommand(RemoveItem, () => SelectedItem is not null);
        LoadFromProject();
    }

    private void LoadFromProject()
    {
        ChainItems.Clear();
        for (var i = 0; i < _project.BundlePackages.Count; i++)
        {
            var item = new ChainItemViewModel(_project.BundlePackages[i]) { DisplayOrder = i };
            ChainItems.Add(item);
        }
    }

    private bool CanMoveUp()
    {
        if (SelectedItem is null) return false;
        var index = ChainItems.IndexOf(SelectedItem);
        return index > 0;
    }

    private bool CanMoveDown()
    {
        if (SelectedItem is null) return false;
        var index = ChainItems.IndexOf(SelectedItem);
        return index >= 0 && index < ChainItems.Count - 1;
    }

    public void MoveUp()
    {
        if (SelectedItem is null) return;
        var index = ChainItems.IndexOf(SelectedItem);
        if (index <= 0) return;

        ChainItems.Move(index, index - 1);
        SyncToProject();
    }

    public void MoveDown()
    {
        if (SelectedItem is null) return;
        var index = ChainItems.IndexOf(SelectedItem);
        if (index < 0 || index >= ChainItems.Count - 1) return;

        ChainItems.Move(index, index + 1);
        SyncToProject();
    }

    public void AddRollbackBoundary()
    {
        var boundary = ChainItemViewModel.CreateRollbackBoundary();

        if (SelectedItem is not null)
        {
            var index = ChainItems.IndexOf(SelectedItem) + 1;
            boundary.DisplayOrder = index;
            ChainItems.Insert(index, boundary);
        }
        else
        {
            boundary.DisplayOrder = ChainItems.Count;
            ChainItems.Add(boundary);
        }

        SelectedItem = boundary;
        UpdateDisplayOrders();
    }

    public void RemoveItem()
    {
        if (SelectedItem is null) return;

        var index = ChainItems.IndexOf(SelectedItem);
        var item = SelectedItem;

        ChainItems.Remove(item);

        if (!item.IsRollbackBoundary && item.Model is not null)
            _project.BundlePackages.Remove(item.Model);

        SelectedItem = ChainItems.Count > 0
            ? ChainItems[Math.Min(index, ChainItems.Count - 1)]
            : null;

        UpdateDisplayOrders();
    }

    private void SyncToProject()
    {
        _project.BundlePackages.Clear();
        foreach (var item in ChainItems)
        {
            if (!item.IsRollbackBoundary && item.Model is not null)
                _project.BundlePackages.Add(item.Model);
        }

        UpdateDisplayOrders();
    }

    private void UpdateDisplayOrders()
    {
        for (var i = 0; i < ChainItems.Count; i++)
            ChainItems[i].DisplayOrder = i;
    }
}
