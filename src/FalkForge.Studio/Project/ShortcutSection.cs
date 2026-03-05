using System.Text.Json.Serialization;

namespace FalkForge.Studio.Project;

public sealed class ShortcutSection
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("targetFile")]
    public string TargetFile { get; set; } = "";

    [JsonPropertyName("desktop")]
    public bool Desktop { get; set; }

    [JsonPropertyName("startMenu")]
    public bool StartMenu { get; set; } = true;

    [JsonPropertyName("startup")]
    public bool Startup { get; set; }

    [JsonPropertyName("arguments")]
    public string? Arguments { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("iconFile")]
    public string? IconFile { get; set; }

    [JsonPropertyName("workingDirectory")]
    public string? WorkingDirectory { get; set; }

    [JsonPropertyName("startMenuSubfolder")]
    public string? StartMenuSubfolder { get; set; }
}
