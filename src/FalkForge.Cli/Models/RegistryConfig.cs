using System.Text.Json.Serialization;

namespace FalkForge.Cli.Models;

public sealed class RegistryConfig
{
    [JsonPropertyName("root")]
    public string? Root { get; set; }

    [JsonPropertyName("key")]
    public string? Key { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }
}
