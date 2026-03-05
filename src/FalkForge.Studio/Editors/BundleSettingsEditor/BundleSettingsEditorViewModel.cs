using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.BundleSettingsEditor;

public sealed class BundleSettingsEditorViewModel : ViewModelBase
{
    private readonly BundleSettingsSection _settings;

    public static readonly string[] Scopes = ["perMachine", "perUser"];
    public static readonly string[] UiTypes = ["BuiltIn", "Custom", "Silent"];

    public BundleSettingsEditorViewModel(BundleSettingsSection settings) { _settings = settings; }

    public string Name { get => _settings.Name; set { _settings.Name = value; OnPropertyChanged(); } }
    public string Manufacturer { get => _settings.Manufacturer; set { _settings.Manufacturer = value; OnPropertyChanged(); } }
    public string Version { get => _settings.Version; set { _settings.Version = value; OnPropertyChanged(); } }
    public string? UpgradeCode { get => _settings.UpgradeCode; set { _settings.UpgradeCode = value; OnPropertyChanged(); } }
    public string Scope { get => _settings.Scope; set { _settings.Scope = value; OnPropertyChanged(); } }
    public string UiType { get => _settings.UiType; set { _settings.UiType = value; OnPropertyChanged(); } }
    public string? LicenseFile { get => _settings.LicenseFile; set { _settings.LicenseFile = value; OnPropertyChanged(); } }
    public long DownloadThrottle { get => _settings.DownloadThrottle; set { _settings.DownloadThrottle = value; OnPropertyChanged(); } }
}
