using System.Text.Json.Serialization;

namespace FalkForge.Cli.Models;

public sealed class InstallerConfig
{
    [JsonPropertyName("product")]
    public ProductConfig Product { get; set; } = new();

    [JsonPropertyName("ui")]
    public string? Ui { get; set; }

    [JsonPropertyName("license")]
    public string? License { get; set; }

    [JsonPropertyName("installDirectory")]
    public string? InstallDirectory { get; set; }

    [JsonPropertyName("majorUpgrade")]
    public MajorUpgradeConfig? MajorUpgrade { get; set; }

    [JsonPropertyName("launchConditions")]
    public List<LaunchConditionConfig>? LaunchConditions { get; set; }

    [JsonPropertyName("features")]
    public List<FeatureConfig>? Features { get; set; }

    [JsonPropertyName("extensions")]
    public ExtensionsConfig? Extensions { get; set; }
}
