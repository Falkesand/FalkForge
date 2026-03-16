namespace FalkForge.Engine.Integrity;

using System.Text.Json.Serialization;

/// <summary>
/// The envelope format stored in <see cref="FalkForge.Engine.Protocol.Manifest.InstallerManifest.ManifestSignature"/>.
/// Contains the ECDSA public key, file hash entries, and the signature over the entries.
/// </summary>
internal sealed class ManifestSignatureEnvelope
{
    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("algorithm")]
    public string Algorithm { get; set; } = string.Empty;

    [JsonPropertyName("publicKey")]
    public string PublicKey { get; set; } = string.Empty;

    [JsonPropertyName("files")]
    public IReadOnlyList<ManifestFileEntry> Files { get; set; } = [];

    [JsonPropertyName("signature")]
    public string Signature { get; set; } = string.Empty;
}
