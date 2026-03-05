using System.Text.Json.Serialization;

namespace FalkForge.Studio.Project;

public sealed class BundleSettingsSection
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("manufacturer")] public string Manufacturer { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "1.0.0";
    [JsonPropertyName("upgradeCode")] public string? UpgradeCode { get; set; }
    [JsonPropertyName("scope")] public string Scope { get; set; } = "perMachine";
    [JsonPropertyName("uiType")] public string UiType { get; set; } = "BuiltIn";
    [JsonPropertyName("licenseFile")] public string? LicenseFile { get; set; }
    [JsonPropertyName("downloadThrottle")] public long DownloadThrottle { get; set; }
}
