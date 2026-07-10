namespace FalkForge.Signing;

/// <summary>
/// The output of an <see cref="ISignatureProvider"/>: one self-describing ECDSA signature over the
/// canonical manifest message, together with the public key it was produced with.
///
/// <para><b>Encoding contract (locked).</b> <see cref="Signature"/> MUST be the IEEE&#160;P1363
/// fixed-width r‖s concatenation (64 bytes for P-256), <b>not</b> an ASN.1 DER
/// (<c>Rfc3279DerSequence</c>) sequence. This is the exact encoding the runtime verifier
/// (<c>IntegrityEnvelopeCodec.VerifyHash</c>) expects; a provider whose backend returns DER
/// (e.g. a remote signing service) must convert to P1363 before returning. Keeping this invariant
/// is what lets the signing backend change without touching the verifier or the on-disk envelope.</para>
/// </summary>
public sealed class ProviderSignature
{
    /// <summary>The signer's public key as a SubjectPublicKeyInfo (SPKI) blob. Self-describing, non-secret.</summary>
    public required byte[] SubjectPublicKeyInfo { get; init; }

    /// <summary>
    /// The raw ECDSA signature in IEEE P1363 (r‖s) form — the manifest's canonical signature encoding.
    /// See the type remarks for why DER is not accepted here.
    /// </summary>
    public required byte[] Signature { get; init; }

    /// <summary>
    /// An optional, operator-facing key label copied verbatim into the envelope's
    /// <c>keyId</c>. Purely informational — never trusted, never affects verification. Defaults to empty
    /// to preserve the historical wire bytes of the built-in signer.
    /// </summary>
    public string KeyId { get; init; } = string.Empty;

    /// <summary>
    /// The algorithm this signature was produced with (PQ-hybrid Stage 1). Defaults to
    /// <see cref="SignatureAlgorithms.EcdsaP256"/> so every existing provider — including third-party
    /// <see cref="ISignatureProvider"/> implementations compiled before this member existed — keeps
    /// meaning exactly what it meant. An ML-DSA provider sets <see cref="SignatureAlgorithms.MlDsa65"/>
    /// (or the actual FIPS 204 parameter-set name of its key); for those, <see cref="Signature"/> is the
    /// raw FIPS 204 signature over the raw message under <see cref="SignatureAlgorithms.ManifestContext"/>
    /// (no SHA-256 pre-hash, no P1363/low-S — those are ECDSA-only concepts).
    /// </summary>
    public string Algorithm { get; init; } = SignatureAlgorithms.EcdsaP256;
}
