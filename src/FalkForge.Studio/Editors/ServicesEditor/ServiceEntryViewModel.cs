using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.ServicesEditor;

public sealed class ServiceEntryViewModel : ViewModelBase
{
    private readonly ServiceSection _model;

    public ServiceEntryViewModel(ServiceSection model) { _model = model; }

    public ServiceSection Model => _model;

    public string Name { get => _model.Name; set { _model.Name = value; OnPropertyChanged(); } }
    public string DisplayName { get => _model.DisplayName; set { _model.DisplayName = value; OnPropertyChanged(); } }
    public string Executable { get => _model.Executable; set { _model.Executable = value; OnPropertyChanged(); } }
    public string? Description { get => _model.Description; set { _model.Description = value; OnPropertyChanged(); } }
    public string StartMode { get => _model.StartMode; set { _model.StartMode = value; OnPropertyChanged(); } }
    public string Account { get => _model.Account; set { _model.Account = value; OnPropertyChanged(); } }
    public bool StartOnInstall { get => _model.StartOnInstall; set { _model.StartOnInstall = value; OnPropertyChanged(); } }
    public bool StopOnUninstall { get => _model.StopOnUninstall; set { _model.StopOnUninstall = value; OnPropertyChanged(); } }
}
