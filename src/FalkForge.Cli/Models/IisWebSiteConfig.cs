using System.Text.Json.Serialization;

namespace FalkForge.Cli.Models;

public sealed class IisWebSiteConfig
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("directory")]
    public string? Directory { get; set; }

    [JsonPropertyName("appPool")]
    public string? AppPool { get; set; }

    [JsonPropertyName("bindings")]
    public List<IisBindingConfig>? Bindings { get; set; }
}
