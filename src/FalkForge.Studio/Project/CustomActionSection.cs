using System.Text.Json.Serialization;

namespace FalkForge.Studio.Project;

public sealed class CustomActionSection
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "DllFromBinary";

    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    [JsonPropertyName("target")]
    public string? Target { get; set; }

    [JsonPropertyName("condition")]
    public string? Condition { get; set; }

    [JsonPropertyName("sequence")]
    public int? Sequence { get; set; }

    [JsonPropertyName("after")]
    public string? After { get; set; }

    [JsonPropertyName("before")]
    public string? Before { get; set; }

    [JsonPropertyName("deferred")]
    public bool Deferred { get; set; }

    [JsonPropertyName("rollback")]
    public bool Rollback { get; set; }

    [JsonPropertyName("commit")]
    public bool Commit { get; set; }

    [JsonPropertyName("noImpersonate")]
    public bool NoImpersonate { get; set; }

    [JsonPropertyName("continueOnError")]
    public bool ContinueOnError { get; set; }
}
