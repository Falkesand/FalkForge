using System.Text.Json.Serialization;

namespace FalkForge.Studio.Project;

public sealed class ProductSection
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "My Application";

    [JsonPropertyName("manufacturer")]
    public string Manufacturer { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("upgradeCode")]
    public string? UpgradeCode { get; set; }

    [JsonPropertyName("architecture")]
    public string Architecture { get; set; } = "x64";

    [JsonPropertyName("scope")]
    public string Scope { get; set; } = "perMachine";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("helpUrl")]
    public string? HelpUrl { get; set; }

    [JsonPropertyName("aboutUrl")]
    public string? AboutUrl { get; set; }

    [JsonPropertyName("updateUrl")]
    public string? UpdateUrl { get; set; }

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("comments")]
    public string? Comments { get; set; }

    [JsonPropertyName("licenseFile")]
    public string? LicenseFile { get; set; }
}
