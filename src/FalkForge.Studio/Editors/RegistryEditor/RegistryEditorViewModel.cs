using System.Collections.ObjectModel;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.RegistryEditor;

public sealed class RegistryEditorViewModel : ViewModelBase
{
    private readonly StudioProject _project;
    private RegistryEntryViewModel? _selectedEntry;

    public ObservableCollection<RegistryEntryViewModel> Entries { get; } = [];

    public static IReadOnlyList<string> Roots { get; } = ["LocalMachine", "CurrentUser", "ClassesRoot", "Users"];
    public static IReadOnlyList<string> ValueTypes { get; } = ["String", "ExpandString", "MultiString", "DWord", "QWord", "Binary"];

    public RegistryEntryViewModel? SelectedEntry
    {
        get => _selectedEntry;
        set => SetProperty(ref _selectedEntry, value);
    }

    public RegistryEditorViewModel(StudioProject project)
    {
        _project = project;
        LoadEntries();
    }

    private void LoadEntries()
    {
        Entries.Clear();
        foreach (var entry in _project.Registry)
            Entries.Add(new RegistryEntryViewModel(entry));
    }

    public void AddEntry()
    {
        var section = new RegistryEntrySection();
        _project.Registry.Add(section);
        var vm = new RegistryEntryViewModel(section);
        Entries.Add(vm);
        SelectedEntry = vm;
    }

    public void RemoveSelected()
    {
        if (SelectedEntry is null) return;

        _project.Registry.Remove(SelectedEntry.Model);
        Entries.Remove(SelectedEntry);
        SelectedEntry = Entries.Count > 0 ? Entries[0] : null;
    }
}
