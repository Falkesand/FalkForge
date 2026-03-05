using System.Collections.ObjectModel;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.BundlePackagesEditor;

public sealed class BundlePackagesEditorViewModel : ViewModelBase
{
    private readonly StudioProject _project;
    private BundlePackageEntryViewModel? _selectedEntry;

    public ObservableCollection<BundlePackageEntryViewModel> Entries { get; } = [];

    public static readonly string[] PackageTypes =
        ["MsiPackage", "ExePackage", "NetRuntime", "MsuPackage", "MspPackage", "BundlePackage"];

    public static readonly string[] DetectionModes = ["Default", "SearchOnly", "Combined"];

    public BundlePackageEntryViewModel? SelectedEntry
    {
        get => _selectedEntry;
        set => SetProperty(ref _selectedEntry, value);
    }

    public BundlePackagesEditorViewModel(StudioProject project)
    {
        _project = project;
        LoadEntries();
    }

    private void LoadEntries()
    {
        Entries.Clear();
        foreach (var pkg in _project.BundlePackages)
            Entries.Add(new BundlePackageEntryViewModel(pkg));
    }

    public void AddEntry()
    {
        var section = new BundlePackageSection();
        _project.BundlePackages.Add(section);
        var vm = new BundlePackageEntryViewModel(section);
        Entries.Add(vm);
        SelectedEntry = vm;
    }

    public void RemoveSelected()
    {
        if (SelectedEntry is null) return;

        _project.BundlePackages.Remove(SelectedEntry.Model);
        Entries.Remove(SelectedEntry);
        SelectedEntry = Entries.Count > 0 ? Entries[0] : null;
    }
}
