using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.CustomActionsEditor;

public sealed class CustomActionEntryViewModel : ViewModelBase
{
    private readonly CustomActionSection _model;

    public CustomActionEntryViewModel(CustomActionSection model) { _model = model; }

    public CustomActionSection Model => _model;

    public string Id { get => _model.Id; set { _model.Id = value; OnPropertyChanged(); } }
    public string Type { get => _model.Type; set { _model.Type = value; OnPropertyChanged(); } }
    public string Source { get => _model.Source; set { _model.Source = value; OnPropertyChanged(); } }
    public string? Target { get => _model.Target; set { _model.Target = value; OnPropertyChanged(); } }
    public string? Condition { get => _model.Condition; set { _model.Condition = value; OnPropertyChanged(); } }
    public int? Sequence { get => _model.Sequence; set { _model.Sequence = value; OnPropertyChanged(); } }
    public string? After { get => _model.After; set { _model.After = value; OnPropertyChanged(); } }
    public string? Before { get => _model.Before; set { _model.Before = value; OnPropertyChanged(); } }
    public bool Deferred { get => _model.Deferred; set { _model.Deferred = value; OnPropertyChanged(); } }
    public bool Rollback { get => _model.Rollback; set { _model.Rollback = value; OnPropertyChanged(); } }
    public bool Commit { get => _model.Commit; set { _model.Commit = value; OnPropertyChanged(); } }
    public bool NoImpersonate { get => _model.NoImpersonate; set { _model.NoImpersonate = value; OnPropertyChanged(); } }
    public bool ContinueOnError { get => _model.ContinueOnError; set { _model.ContinueOnError = value; OnPropertyChanged(); } }
}
