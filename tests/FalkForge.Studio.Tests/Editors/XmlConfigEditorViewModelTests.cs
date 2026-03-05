using FalkForge.Studio.Editors.XmlConfigEditor;
using FalkForge.Studio.Project;
using Xunit;

namespace FalkForge.Studio.Tests.Editors;

public class XmlConfigEditorViewModelTests
{
    private static StudioProject CreateProjectWithEntries()
    {
        var project = StudioProjectLoader.NewProject();
        project.XmlConfigs.Add(new XmlConfigSection
        {
            Id = "XC1",
            FilePath = "web.config",
            XPath = "//appSettings",
            Action = "SetAttribute"
        });
        project.XmlConfigs.Add(new XmlConfigSection
        {
            Id = "XC2",
            FilePath = "app.config",
            XPath = "//connectionStrings",
            Action = "SetValue"
        });
        return project;
    }

    [Fact]
    public void Constructor_LoadsEntries()
    {
        var project = CreateProjectWithEntries();
        var vm = new XmlConfigEditorViewModel(project);
        Assert.Equal(2, vm.Entries.Count);
    }

    [Fact]
    public void AddEntry_AddsToCollectionAndModel()
    {
        var project = StudioProjectLoader.NewProject();
        var vm = new XmlConfigEditorViewModel(project);
        vm.AddEntry();
        Assert.Single(vm.Entries);
        Assert.Single(project.XmlConfigs);
        Assert.Equal(vm.Entries[0], vm.SelectedEntry);
    }

    [Fact]
    public void RemoveSelected_RemovesEntry()
    {
        var project = CreateProjectWithEntries();
        var vm = new XmlConfigEditorViewModel(project);
        vm.SelectedEntry = vm.Entries[0];
        vm.RemoveSelected();
        Assert.Single(vm.Entries);
        Assert.Single(project.XmlConfigs);
        Assert.Equal("XC2", vm.Entries[0].Id);
    }

    [Fact]
    public void RemoveSelected_NullSelected_NoOp()
    {
        var project = CreateProjectWithEntries();
        var vm = new XmlConfigEditorViewModel(project);
        vm.SelectedEntry = null;
        vm.RemoveSelected();
        Assert.Equal(2, vm.Entries.Count);
    }

    [Fact]
    public void EntryViewModel_PropertyChange_UpdatesModel()
    {
        var model = new XmlConfigSection { Id = "XC1", FilePath = "web.config" };
        var vm = new XmlConfigEntryViewModel(model);
        vm.FilePath = "app.config";
        Assert.Equal("app.config", model.FilePath);
    }
}
