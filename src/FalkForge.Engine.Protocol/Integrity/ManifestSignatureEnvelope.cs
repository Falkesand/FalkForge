namespace FalkForge.Engine.Protocol.Integrity;

using System.Text.Json.Serialization;

/// <summary>
/// The envelope stored in <see cref="FalkForge.Engine.Protocol.Manifest.InstallerManifest.ManifestSignature"/>.
/// Carries the ECDSA-P256 public key (SubjectPublicKeyInfo, base64), the signed file
/// hash entries, and the base64 ECDSA signature computed over the SHA-256 of the
/// canonically serialized <see cref="Files"/> array.
///
/// <para>This is the wire contract shared by the build-time signer (Compiler.Bundle)
/// and the runtime verifier (Engine). Property names and declaration order are locked
/// by <see cref="IntegrityEnvelopeCodec"/>; do not reorder or rename without breaking
/// verification of bundles already signed in the field.</para>
/// </summary>
public sealed class ManifestSignatureEnvelope
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
