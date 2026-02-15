using System.Text.Json.Serialization;

namespace FalkForge.Cli.Models;

public sealed class ProductConfig
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("manufacturer")]
    public string? Manufacturer { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("upgradeCode")]
    public string? UpgradeCode { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("platform")]
    public string? Platform { get; set; }
}
