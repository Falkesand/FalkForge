using System.Text.Json.Serialization;

namespace FalkForge.Cli.Models;

public sealed class EnvironmentVariableConfig
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("action")]
    public string? Action { get; set; }

    [JsonPropertyName("system")]
    public bool System { get; set; } = true;
}
