using System.Text.Json.Serialization;

namespace FalkForge.Studio.Project;

public sealed class OdbcDriverSection
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("driverName")] public string DriverName { get; set; } = "";
    [JsonPropertyName("fileName")] public string FileName { get; set; } = "";
    [JsonPropertyName("setupFileName")] public string? SetupFileName { get; set; }
}
