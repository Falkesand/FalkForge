using System.Text.Json.Serialization;

namespace FalkForge.Cli.Models;

public sealed class FeatureConfig
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("default")]
    public bool Default { get; set; } = true;

    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("files")]
    public List<FileConfig>? Files { get; set; }

    [JsonPropertyName("registry")]
    public List<RegistryConfig>? Registry { get; set; }

    [JsonPropertyName("services")]
    public List<ServiceConfig>? Services { get; set; }

    [JsonPropertyName("environmentVariables")]
    public List<EnvironmentVariableConfig>? EnvironmentVariables { get; set; }

    [JsonPropertyName("features")]
    public List<FeatureConfig>? Features { get; set; }
}
