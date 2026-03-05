using System.Text.Json.Serialization;

namespace FalkForge.Studio.Project;

public sealed class ServiceSection
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("executable")]
    public string Executable { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("startMode")]
    public string StartMode { get; set; } = "Automatic";

    [JsonPropertyName("account")]
    public string Account { get; set; } = "LocalSystem";

    [JsonPropertyName("startOnInstall")]
    public bool StartOnInstall { get; set; } = true;

    [JsonPropertyName("stopOnUninstall")]
    public bool StopOnUninstall { get; set; } = true;
}
