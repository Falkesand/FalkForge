using System.Collections.ObjectModel;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.PerfCountersEditor;

public sealed class PerfCountersEditorViewModel : ViewModelBase
{
    private readonly StudioProject _project;
    private PerfCounterEntryViewModel? _selectedEntry;

    public ObservableCollection<PerfCounterEntryViewModel> Entries { get; } = [];

    public static IReadOnlyList<string> CounterTypes { get; } = ["NumberOfItems32", "NumberOfItems64", "RateOfCountsPerSecond32", "RateOfCountsPerSecond64", "AverageTimer32", "AverageCount64"];

    public PerfCounterEntryViewModel? SelectedEntry
    {
        get => _selectedEntry;
        set => SetProperty(ref _selectedEntry, value);
    }

    public PerfCountersEditorViewModel(StudioProject project)
    {
        _project = project;
        LoadEntries();
    }

    private void LoadEntries()
    {
        Entries.Clear();
        foreach (var item in _project.PerfCounters)
            Entries.Add(new PerfCounterEntryViewModel(item));
    }

    public void AddEntry()
    {
        var section = new PerfCounterSection();
        _project.PerfCounters.Add(section);
        var vm = new PerfCounterEntryViewModel(section);
        Entries.Add(vm);
        SelectedEntry = vm;
    }

    public void RemoveSelected()
    {
        if (SelectedEntry is null) return;
        _project.PerfCounters.Remove(SelectedEntry.Model);
        Entries.Remove(SelectedEntry);
        SelectedEntry = Entries.Count > 0 ? Entries[0] : null;
    }
}
