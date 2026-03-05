using FalkForge.Studio.Editors.OdbcEditor;
using FalkForge.Studio.Project;
using Xunit;

namespace FalkForge.Studio.Tests.Editors;

public class OdbcEditorViewModelTests
{
    private static StudioProject CreateProjectWithEntries()
    {
        var project = StudioProjectLoader.NewProject();
        project.OdbcDrivers.Add(new OdbcDriverSection
        {
            Id = "DRV1",
            DriverName = "MySQL ODBC",
            FileName = "myodbc.dll"
        });
        project.OdbcDataSources.Add(new OdbcDataSourceSection
        {
            Id = "DSN1",
            Name = "MyDatabase",
            DriverName = "MySQL ODBC",
            Registration = "PerMachine"
        });
        return project;
    }

    [Fact]
    public void Constructor_LoadsDriversAndDataSources()
    {
        var project = CreateProjectWithEntries();
        var vm = new OdbcEditorViewModel(project);
        Assert.Single(vm.Drivers);
        Assert.Single(vm.DataSources);
    }

    [Fact]
    public void AddDriver_AddsToCollectionAndModel()
    {
        var project = StudioProjectLoader.NewProject();
        var vm = new OdbcEditorViewModel(project);
        vm.AddDriver();
        Assert.Single(vm.Drivers);
        Assert.Single(project.OdbcDrivers);
        Assert.Equal(vm.Drivers[0], vm.SelectedDriver);
    }

    [Fact]
    public void RemoveSelectedDriver_RemovesDriver()
    {
        var project = CreateProjectWithEntries();
        var vm = new OdbcEditorViewModel(project);
        vm.SelectedDriver = vm.Drivers[0];
        vm.RemoveSelectedDriver();
        Assert.Empty(vm.Drivers);
        Assert.Empty(project.OdbcDrivers);
    }

    [Fact]
    public void AddDataSource_AddsToCollectionAndModel()
    {
        var project = StudioProjectLoader.NewProject();
        var vm = new OdbcEditorViewModel(project);
        vm.AddDataSource();
        Assert.Single(vm.DataSources);
        Assert.Single(project.OdbcDataSources);
        Assert.Equal(vm.DataSources[0], vm.SelectedDataSource);
    }

    [Fact]
    public void RemoveSelectedDriver_NullSelected_NoOp()
    {
        var project = CreateProjectWithEntries();
        var vm = new OdbcEditorViewModel(project);
        vm.SelectedDriver = null;
        vm.RemoveSelectedDriver();
        Assert.Single(vm.Drivers);
    }

    [Fact]
    public void RemoveSelectedDataSource_NullSelected_NoOp()
    {
        var project = CreateProjectWithEntries();
        var vm = new OdbcEditorViewModel(project);
        vm.SelectedDataSource = null;
        vm.RemoveSelectedDataSource();
        Assert.Single(vm.DataSources);
    }

    [Fact]
    public void DataSourceViewModel_PropertyChange_UpdatesModel()
    {
        var model = new OdbcDataSourceSection { Id = "DSN1", Name = "MyDB" };
        var vm = new OdbcDataSourceEntryViewModel(model);
        vm.Name = "ProdDB";
        Assert.Equal("ProdDB", model.Name);
    }
}
