using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.FirewallEditor;

public sealed class FirewallRuleViewModel : ViewModelBase
{
    private readonly FirewallRuleSection _model;

    public FirewallRuleViewModel(FirewallRuleSection model) { _model = model; }

    public FirewallRuleSection Model => _model;

    public string Id { get => _model.Id; set { _model.Id = value; OnPropertyChanged(); } }
    public string Name { get => _model.Name; set { _model.Name = value; OnPropertyChanged(); } }
    public string Protocol { get => _model.Protocol; set { _model.Protocol = value; OnPropertyChanged(); } }
    public string? Port { get => _model.Port; set { _model.Port = value; OnPropertyChanged(); } }
    public string Direction { get => _model.Direction; set { _model.Direction = value; OnPropertyChanged(); } }
    public string Profile { get => _model.Profile; set { _model.Profile = value; OnPropertyChanged(); } }
    public string Action { get => _model.Action; set { _model.Action = value; OnPropertyChanged(); } }
    public string? Program { get => _model.Program; set { _model.Program = value; OnPropertyChanged(); } }
}
