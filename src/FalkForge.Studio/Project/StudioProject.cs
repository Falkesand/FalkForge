using System.Text.Json.Serialization;

namespace FalkForge.Studio.Project;

public sealed class StudioProject
{
    [JsonPropertyName("projectType")]
    public string ProjectType { get; set; } = "msi";

    [JsonPropertyName("product")]
    public ProductSection Product { get; set; } = new();

    [JsonPropertyName("installDirectory")]
    public string? InstallDirectory { get; set; }

    [JsonPropertyName("features")]
    public List<FeatureSection> Features { get; set; } = [];

    [JsonPropertyName("registry")]
    public List<RegistryEntrySection> Registry { get; set; } = [];

    [JsonPropertyName("services")]
    public List<ServiceSection> Services { get; set; } = [];

    [JsonPropertyName("shortcuts")]
    public List<ShortcutSection> Shortcuts { get; set; } = [];

    [JsonPropertyName("environment")]
    public List<EnvironmentVariableSection> Environment { get; set; } = [];

    [JsonPropertyName("customActions")]
    public List<CustomActionSection> CustomActions { get; set; } = [];

    [JsonPropertyName("sqlDatabases")]
    public List<SqlDatabaseSection> SqlDatabases { get; set; } = [];

    [JsonPropertyName("firewallRules")]
    public List<FirewallRuleSection> FirewallRules { get; set; } = [];

    [JsonPropertyName("xmlConfigs")]
    public List<XmlConfigSection> XmlConfigs { get; set; } = [];

    [JsonPropertyName("scheduledTasks")]
    public List<ScheduledTaskSection> ScheduledTasks { get; set; } = [];

    [JsonPropertyName("perfCounters")]
    public List<PerfCounterSection> PerfCounters { get; set; } = [];

    [JsonPropertyName("odbcDrivers")]
    public List<OdbcDriverSection> OdbcDrivers { get; set; } = [];

    [JsonPropertyName("odbcDataSources")]
    public List<OdbcDataSourceSection> OdbcDataSources { get; set; } = [];

    [JsonPropertyName("bundleSettings")]
    public BundleSettingsSection? BundleSettings { get; set; }

    [JsonPropertyName("bundlePackages")]
    public List<BundlePackageSection> BundlePackages { get; set; } = [];

    [JsonPropertyName("ui")]
    public UiSection Ui { get; set; } = new();

    [JsonPropertyName("build")]
    public BuildSection Build { get; set; } = new();
}
