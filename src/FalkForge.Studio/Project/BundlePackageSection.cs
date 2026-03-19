using System.Text.Json.Serialization;

namespace FalkForge.Studio.Project;

public sealed class BundlePackageSection
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "MsiPackage";
    [JsonPropertyName("sourcePath")] public string SourcePath { get; set; } = "";
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("vital")] public bool Vital { get; set; } = true;
    [JsonPropertyName("installCondition")] public string? InstallCondition { get; set; }
    [JsonPropertyName("detectionMode")] public string DetectionMode { get; set; } = "Default";
    [JsonPropertyName("authenticodeThumbprint")] public string? AuthenticodeThumbprint { get; set; }
    [JsonPropertyName("isPrerequisite")] public bool IsPrerequisite { get; set; }
}
