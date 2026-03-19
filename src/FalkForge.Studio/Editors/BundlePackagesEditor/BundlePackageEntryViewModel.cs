using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.BundlePackagesEditor;

public sealed class BundlePackageEntryViewModel : ViewModelBase
{
    private readonly BundlePackageSection _model;

    public BundlePackageEntryViewModel(BundlePackageSection model) { _model = model; }

    public BundlePackageSection Model => _model;

    public string Id { get => _model.Id; set { _model.Id = value; OnPropertyChanged(); } }
    public string Type { get => _model.Type; set { _model.Type = value; OnPropertyChanged(); } }
    public string SourcePath { get => _model.SourcePath; set { _model.SourcePath = value; OnPropertyChanged(); } }
    public string DisplayName { get => _model.DisplayName; set { _model.DisplayName = value; OnPropertyChanged(); } }
    public bool Vital { get => _model.Vital; set { _model.Vital = value; OnPropertyChanged(); } }
    public string? InstallCondition { get => _model.InstallCondition; set { _model.InstallCondition = value; OnPropertyChanged(); } }
    public string DetectionMode { get => _model.DetectionMode; set { _model.DetectionMode = value; OnPropertyChanged(); } }
    public string? AuthenticodeThumbprint { get => _model.AuthenticodeThumbprint; set { _model.AuthenticodeThumbprint = value; OnPropertyChanged(); } }
    public bool IsPrerequisite { get => _model.IsPrerequisite; set { _model.IsPrerequisite = value; OnPropertyChanged(); } }
}
