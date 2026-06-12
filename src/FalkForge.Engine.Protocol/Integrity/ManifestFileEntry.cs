namespace FalkForge.Engine.Protocol.Integrity;

using System.Text.Json.Serialization;

/// <summary>
/// A single entry in the integrity manifest, mapping a payload identifier (the
/// bundle package id) to its expected SHA-256 hash. Property names and declaration
/// order are part of the signed wire contract — the engine recomputes the signed
/// bytes from this exact serialization, so changing either breaks verification of
/// already-signed bundles.
/// </summary>
public sealed class ManifestFileEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;
}
