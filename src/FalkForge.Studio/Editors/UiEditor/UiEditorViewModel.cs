using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.UiEditor;

public sealed class UiEditorViewModel : ViewModelBase
{
    private readonly UiSection _model;

    public UiEditorViewModel(UiSection model) { _model = model; }

    public string DialogSet { get => _model.DialogSet; set { _model.DialogSet = value; OnPropertyChanged(); } }
    public string? LicenseFile { get => _model.LicenseFile; set { _model.LicenseFile = value; OnPropertyChanged(); } }
    public string[] DialogSets { get; } = ["None", "Minimal", "InstallDir", "FeatureTree", "Mondo", "Advanced"];
}
