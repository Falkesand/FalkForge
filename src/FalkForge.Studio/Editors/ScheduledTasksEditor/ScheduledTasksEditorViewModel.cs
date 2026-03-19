using System.Collections.ObjectModel;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.ScheduledTasksEditor;

public sealed class ScheduledTasksEditorViewModel : ViewModelBase
{
    private readonly StudioProject _project;
    private ScheduledTaskEntryViewModel? _selectedEntry;

    public ObservableCollection<ScheduledTaskEntryViewModel> Entries { get; } = [];

    public static IReadOnlyList<string> TriggerTypes { get; } = ["OnInstall", "OnLogin", "OnSchedule", "OnBoot"];

    public ScheduledTaskEntryViewModel? SelectedEntry
    {
        get => _selectedEntry;
        set => SetProperty(ref _selectedEntry, value);
    }

    public ScheduledTasksEditorViewModel(StudioProject project)
    {
        _project = project;
        LoadEntries();
    }

    private void LoadEntries()
    {
        Entries.Clear();
        foreach (var item in _project.ScheduledTasks)
            Entries.Add(new ScheduledTaskEntryViewModel(item));
    }

    public void AddEntry()
    {
        var section = new ScheduledTaskSection();
        _project.ScheduledTasks.Add(section);
        var vm = new ScheduledTaskEntryViewModel(section);
        Entries.Add(vm);
        SelectedEntry = vm;
    }

    public void RemoveSelected()
    {
        if (SelectedEntry is null) return;
        _project.ScheduledTasks.Remove(SelectedEntry.Model);
        Entries.Remove(SelectedEntry);
        SelectedEntry = Entries.Count > 0 ? Entries[0] : null;
    }
}
