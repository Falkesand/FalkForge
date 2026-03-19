namespace FalkForge.Engine.Integrity;

using System.Text.Json.Serialization;

/// <summary>
/// A single file entry in the integrity manifest, mapping a relative path to its expected SHA-256 hash.
/// </summary>
internal sealed class ManifestFileEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;
}
