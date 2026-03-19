using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.FeaturesEditor;

public sealed class FeatureNodeViewModel : ViewModelBase
{
    private readonly FeatureSection _model;

    public FeatureNodeViewModel(FeatureSection model) { _model = model; }
    public FeatureSection Model => _model;

    public string Id { get => _model.Id; set { _model.Id = value; OnPropertyChanged(); } }
    public string Title { get => _model.Title; set { _model.Title = value; OnPropertyChanged(); } }
    public string? Description { get => _model.Description; set { _model.Description = value; OnPropertyChanged(); } }
    public bool IsDefault { get => _model.IsDefault; set { _model.IsDefault = value; OnPropertyChanged(); } }
    public bool IsRequired { get => _model.IsRequired; set { _model.IsRequired = value; OnPropertyChanged(); } }
    public int InstallLevel { get => _model.InstallLevel; set { _model.InstallLevel = value; OnPropertyChanged(); } }
    public string Display { get => _model.Display; set { _model.Display = value; OnPropertyChanged(); } }

    public static string[] DisplayModes { get; } = ["expand", "collapse", "hidden"];
    public static int[] InstallLevels { get; } = [0, 1, 3, 4];

    public int FileCount => _model.Files.Count;
}
