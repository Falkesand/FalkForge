namespace FalkForge.Signing;

/// <summary>
/// The signature algorithm identifiers and the ML-DSA signing context shared by the build-time
/// signer and the runtime verifier. These strings are part of the envelope wire contract: they
/// appear in the per-entry <c>algorithm</c> field and select the verification path, so they are
/// frozen once the first bundle carrying them ships.
/// </summary>
public static class SignatureAlgorithms
{
    /// <summary>
    /// Classical ECDSA over NIST P-256 — the historical (and default) manifest signature algorithm.
    /// An envelope entry with no <c>algorithm</c> field means this value, so every envelope signed
    /// before the field existed keeps its exact meaning.
    /// </summary>
    public const string EcdsaP256 = "ECDSA-P256";

    /// <summary>
    /// ML-DSA-65 (FIPS 204, Category 3) — the default post-quantum manifest signature algorithm
    /// (PQ-hybrid design §2.1). The parameter set travels in the wire value, so ML-DSA-44/-87
    /// remain expressible later without a wire change; 65 is what the shipped signer emits.
    /// </summary>
    public const string MlDsa65 = "ML-DSA-65";

    /// <summary>
    /// The FIPS 204 context string for every FalkForge manifest ML-DSA signature — domain
    /// separation guaranteeing a manifest signature can never be replayed as any other FalkForge
    /// ML-DSA artifact signature.
    ///
    /// <para><b>FROZEN FOREVER.</b> This value is part of the signed-bytes contract from the first
    /// shipped hybrid bundle onward (human decision, PQ-hybrid design §8.3). Changing it invalidates
    /// every ML-DSA signature ever produced. Do not touch.</para>
    /// </summary>
    public static ReadOnlySpan<byte> ManifestContext => "falkforge/manifest"u8;
}
