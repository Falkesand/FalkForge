using System.Collections.ObjectModel;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.ShortcutsEditor;

public sealed class ShortcutsEditorViewModel : ViewModelBase
{
    private readonly StudioProject _project;
    private ShortcutEntryViewModel? _selectedEntry;

    public ObservableCollection<ShortcutEntryViewModel> Entries { get; } = [];

    public ShortcutEntryViewModel? SelectedEntry
    {
        get => _selectedEntry;
        set => SetProperty(ref _selectedEntry, value);
    }

    public ShortcutsEditorViewModel(StudioProject project)
    {
        _project = project;
        LoadEntries();
    }

    private void LoadEntries()
    {
        Entries.Clear();
        foreach (var shortcut in _project.Shortcuts)
            Entries.Add(new ShortcutEntryViewModel(shortcut));
    }

    public void AddEntry()
    {
        var section = new ShortcutSection();
        _project.Shortcuts.Add(section);
        var vm = new ShortcutEntryViewModel(section);
        Entries.Add(vm);
        SelectedEntry = vm;
    }

    public void RemoveSelected()
    {
        if (SelectedEntry is null) return;

        _project.Shortcuts.Remove(SelectedEntry.Model);
        Entries.Remove(SelectedEntry);
        SelectedEntry = Entries.Count > 0 ? Entries[0] : null;
    }
}
