using System.Collections.ObjectModel;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.EnvironmentEditor;

public sealed class EnvironmentEditorViewModel : ViewModelBase
{
    private readonly StudioProject _project;
    private EnvironmentEntryViewModel? _selectedEntry;

    public static string[] Actions { get; } = ["Set", "Append", "Prepend"];

    public ObservableCollection<EnvironmentEntryViewModel> Entries { get; } = [];

    public EnvironmentEntryViewModel? SelectedEntry
    {
        get => _selectedEntry;
        set => SetProperty(ref _selectedEntry, value);
    }

    public EnvironmentEditorViewModel(StudioProject project)
    {
        _project = project;
        LoadEntries();
    }

    private void LoadEntries()
    {
        Entries.Clear();
        foreach (var env in _project.Environment)
            Entries.Add(new EnvironmentEntryViewModel(env));
    }

    public void AddEntry()
    {
        var section = new EnvironmentVariableSection();
        _project.Environment.Add(section);
        var vm = new EnvironmentEntryViewModel(section);
        Entries.Add(vm);
        SelectedEntry = vm;
    }

    public void RemoveSelected()
    {
        if (SelectedEntry is null) return;

        _project.Environment.Remove(SelectedEntry.Model);
        Entries.Remove(SelectedEntry);
        SelectedEntry = Entries.Count > 0 ? Entries[0] : null;
    }
}
