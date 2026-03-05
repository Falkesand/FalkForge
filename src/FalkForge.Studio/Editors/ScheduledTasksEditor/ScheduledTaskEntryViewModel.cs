using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.ScheduledTasksEditor;

public sealed class ScheduledTaskEntryViewModel : ViewModelBase
{
    private readonly ScheduledTaskSection _model;

    public ScheduledTaskEntryViewModel(ScheduledTaskSection model) { _model = model; }

    public ScheduledTaskSection Model => _model;

    public string Id { get => _model.Id; set { _model.Id = value; OnPropertyChanged(); } }
    public string Name { get => _model.Name; set { _model.Name = value; OnPropertyChanged(); } }
    public string Command { get => _model.Command; set { _model.Command = value; OnPropertyChanged(); } }
    public string? Arguments { get => _model.Arguments; set { _model.Arguments = value; OnPropertyChanged(); } }
    public string? WorkingDir { get => _model.WorkingDir; set { _model.WorkingDir = value; OnPropertyChanged(); } }
    public string TriggerType { get => _model.TriggerType; set { _model.TriggerType = value; OnPropertyChanged(); } }
    public string? Schedule { get => _model.Schedule; set { _model.Schedule = value; OnPropertyChanged(); } }
    public string? RunAsUser { get => _model.RunAsUser; set { _model.RunAsUser = value; OnPropertyChanged(); } }
    public bool RunElevated { get => _model.RunElevated; set { _model.RunElevated = value; OnPropertyChanged(); } }
}
