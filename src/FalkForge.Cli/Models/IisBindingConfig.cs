using System.Text.Json.Serialization;

namespace FalkForge.Cli.Models;

public sealed class IisBindingConfig
{
    [JsonPropertyName("protocol")]
    public string? Protocol { get; set; }

    [JsonPropertyName("port")]
    public int Port { get; set; }

    [JsonPropertyName("host")]
    public string? Host { get; set; }
}
