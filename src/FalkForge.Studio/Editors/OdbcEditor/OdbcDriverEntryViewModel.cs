using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.OdbcEditor;

public sealed class OdbcDriverEntryViewModel : ViewModelBase
{
    private readonly OdbcDriverSection _model;

    public OdbcDriverEntryViewModel(OdbcDriverSection model) { _model = model; }

    public OdbcDriverSection Model => _model;

    public string Id { get => _model.Id; set { _model.Id = value; OnPropertyChanged(); } }
    public string DriverName { get => _model.DriverName; set { _model.DriverName = value; OnPropertyChanged(); } }
    public string FileName { get => _model.FileName; set { _model.FileName = value; OnPropertyChanged(); } }
    public string? SetupFileName { get => _model.SetupFileName; set { _model.SetupFileName = value; OnPropertyChanged(); } }
}
