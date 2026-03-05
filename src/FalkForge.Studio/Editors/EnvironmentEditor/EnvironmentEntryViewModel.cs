using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.EnvironmentEditor;

public sealed class EnvironmentEntryViewModel : ViewModelBase
{
    private readonly EnvironmentVariableSection _model;

    public EnvironmentEntryViewModel(EnvironmentVariableSection model) { _model = model; }

    public EnvironmentVariableSection Model => _model;

    public string Name { get => _model.Name; set { _model.Name = value; OnPropertyChanged(); } }
    public string Value { get => _model.Value; set { _model.Value = value; OnPropertyChanged(); } }
    public string Action { get => _model.Action; set { _model.Action = value; OnPropertyChanged(); } }
    public bool IsSystem { get => _model.IsSystem; set { _model.IsSystem = value; OnPropertyChanged(); } }
}
