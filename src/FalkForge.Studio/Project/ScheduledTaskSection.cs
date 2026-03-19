using System.Text.Json.Serialization;

namespace FalkForge.Studio.Project;

public sealed class ScheduledTaskSection
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("command")] public string Command { get; set; } = "";
    [JsonPropertyName("arguments")] public string? Arguments { get; set; }
    [JsonPropertyName("workingDir")] public string? WorkingDir { get; set; }
    [JsonPropertyName("triggerType")] public string TriggerType { get; set; } = "OnInstall";
    [JsonPropertyName("schedule")] public string? Schedule { get; set; }
    [JsonPropertyName("runAsUser")] public string? RunAsUser { get; set; }
    [JsonPropertyName("runElevated")] public bool RunElevated { get; set; }
}
