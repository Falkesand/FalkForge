using System.Collections.ObjectModel;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.XmlConfigEditor;

public sealed class XmlConfigEditorViewModel : ViewModelBase
{
    private readonly StudioProject _project;
    private XmlConfigEntryViewModel? _selectedEntry;

    public ObservableCollection<XmlConfigEntryViewModel> Entries { get; } = [];

    public static IReadOnlyList<string> Actions { get; } = ["CreateElement", "DeleteElement", "SetAttribute", "DeleteAttribute", "SetValue", "BulkSetValue"];

    public XmlConfigEntryViewModel? SelectedEntry
    {
        get => _selectedEntry;
        set => SetProperty(ref _selectedEntry, value);
    }

    public XmlConfigEditorViewModel(StudioProject project)
    {
        _project = project;
        LoadEntries();
    }

    private void LoadEntries()
    {
        Entries.Clear();
        foreach (var item in _project.XmlConfigs)
            Entries.Add(new XmlConfigEntryViewModel(item));
    }

    public void AddEntry()
    {
        var section = new XmlConfigSection();
        _project.XmlConfigs.Add(section);
        var vm = new XmlConfigEntryViewModel(section);
        Entries.Add(vm);
        SelectedEntry = vm;
    }

    public void RemoveSelected()
    {
        if (SelectedEntry is null) return;
        _project.XmlConfigs.Remove(SelectedEntry.Model);
        Entries.Remove(SelectedEntry);
        SelectedEntry = Entries.Count > 0 ? Entries[0] : null;
    }
}
