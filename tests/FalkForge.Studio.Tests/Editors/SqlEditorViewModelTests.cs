using FalkForge.Studio.Editors.SqlEditor;
using FalkForge.Studio.Project;
using Xunit;

namespace FalkForge.Studio.Tests.Editors;

public class SqlEditorViewModelTests
{
    private static StudioProject CreateProjectWithDatabases()
    {
        var project = StudioProjectLoader.NewProject();
        project.SqlDatabases.Add(new SqlDatabaseSection
        {
            Id = "DB1",
            Database = "AppDb",
            Scripts = [new SqlScriptSection { Id = "S1", SourceFile = "init.sql" }]
        });
        project.SqlDatabases.Add(new SqlDatabaseSection
        {
            Id = "DB2",
            Database = "LogDb"
        });
        return project;
    }

    [Fact]
    public void Constructor_LoadsDatabases()
    {
        var project = CreateProjectWithDatabases();
        var vm = new SqlEditorViewModel(project);
        Assert.Equal(2, vm.Databases.Count);
    }

    [Fact]
    public void AddDatabase_AddsToCollectionAndModel()
    {
        var project = StudioProjectLoader.NewProject();
        var vm = new SqlEditorViewModel(project);
        vm.AddDatabase();
        Assert.Single(vm.Databases);
        Assert.Single(project.SqlDatabases);
        Assert.Equal(vm.Databases[0], vm.SelectedDatabase);
    }

    [Fact]
    public void RemoveSelectedDatabase_RemovesDatabase()
    {
        var project = CreateProjectWithDatabases();
        var vm = new SqlEditorViewModel(project);
        vm.SelectedDatabase = vm.Databases[0];
        vm.RemoveSelectedDatabase();
        Assert.Single(vm.Databases);
        Assert.Single(project.SqlDatabases);
        Assert.Equal("DB2", vm.Databases[0].Id);
    }

    [Fact]
    public void AddScript_AddsToSelectedDatabase()
    {
        var project = CreateProjectWithDatabases();
        var vm = new SqlEditorViewModel(project);
        vm.SelectedDatabase = vm.Databases[1]; // DB2, no scripts
        vm.AddScript();
        Assert.Single(vm.SelectedDatabase.Scripts);
        Assert.Single(project.SqlDatabases[1].Scripts);
    }

    [Fact]
    public void RemoveSelectedScript_RemovesScript()
    {
        var project = CreateProjectWithDatabases();
        var vm = new SqlEditorViewModel(project);
        vm.SelectedDatabase = vm.Databases[0]; // DB1, has 1 script
        vm.SelectedDatabase.SelectedScript = vm.SelectedDatabase.Scripts[0];
        vm.RemoveSelectedScript();
        Assert.Empty(vm.SelectedDatabase.Scripts);
        Assert.Empty(project.SqlDatabases[0].Scripts);
    }

    [Fact]
    public void DatabaseViewModel_PropertyChange_UpdatesModel()
    {
        var model = new SqlDatabaseSection { Id = "DB1", Database = "OldName" };
        var vm = new SqlDatabaseViewModel(model);
        vm.Database = "NewName";
        Assert.Equal("NewName", model.Database);
    }
}
