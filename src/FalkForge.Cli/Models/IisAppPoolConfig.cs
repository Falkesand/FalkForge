using System.Text.Json.Serialization;

namespace FalkForge.Cli.Models;

public sealed class IisAppPoolConfig
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("runtimeVersion")]
    public string? RuntimeVersion { get; set; }

    [JsonPropertyName("pipelineMode")]
    public string? PipelineMode { get; set; }

    [JsonPropertyName("identity")]
    public string? Identity { get; set; }
}
