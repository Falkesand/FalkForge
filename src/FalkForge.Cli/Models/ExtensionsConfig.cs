using System.Text.Json.Serialization;

namespace FalkForge.Cli.Models;

public sealed class ExtensionsConfig
{
    [JsonPropertyName("firewall")]
    public List<FirewallRuleConfig>? Firewall { get; set; }

    [JsonPropertyName("iis")]
    public IisConfig? Iis { get; set; }

    [JsonPropertyName("sql")]
    public List<SqlConfig>? Sql { get; set; }

    [JsonPropertyName("dotnet")]
    public List<DotNetSearchConfig>? DotNet { get; set; }
}
