using System.Text.Json.Serialization;

namespace FalkForge.Studio.Project;

public sealed class RegistryEntrySection
{
    [JsonPropertyName("root")]
    public string Root { get; set; } = "LocalMachine";

    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("valueName")]
    public string ValueName { get; set; } = "";

    [JsonPropertyName("valueType")]
    public string ValueType { get; set; } = "String";

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";
}
