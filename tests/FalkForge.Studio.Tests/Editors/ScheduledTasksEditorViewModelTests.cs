using FalkForge.Studio.Editors.ScheduledTasksEditor;
using FalkForge.Studio.Project;
using Xunit;

namespace FalkForge.Studio.Tests.Editors;

public class ScheduledTasksEditorViewModelTests
{
    private static StudioProject CreateProjectWithEntries()
    {
        var project = StudioProjectLoader.NewProject();
        project.ScheduledTasks.Add(new ScheduledTaskSection
        {
            Id = "ST1",
            Name = "Backup",
            Command = "backup.exe",
            TriggerType = "OnSchedule"
        });
        project.ScheduledTasks.Add(new ScheduledTaskSection
        {
            Id = "ST2",
            Name = "Cleanup",
            Command = "cleanup.exe",
            TriggerType = "OnInstall"
        });
        return project;
    }

    [Fact]
    public void Constructor_LoadsEntries()
    {
        var project = CreateProjectWithEntries();
        var vm = new ScheduledTasksEditorViewModel(project);
        Assert.Equal(2, vm.Entries.Count);
    }

    [Fact]
    public void AddEntry_AddsToCollectionAndModel()
    {
        var project = StudioProjectLoader.NewProject();
        var vm = new ScheduledTasksEditorViewModel(project);
        vm.AddEntry();
        Assert.Single(vm.Entries);
        Assert.Single(project.ScheduledTasks);
        Assert.Equal(vm.Entries[0], vm.SelectedEntry);
    }

    [Fact]
    public void RemoveSelected_RemovesEntry()
    {
        var project = CreateProjectWithEntries();
        var vm = new ScheduledTasksEditorViewModel(project);
        vm.SelectedEntry = vm.Entries[0];
        vm.RemoveSelected();
        Assert.Single(vm.Entries);
        Assert.Single(project.ScheduledTasks);
        Assert.Equal("ST2", vm.Entries[0].Id);
    }

    [Fact]
    public void RemoveSelected_NullSelected_NoOp()
    {
        var project = CreateProjectWithEntries();
        var vm = new ScheduledTasksEditorViewModel(project);
        vm.SelectedEntry = null;
        vm.RemoveSelected();
        Assert.Equal(2, vm.Entries.Count);
    }

    [Fact]
    public void EntryViewModel_PropertyChange_UpdatesModel()
    {
        var model = new ScheduledTaskSection { Id = "ST1", Name = "Backup" };
        var vm = new ScheduledTaskEntryViewModel(model);
        vm.Name = "DailyBackup";
        Assert.Equal("DailyBackup", model.Name);
    }
}
