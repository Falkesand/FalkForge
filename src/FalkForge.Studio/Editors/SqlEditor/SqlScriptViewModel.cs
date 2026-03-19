using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.SqlEditor;

public sealed class SqlScriptViewModel : ViewModelBase
{
    private readonly SqlScriptSection _model;

    public SqlScriptViewModel(SqlScriptSection model) { _model = model; }

    public SqlScriptSection Model => _model;

    public string Id { get => _model.Id; set { _model.Id = value; OnPropertyChanged(); } }
    public string SourceFile { get => _model.SourceFile; set { _model.SourceFile = value; OnPropertyChanged(); } }
    public bool ExecuteOnInstall { get => _model.ExecuteOnInstall; set { _model.ExecuteOnInstall = value; OnPropertyChanged(); } }
    public bool ExecuteOnUninstall { get => _model.ExecuteOnUninstall; set { _model.ExecuteOnUninstall = value; OnPropertyChanged(); } }
    public int Sequence { get => _model.Sequence; set { _model.Sequence = value; OnPropertyChanged(); } }
}
