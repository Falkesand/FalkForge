using System.Text.Json.Serialization;

namespace FalkForge.Studio.Project;

public sealed class XmlConfigSection
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("filePath")] public string FilePath { get; set; } = "";
    [JsonPropertyName("xPath")] public string XPath { get; set; } = "";
    [JsonPropertyName("action")] public string Action { get; set; } = "SetAttribute";
    [JsonPropertyName("elementName")] public string? ElementName { get; set; }
    [JsonPropertyName("attributeName")] public string? AttributeName { get; set; }
    [JsonPropertyName("value")] public string? Value { get; set; }
    [JsonPropertyName("sequence")] public int Sequence { get; set; }
}
