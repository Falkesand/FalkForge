using System.Text.Json.Serialization;

namespace FalkForge.Cli.Models;

public sealed class ServiceConfig
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("executable")]
    public string? Executable { get; set; }

    [JsonPropertyName("startType")]
    public string? StartType { get; set; }

    [JsonPropertyName("account")]
    public string? Account { get; set; }
}
