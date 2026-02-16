using System.Text.Json.Serialization;

namespace FalkForge.Cli.Models;

public sealed class LaunchConditionConfig
{
    [JsonPropertyName("condition")]
    public string? Condition { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
