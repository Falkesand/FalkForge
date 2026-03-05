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
}
