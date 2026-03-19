using System.Text.Json.Serialization;

namespace FalkForge.Studio.Project;

public sealed class EnvironmentVariableSection
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    [JsonPropertyName("action")]
    public string Action { get; set; } = "Set";

    [JsonPropertyName("isSystem")]
    public bool IsSystem { get; set; } = true;
}
