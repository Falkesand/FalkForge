namespace FalkForge.Engine.Protocol.Integrity;

using System.Text.Json.Serialization;

/// <summary>
/// The envelope stored in <see cref="FalkForge.Engine.Protocol.Manifest.InstallerManifest.ManifestSignature"/>.
/// Carries the signed file-hash entries (<see cref="Files"/>) and a list of self-describing
/// ECDSA-P256 <see cref="Signatures"/>, each over the SHA-256 of the canonically serialized
/// <see cref="Files"/> array.
///
/// <para><b>Version 2</b> uses <see cref="Signatures"/> (one or more <see cref="SignatureEntry"/>).
/// <b>Version 1</b> (already-shipped bundles) instead carries a single top-level
/// <see cref="PublicKey"/> + <see cref="Signature"/>. Both shapes deserialize into this one type;
/// <see cref="IntegrityEnvelopeCodec.Parse"/> normalizes a v1 envelope into a one-element
/// <see cref="Signatures"/> list so the verifier only ever sees the list form.</para>
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

    /// <summary>
    /// v1 read-compat only: the single embedded public key of a legacy single-signature envelope.
    /// Null (and omitted) on v2 envelopes, which carry their key(s) inside <see cref="Signatures"/>.
    /// </summary>
    [JsonPropertyName("publicKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PublicKey { get; set; }

    [JsonPropertyName("files")]
    public IReadOnlyList<ManifestFileEntry> Files { get; set; } = [];

    /// <summary>
    /// v1 read-compat only: the single signature of a legacy single-signature envelope.
    /// Null (and omitted) on v2 envelopes, which carry their signature(s) inside <see cref="Signatures"/>.
    /// </summary>
    [JsonPropertyName("signature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Signature { get; set; }

    /// <summary>
    /// v2 signatures — one entry per signing key, all over the identical <see cref="Files"/> list.
    /// Populated directly on v2 envelopes and synthesized from the v1 fields by
    /// <see cref="IntegrityEnvelopeCodec.Parse"/> for legacy envelopes.
    /// </summary>
    [JsonPropertyName("signatures")]
    public IReadOnlyList<SignatureEntry> Signatures { get; set; } = [];
}
