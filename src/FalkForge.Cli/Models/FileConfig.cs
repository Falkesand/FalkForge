using System.Text.Json.Serialization;

namespace FalkForge.Cli.Models;

public sealed class FileConfig
{
    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("shortcut")]
    public ShortcutConfig? Shortcut { get; set; }
}
