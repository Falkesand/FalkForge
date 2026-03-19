using System.Collections.ObjectModel;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.CustomActionsEditor;

public sealed class CustomActionsEditorViewModel : ViewModelBase
{
    private readonly StudioProject _project;
    private CustomActionEntryViewModel? _selectedEntry;

    public static string[] Types { get; } = ["DllFromBinary", "ExeFromBinary", "SetProperty"];

    public ObservableCollection<CustomActionEntryViewModel> Entries { get; } = [];

    public CustomActionEntryViewModel? SelectedEntry
    {
        get => _selectedEntry;
        set => SetProperty(ref _selectedEntry, value);
    }

    public CustomActionsEditorViewModel(StudioProject project)
    {
        _project = project;
        LoadEntries();
    }

    private void LoadEntries()
    {
        Entries.Clear();
        foreach (var ca in _project.CustomActions)
            Entries.Add(new CustomActionEntryViewModel(ca));
    }

    public void AddEntry()
    {
        var section = new CustomActionSection();
        _project.CustomActions.Add(section);
        var vm = new CustomActionEntryViewModel(section);
        Entries.Add(vm);
        SelectedEntry = vm;
    }

    public void RemoveSelected()
    {
        if (SelectedEntry is null) return;

        _project.CustomActions.Remove(SelectedEntry.Model);
        Entries.Remove(SelectedEntry);
        SelectedEntry = Entries.Count > 0 ? Entries[0] : null;
    }
}
