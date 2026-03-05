using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.OdbcEditor;

public sealed class OdbcDataSourceEntryViewModel : ViewModelBase
{
    private readonly OdbcDataSourceSection _model;

    public OdbcDataSourceEntryViewModel(OdbcDataSourceSection model) { _model = model; }

    public OdbcDataSourceSection Model => _model;

    public string Id { get => _model.Id; set { _model.Id = value; OnPropertyChanged(); } }
    public string Name { get => _model.Name; set { _model.Name = value; OnPropertyChanged(); } }
    public string DriverName { get => _model.DriverName; set { _model.DriverName = value; OnPropertyChanged(); } }
    public string Registration { get => _model.Registration; set { _model.Registration = value; OnPropertyChanged(); } }
}
