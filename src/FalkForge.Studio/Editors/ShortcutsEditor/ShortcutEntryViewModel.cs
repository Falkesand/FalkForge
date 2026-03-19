using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.ShortcutsEditor;

public sealed class ShortcutEntryViewModel : ViewModelBase
{
    private readonly ShortcutSection _model;

    public ShortcutEntryViewModel(ShortcutSection model) { _model = model; }

    public ShortcutSection Model => _model;

    public string Name { get => _model.Name; set { _model.Name = value; OnPropertyChanged(); } }
    public string TargetFile { get => _model.TargetFile; set { _model.TargetFile = value; OnPropertyChanged(); } }
    public bool Desktop { get => _model.Desktop; set { _model.Desktop = value; OnPropertyChanged(); } }
    public bool StartMenu { get => _model.StartMenu; set { _model.StartMenu = value; OnPropertyChanged(); } }
    public bool Startup { get => _model.Startup; set { _model.Startup = value; OnPropertyChanged(); } }
    public string? Arguments { get => _model.Arguments; set { _model.Arguments = value; OnPropertyChanged(); } }
    public string? Description { get => _model.Description; set { _model.Description = value; OnPropertyChanged(); } }
    public string? IconFile { get => _model.IconFile; set { _model.IconFile = value; OnPropertyChanged(); } }
    public string? WorkingDirectory { get => _model.WorkingDirectory; set { _model.WorkingDirectory = value; OnPropertyChanged(); } }
    public string? StartMenuSubfolder { get => _model.StartMenuSubfolder; set { _model.StartMenuSubfolder = value; OnPropertyChanged(); } }
}
