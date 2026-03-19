using System.Collections.ObjectModel;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.OdbcEditor;

public sealed class OdbcEditorViewModel : ViewModelBase
{
    private readonly StudioProject _project;
    private OdbcDriverEntryViewModel? _selectedDriver;
    private OdbcDataSourceEntryViewModel? _selectedDataSource;

    public ObservableCollection<OdbcDriverEntryViewModel> Drivers { get; } = [];
    public ObservableCollection<OdbcDataSourceEntryViewModel> DataSources { get; } = [];

    public static IReadOnlyList<string> Registrations { get; } = ["PerMachine", "PerUser"];

    public OdbcDriverEntryViewModel? SelectedDriver
    {
        get => _selectedDriver;
        set => SetProperty(ref _selectedDriver, value);
    }

    public OdbcDataSourceEntryViewModel? SelectedDataSource
    {
        get => _selectedDataSource;
        set => SetProperty(ref _selectedDataSource, value);
    }

    public OdbcEditorViewModel(StudioProject project)
    {
        _project = project;
        LoadEntries();
    }

    private void LoadEntries()
    {
        Drivers.Clear();
        foreach (var item in _project.OdbcDrivers)
            Drivers.Add(new OdbcDriverEntryViewModel(item));

        DataSources.Clear();
        foreach (var item in _project.OdbcDataSources)
            DataSources.Add(new OdbcDataSourceEntryViewModel(item));
    }

    public void AddDriver()
    {
        var section = new OdbcDriverSection();
        _project.OdbcDrivers.Add(section);
        var vm = new OdbcDriverEntryViewModel(section);
        Drivers.Add(vm);
        SelectedDriver = vm;
    }

    public void RemoveSelectedDriver()
    {
        if (SelectedDriver is null) return;
        _project.OdbcDrivers.Remove(SelectedDriver.Model);
        Drivers.Remove(SelectedDriver);
        SelectedDriver = Drivers.Count > 0 ? Drivers[0] : null;
    }

    public void AddDataSource()
    {
        var section = new OdbcDataSourceSection();
        _project.OdbcDataSources.Add(section);
        var vm = new OdbcDataSourceEntryViewModel(section);
        DataSources.Add(vm);
        SelectedDataSource = vm;
    }

    public void RemoveSelectedDataSource()
    {
        if (SelectedDataSource is null) return;
        _project.OdbcDataSources.Remove(SelectedDataSource.Model);
        DataSources.Remove(SelectedDataSource);
        SelectedDataSource = DataSources.Count > 0 ? DataSources[0] : null;
    }
}
