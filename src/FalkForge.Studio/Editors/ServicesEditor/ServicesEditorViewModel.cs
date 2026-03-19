using System.Collections.ObjectModel;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.ServicesEditor;

public sealed class ServicesEditorViewModel : ViewModelBase
{
    private readonly StudioProject _project;
    private ServiceEntryViewModel? _selectedEntry;

    public ObservableCollection<ServiceEntryViewModel> Entries { get; } = [];

    public static IReadOnlyList<string> StartModes { get; } = ["Automatic", "Manual", "Disabled", "DelayedAutomatic"];
    public static IReadOnlyList<string> Accounts { get; } = ["LocalSystem", "LocalService", "NetworkService", "User"];

    public ServiceEntryViewModel? SelectedEntry
    {
        get => _selectedEntry;
        set => SetProperty(ref _selectedEntry, value);
    }

    public ServicesEditorViewModel(StudioProject project)
    {
        _project = project;
        LoadEntries();
    }

    private void LoadEntries()
    {
        Entries.Clear();
        foreach (var svc in _project.Services)
            Entries.Add(new ServiceEntryViewModel(svc));
    }

    public void AddEntry()
    {
        var section = new ServiceSection();
        _project.Services.Add(section);
        var vm = new ServiceEntryViewModel(section);
        Entries.Add(vm);
        SelectedEntry = vm;
    }

    public void RemoveSelected()
    {
        if (SelectedEntry is null) return;

        _project.Services.Remove(SelectedEntry.Model);
        Entries.Remove(SelectedEntry);
        SelectedEntry = Entries.Count > 0 ? Entries[0] : null;
    }
}
