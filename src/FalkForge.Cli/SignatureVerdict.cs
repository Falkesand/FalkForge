namespace FalkForge.Cli;

/// <summary>
/// The outcome of <see cref="MsiIntegrityVerifier.Verify"/>.
/// </summary>
public enum SignatureVerdict
{
    /// <summary>The signature cryptographically verifies and the MSI's actual embedded payload
    /// matches every hash it declares.</summary>
    Verified,

    /// <summary>Neither the embedded <c>_FalkForgeIntegrity</c> table nor a detached
    /// <c>.sig.json</c> sidecar carries a signature for this MSI.</summary>
    NotSigned,

    /// <summary>A signature was found but did not verify, matched no trusted key, or the MSI's
    /// actual payload does not match the hashes the signature declares.</summary>
    Failed
}
