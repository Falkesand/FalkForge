using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.BuildSettingsEditor;

public sealed class BuildSettingsEditorViewModel : ViewModelBase
{
    private readonly BuildSection _model;

    public BuildSettingsEditorViewModel(BuildSection model) { _model = model; }

    public string OutputPath { get => _model.OutputPath; set { _model.OutputPath = value; OnPropertyChanged(); } }
    public string Compression { get => _model.Compression; set { _model.Compression = value; OnPropertyChanged(); } }
    public string[] CompressionLevels { get; } = ["None", "Low", "Medium", "High"];
}
