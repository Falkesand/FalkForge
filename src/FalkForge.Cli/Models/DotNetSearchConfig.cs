using System.Text.Json.Serialization;

namespace FalkForge.Cli.Models;

public sealed class DotNetSearchConfig
{
    [JsonPropertyName("runtimeType")]
    public string? RuntimeType { get; set; }

    [JsonPropertyName("platform")]
    public string? Platform { get; set; }

    [JsonPropertyName("minimumVersion")]
    public string? MinimumVersion { get; set; }

    [JsonPropertyName("variableName")]
    public string? VariableName { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
