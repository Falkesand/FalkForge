using System.Text.Json.Serialization;

namespace FalkForge.Studio.Project;

public sealed class UiSection
{
    [JsonPropertyName("dialogSet")]
    public string DialogSet { get; set; } = "Minimal";

    [JsonPropertyName("licenseFile")]
    public string? LicenseFile { get; set; }

    [JsonPropertyName("dialogs")]
    public List<DialogDefinition> Dialogs { get; set; } = [];
}
