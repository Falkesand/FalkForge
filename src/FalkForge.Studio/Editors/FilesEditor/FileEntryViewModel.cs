using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.FilesEditor;

public sealed class FileEntryViewModel : ViewModelBase
{
    private readonly FileEntry _model;
    private string _featureId;

    public FileEntryViewModel(FileEntry model, string featureId)
    {
        _model = model;
        _featureId = featureId;
    }

    public FileEntry Model => _model;

    public string Source
    {
        get => _model.Source;
        set { _model.Source = value; OnPropertyChanged(); }
    }

    public string? TargetDirectory
    {
        get => _model.TargetDirectory;
        set { _model.TargetDirectory = value; OnPropertyChanged(); }
    }

    public bool Vital
    {
        get => _model.Vital;
        set { _model.Vital = value; OnPropertyChanged(); }
    }

    public string FeatureId
    {
        get => _featureId;
        set => SetProperty(ref _featureId, value);
    }
}
