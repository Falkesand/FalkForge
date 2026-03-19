using FalkForge.Studio.Editors.FirewallEditor;
using FalkForge.Studio.Project;
using Xunit;

namespace FalkForge.Studio.Tests.Editors;

public class FirewallEditorViewModelTests
{
    private static StudioProject CreateProjectWithRules()
    {
        var project = StudioProjectLoader.NewProject();
        project.FirewallRules.Add(new FirewallRuleSection
        {
            Id = "FW1",
            Name = "HTTP",
            Protocol = "Tcp",
            Port = "80",
            Direction = "Inbound",
            Action = "Allow"
        });
        project.FirewallRules.Add(new FirewallRuleSection
        {
            Id = "FW2",
            Name = "HTTPS",
            Protocol = "Tcp",
            Port = "443",
            Direction = "Inbound",
            Action = "Allow"
        });
        return project;
    }

    [Fact]
    public void Constructor_LoadsRules()
    {
        var project = CreateProjectWithRules();
        var vm = new FirewallEditorViewModel(project);
        Assert.Equal(2, vm.Entries.Count);
    }

    [Fact]
    public void AddEntry_AddsToCollectionAndModel()
    {
        var project = StudioProjectLoader.NewProject();
        var vm = new FirewallEditorViewModel(project);
        vm.AddEntry();
        Assert.Single(vm.Entries);
        Assert.Single(project.FirewallRules);
        Assert.Equal(vm.Entries[0], vm.SelectedEntry);
    }

    [Fact]
    public void RemoveSelected_RemovesEntry()
    {
        var project = CreateProjectWithRules();
        var vm = new FirewallEditorViewModel(project);
        vm.SelectedEntry = vm.Entries[0];
        vm.RemoveSelected();
        Assert.Single(vm.Entries);
        Assert.Single(project.FirewallRules);
        Assert.Equal("FW2", vm.Entries[0].Id);
    }

    [Fact]
    public void RemoveSelected_NullSelected_NoOp()
    {
        var project = CreateProjectWithRules();
        var vm = new FirewallEditorViewModel(project);
        vm.SelectedEntry = null;
        vm.RemoveSelected();
        Assert.Equal(2, vm.Entries.Count);
    }

    [Fact]
    public void EntryViewModel_PropertyChange_UpdatesModel()
    {
        var model = new FirewallRuleSection { Id = "FW1", Name = "HTTP" };
        var vm = new FirewallRuleViewModel(model);
        vm.Name = "HTTPS-Alt";
        Assert.Equal("HTTPS-Alt", model.Name);
    }

    [Fact]
    public void Dropdowns_ContainExpectedValues()
    {
        Assert.Contains("Tcp", FirewallEditorViewModel.Protocols);
        Assert.Contains("Udp", FirewallEditorViewModel.Protocols);
        Assert.Contains("Any", FirewallEditorViewModel.Protocols);
        Assert.Contains("Inbound", FirewallEditorViewModel.Directions);
        Assert.Contains("Outbound", FirewallEditorViewModel.Directions);
        Assert.Contains("All", FirewallEditorViewModel.Profiles);
        Assert.Contains("Domain", FirewallEditorViewModel.Profiles);
        Assert.Contains("Private", FirewallEditorViewModel.Profiles);
        Assert.Contains("Public", FirewallEditorViewModel.Profiles);
        Assert.Contains("Allow", FirewallEditorViewModel.Actions);
        Assert.Contains("Block", FirewallEditorViewModel.Actions);
    }
}
