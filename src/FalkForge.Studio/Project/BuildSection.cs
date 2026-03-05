using System.Text.Json.Serialization;

namespace FalkForge.Studio.Project;

public sealed class BuildSection
{
    [JsonPropertyName("outputPath")]
    public string OutputPath { get; set; } = "out/";

    [JsonPropertyName("compression")]
    public string Compression { get; set; } = "High";
}
