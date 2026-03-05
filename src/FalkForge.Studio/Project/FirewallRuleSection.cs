using System.Text.Json.Serialization;

namespace FalkForge.Studio.Project;

public sealed class FirewallRuleSection
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("protocol")] public string Protocol { get; set; } = "Tcp";
    [JsonPropertyName("port")] public string? Port { get; set; }
    [JsonPropertyName("direction")] public string Direction { get; set; } = "Inbound";
    [JsonPropertyName("profile")] public string Profile { get; set; } = "All";
    [JsonPropertyName("action")] public string Action { get; set; } = "Allow";
    [JsonPropertyName("program")] public string? Program { get; set; }
}
