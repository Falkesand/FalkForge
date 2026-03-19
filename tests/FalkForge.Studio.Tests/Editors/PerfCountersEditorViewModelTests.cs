using FalkForge.Studio.Editors.PerfCountersEditor;
using FalkForge.Studio.Project;
using Xunit;

namespace FalkForge.Studio.Tests.Editors;

public class PerfCountersEditorViewModelTests
{
    private static StudioProject CreateProjectWithEntries()
    {
        var project = StudioProjectLoader.NewProject();
        project.PerfCounters.Add(new PerfCounterSection
        {
            Id = "PC1",
            CategoryName = "MyApp",
            CounterName = "Requests",
            CounterType = "NumberOfItems32"
        });
        project.PerfCounters.Add(new PerfCounterSection
        {
            Id = "PC2",
            CategoryName = "MyApp",
            CounterName = "Errors",
            CounterType = "NumberOfItems64"
        });
        return project;
    }

    [Fact]
    public void Constructor_LoadsEntries()
    {
        var project = CreateProjectWithEntries();
        var vm = new PerfCountersEditorViewModel(project);
        Assert.Equal(2, vm.Entries.Count);
    }

    [Fact]
    public void AddEntry_AddsToCollectionAndModel()
    {
        var project = StudioProjectLoader.NewProject();
        var vm = new PerfCountersEditorViewModel(project);
        vm.AddEntry();
        Assert.Single(vm.Entries);
        Assert.Single(project.PerfCounters);
        Assert.Equal(vm.Entries[0], vm.SelectedEntry);
    }

    [Fact]
    public void RemoveSelected_RemovesEntry()
    {
        var project = CreateProjectWithEntries();
        var vm = new PerfCountersEditorViewModel(project);
        vm.SelectedEntry = vm.Entries[0];
        vm.RemoveSelected();
        Assert.Single(vm.Entries);
        Assert.Single(project.PerfCounters);
        Assert.Equal("PC2", vm.Entries[0].Id);
    }

    [Fact]
    public void RemoveSelected_NullSelected_NoOp()
    {
        var project = CreateProjectWithEntries();
        var vm = new PerfCountersEditorViewModel(project);
        vm.SelectedEntry = null;
        vm.RemoveSelected();
        Assert.Equal(2, vm.Entries.Count);
    }

    [Fact]
    public void EntryViewModel_PropertyChange_UpdatesModel()
    {
        var model = new PerfCounterSection { Id = "PC1", CounterName = "Requests" };
        var vm = new PerfCounterEntryViewModel(model);
        vm.CounterName = "TotalRequests";
        Assert.Equal("TotalRequests", model.CounterName);
    }
}
