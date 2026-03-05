using System.Text.Json.Serialization;

namespace FalkForge.Studio.Project;

public sealed class OdbcDataSourceSection
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("driverName")] public string DriverName { get; set; } = "";
    [JsonPropertyName("registration")] public string Registration { get; set; } = "PerMachine";
}
