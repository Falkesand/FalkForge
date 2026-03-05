using System.Text.Json.Serialization;

namespace FalkForge.Studio.Project;

public sealed class FileEntry
{
    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    [JsonPropertyName("targetDirectory")]
    public string? TargetDirectory { get; set; }
}
