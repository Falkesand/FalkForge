using System.Collections.ObjectModel;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.SqlEditor;

public sealed class SqlEditorViewModel : ViewModelBase
{
    private readonly StudioProject _project;
    private SqlDatabaseViewModel? _selectedDatabase;

    public ObservableCollection<SqlDatabaseViewModel> Databases { get; } = [];

    public SqlDatabaseViewModel? SelectedDatabase
    {
        get => _selectedDatabase;
        set => SetProperty(ref _selectedDatabase, value);
    }

    public SqlEditorViewModel(StudioProject project)
    {
        _project = project;
        LoadDatabases();
    }

    private void LoadDatabases()
    {
        Databases.Clear();
        foreach (var db in _project.SqlDatabases)
            Databases.Add(new SqlDatabaseViewModel(db));
    }

    public void AddDatabase()
    {
        var section = new SqlDatabaseSection { Id = $"DB{_project.SqlDatabases.Count + 1}" };
        _project.SqlDatabases.Add(section);
        var vm = new SqlDatabaseViewModel(section);
        Databases.Add(vm);
        SelectedDatabase = vm;
    }

    public void RemoveSelectedDatabase()
    {
        if (SelectedDatabase is null) return;
        _project.SqlDatabases.Remove(SelectedDatabase.Model);
        Databases.Remove(SelectedDatabase);
        SelectedDatabase = Databases.Count > 0 ? Databases[0] : null;
    }

    public void AddScript()
    {
        if (SelectedDatabase is null) return;
        var script = new SqlScriptSection { Id = $"Script{SelectedDatabase.Model.Scripts.Count + 1}" };
        SelectedDatabase.Model.Scripts.Add(script);
        SelectedDatabase.Scripts.Add(new SqlScriptViewModel(script));
    }

    public void RemoveSelectedScript()
    {
        if (SelectedDatabase?.SelectedScript is null) return;
        SelectedDatabase.Model.Scripts.Remove(SelectedDatabase.SelectedScript.Model);
        SelectedDatabase.Scripts.Remove(SelectedDatabase.SelectedScript);
        SelectedDatabase.SelectedScript = SelectedDatabase.Scripts.Count > 0 ? SelectedDatabase.Scripts[0] : null;
    }
}
