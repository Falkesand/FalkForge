using System.Text.Json.Serialization;

namespace FalkForge.Cli.Models;

public sealed class SqlScriptConfig
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("sourceFile")]
    public string? SourceFile { get; set; }

    [JsonPropertyName("executeOnInstall")]
    public bool ExecuteOnInstall { get; set; } = true;

    [JsonPropertyName("executeOnUninstall")]
    public bool ExecuteOnUninstall { get; set; }

    [JsonPropertyName("sequence")]
    public int Sequence { get; set; }
}
