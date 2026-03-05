using System.Text.Json.Serialization;

namespace FalkForge.Cli.Models;

public sealed class MajorUpgradeConfig
{
    [JsonPropertyName("schedule")]
    public string? Schedule { get; set; }
}
