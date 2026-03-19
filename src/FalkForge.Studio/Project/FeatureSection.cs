using System.Text.Json.Serialization;

namespace FalkForge.Studio.Project;

public sealed class FeatureSection
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("isDefault")]
    public bool IsDefault { get; set; } = true;

    [JsonPropertyName("isRequired")]
    public bool IsRequired { get; set; }

    [JsonPropertyName("installLevel")]
    public int InstallLevel { get; set; } = 1;

    [JsonPropertyName("display")]
    public string Display { get; set; } = "expand";

    [JsonPropertyName("files")]
    public List<FileEntry> Files { get; set; } = [];

    [JsonPropertyName("features")]
    public List<FeatureSection>? Features { get; set; }
}
