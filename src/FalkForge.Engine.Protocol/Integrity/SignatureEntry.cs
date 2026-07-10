namespace FalkForge.Engine.Protocol.Integrity;

using System.Text.Json.Serialization;

/// <summary>
/// One self-describing signature in a v2 integrity envelope. Every signer signs the
/// identical <see cref="ManifestSignatureEnvelope.Files"/> list, so an envelope can carry
/// several entries (rotation-safe dual-sign) while the signed message stays the same for all.
///
/// <para>Property names and declaration order are part of the wire contract consumed by
/// <see cref="IntegrityEnvelopeCodec"/>; do not rename or reorder without breaking verification
/// of bundles already signed in the field.</para>
/// </summary>
public sealed class SignatureEntry
{
    /// <summary>
    /// A stable, operator-facing label for the signing key (e.g. "falkforge-2026-a").
    /// Purely informational — it is never trusted and never affects verification.
    /// </summary>
    [JsonPropertyName("keyId")]
    public string KeyId { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 of the key's SubjectPublicKeyInfo, uppercase hex, no separators. This is the value
    /// matched against the engine's baked trusted set. It is re-derived from <see cref="PublicKey"/>
    /// during verification and rejected if it does not match — a lying fingerprint is never trusted.
    /// </summary>
    [JsonPropertyName("fingerprint")]
    public string Fingerprint { get; set; } = string.Empty;

    /// <summary>Base64 SubjectPublicKeyInfo of the signing key (self-describing, as in v1).</summary>
    [JsonPropertyName("publicKey")]
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>Base64 ECDSA signature over SHA-256(UTF-8(JSON(files))).</summary>
    [JsonPropertyName("signature")]
    public string Signature { get; set; } = string.Empty;

    /// <summary>
    /// The signature algorithm of this entry (PQ-hybrid Stage 1). OPTIONAL and appended last so the
    /// wire shape of every pre-existing entry is byte-identical: <c>null</c> (the field absent on the
    /// wire) means <see cref="IntegrityEnvelopeCodec.AlgorithmId"/> (ECDSA-P256) — exactly what every
    /// envelope signed before this field existed meant. An ML-DSA entry carries its FIPS 204
    /// parameter-set name (e.g. <see cref="IntegrityEnvelopeCodec.MlDsa65AlgorithmId"/>). Verifiers
    /// skip entries with an algorithm they do not know (forward compatibility); already-shipped
    /// algorithm-ignorant verifiers skip an ML-DSA entry anyway because its signature fails the
    /// 64-byte ECDSA low-S length gate and iteration continues.
    /// </summary>
    [JsonPropertyName("algorithm")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Algorithm { get; set; }
}
