using System.Collections.ObjectModel;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.SqlEditor;

public sealed class SqlDatabaseViewModel : ViewModelBase
{
    private readonly SqlDatabaseSection _model;
    private SqlScriptViewModel? _selectedScript;

    public SqlDatabaseViewModel(SqlDatabaseSection model)
    {
        _model = model;
        foreach (var script in _model.Scripts)
            Scripts.Add(new SqlScriptViewModel(script));
    }

    public SqlDatabaseSection Model => _model;

    public ObservableCollection<SqlScriptViewModel> Scripts { get; } = [];

    public SqlScriptViewModel? SelectedScript
    {
        get => _selectedScript;
        set => SetProperty(ref _selectedScript, value);
    }

    public string Id { get => _model.Id; set { _model.Id = value; OnPropertyChanged(); } }
    public string? Server { get => _model.Server; set { _model.Server = value; OnPropertyChanged(); } }
    public string Database { get => _model.Database; set { _model.Database = value; OnPropertyChanged(); } }
    public bool CreateOnInstall { get => _model.CreateOnInstall; set { _model.CreateOnInstall = value; OnPropertyChanged(); } }
    public bool DropOnUninstall { get => _model.DropOnUninstall; set { _model.DropOnUninstall = value; OnPropertyChanged(); } }
}
