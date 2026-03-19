using System.Text.Json.Serialization;

namespace FalkForge.Studio.Project;

public sealed class PerfCounterSection
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("categoryName")] public string CategoryName { get; set; } = "";
    [JsonPropertyName("counterName")] public string CounterName { get; set; } = "";
    [JsonPropertyName("counterType")] public string CounterType { get; set; } = "NumberOfItems32";
    [JsonPropertyName("categoryHelp")] public string? CategoryHelp { get; set; }
    [JsonPropertyName("counterHelp")] public string? CounterHelp { get; set; }
}
