using System.Text.Json.Serialization;

namespace FalkForge.Cli.Models;

public sealed class MajorUpgradeConfig
{
    [JsonPropertyName("allowDowngrades")]
    public bool AllowDowngrades { get; set; }

    [JsonPropertyName("downgradeMessage")]
    public string? DowngradeMessage { get; set; }

    [JsonPropertyName("schedule")]
    public string? Schedule { get; set; }
}
