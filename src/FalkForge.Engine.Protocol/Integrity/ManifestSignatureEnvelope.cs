namespace FalkForge.Engine.Protocol.Integrity;

using System.Text.Json.Serialization;
using FalkForge.Engine.Protocol.Manifest;

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

    /// <summary>
    /// v2 key-epoch counter (C14 Stage 2, §6). The publisher bumps it only when a key is retired or
    /// revoked (not per release). A client refuses any bundle whose epoch is below the highest it has
    /// accepted (anti-downgrade / replay defense). 0 (the default, and the value for v1 bundles) means
    /// "unset". The epoch is part of the signed message <b>only when non-zero</b> so that legacy v1 and
    /// Stage-1 v2 (epoch-0) envelopes keep the byte-identical files-only signed bytes and still verify —
    /// see <see cref="IntegrityEnvelopeCodec.ComputeSignedBytes(IReadOnlyList{ManifestFileEntry}, int, IReadOnlyList{string})"/>.
    /// </summary>
    [JsonPropertyName("epoch")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Epoch { get; set; }

    /// <summary>
    /// v2 key-revocation list (C14 Stage 2, §6.5): fingerprints (uppercase hex) the publisher declares
    /// revoked. Once a client applies a verified update carrying revocations, it records them and
    /// thereafter refuses any bundle signed only by a revoked key — even one still in an older engine's
    /// baked set. The list is part of the signed message <b>only when non-empty</b> (same compat rule as
    /// <see cref="Epoch"/>), so it is cryptographically covered and cannot be stripped or forged without
    /// breaking the signature. Empty (the default) on v1 and Stage-1 v2 envelopes.
    /// </summary>
    [JsonPropertyName("revoked")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public IReadOnlyList<string> Revoked { get; set; } = [];

    /// <summary>
    /// The external, separately-downloadable payload containers (A6) covered by the signature. Each entry
    /// mirrors the manifest's <see cref="FalkForge.Engine.Protocol.Manifest.InstallerManifest.ExternalContainers"/>
    /// so the container <b>download URL</b>, whole-file <b>hash</b>, and declared <b>package membership</b> are
    /// bound into the ECDSA-signed message — closing the A6 SSRF/DoS residual where a tampered bundle could
    /// repoint <c>DownloadUrl</c> at an internal host without invalidating the signature (payloads were
    /// already bound, the URL was not). Folded into the signed message <b>only when present</b> (same compat
    /// rule as <see cref="Epoch"/>/<see cref="Revoked"/>), so a bundle with no external containers signs the
    /// byte-identical files-only message every already-shipped bundle signed — see
    /// <see cref="IntegrityEnvelopeCodec.ComputeSignedBytes(IReadOnlyList{ManifestFileEntry}, int, IReadOnlyList{string}, IReadOnlyList{ExternalContainerInfo})"/>.
    /// Null (and omitted from the wire) on v1 and container-free envelopes;
    /// <see cref="SignedPayloadTocVerifier"/> binds the manifest's declared set to this signed copy (INT013).
    /// </summary>
    [JsonPropertyName("externalContainers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ExternalContainerInfo>? ExternalContainers { get; set; }
}
