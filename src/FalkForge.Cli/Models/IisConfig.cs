using System.Text.Json.Serialization;

namespace FalkForge.Cli.Models;

public sealed class IisConfig
{
    [JsonPropertyName("appPools")]
    public List<IisAppPoolConfig>? AppPools { get; set; }

    [JsonPropertyName("webSites")]
    public List<IisWebSiteConfig>? WebSites { get; set; }
}
