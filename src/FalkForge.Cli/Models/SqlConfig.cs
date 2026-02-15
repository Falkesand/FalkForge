using System.Text.Json.Serialization;

namespace FalkForge.Cli.Models;

public sealed class SqlConfig
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("server")]
    public string? Server { get; set; }

    [JsonPropertyName("database")]
    public string? Database { get; set; }

    [JsonPropertyName("createOnInstall")]
    public bool CreateOnInstall { get; set; } = true;

    [JsonPropertyName("dropOnUninstall")]
    public bool DropOnUninstall { get; set; }

    [JsonPropertyName("scripts")]
    public List<SqlScriptConfig>? Scripts { get; set; }
}
