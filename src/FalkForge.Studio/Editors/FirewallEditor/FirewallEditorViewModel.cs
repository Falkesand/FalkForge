using System.Collections.ObjectModel;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.FirewallEditor;

public sealed class FirewallEditorViewModel : ViewModelBase
{
    private readonly StudioProject _project;
    private FirewallRuleViewModel? _selectedEntry;

    public ObservableCollection<FirewallRuleViewModel> Entries { get; } = [];

    public static IReadOnlyList<string> Protocols { get; } = ["Tcp", "Udp", "Any"];
    public static IReadOnlyList<string> Directions { get; } = ["Inbound", "Outbound"];
    public static IReadOnlyList<string> Profiles { get; } = ["All", "Domain", "Private", "Public"];
    public static IReadOnlyList<string> Actions { get; } = ["Allow", "Block"];

    public FirewallRuleViewModel? SelectedEntry
    {
        get => _selectedEntry;
        set => SetProperty(ref _selectedEntry, value);
    }

    public FirewallEditorViewModel(StudioProject project)
    {
        _project = project;
        LoadEntries();
    }

    private void LoadEntries()
    {
        Entries.Clear();
        foreach (var rule in _project.FirewallRules)
            Entries.Add(new FirewallRuleViewModel(rule));
    }

    public void AddEntry()
    {
        var section = new FirewallRuleSection();
        _project.FirewallRules.Add(section);
        var vm = new FirewallRuleViewModel(section);
        Entries.Add(vm);
        SelectedEntry = vm;
    }

    public void RemoveSelected()
    {
        if (SelectedEntry is null) return;
        _project.FirewallRules.Remove(SelectedEntry.Model);
        Entries.Remove(SelectedEntry);
        SelectedEntry = Entries.Count > 0 ? Entries[0] : null;
    }
}
