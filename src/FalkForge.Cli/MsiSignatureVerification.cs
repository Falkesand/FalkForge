namespace FalkForge.Cli;

/// <summary>
/// Result of verifying an MSI's pure-.NET ECDSA integrity signature (<see
/// cref="MsiIntegrityVerifier"/>). Carries enough detail for <c>forge verify</c> to print a
/// PASS/FAIL diagnostic without the caller re-deriving anything cryptographic.
/// </summary>
public sealed class MsiSignatureVerification
{
    /// <summary>The overall PASS/FAIL/NOT-SIGNED outcome.</summary>
    public required SignatureVerdict Verdict { get; init; }

    /// <summary>The <c>Format</c> column value of the <c>ManifestSignature</c> row (e.g.
    /// <c>falkforge-ecdsa-envelope-v2</c>), or null when the signature came from a sidecar (which
    /// carries no format column) or no signature was found.</summary>
    public string? FormatTag { get; init; }

    /// <summary>Where the signature envelope was read from — the embedded table or the detached
    /// sidecar — for display only.</summary>
    public string? Source { get; init; }

    /// <summary>The fingerprint of the signature that established a <see
    /// cref="SignatureVerdict.Verified"/> outcome with authorship (a matching <c>--trusted-key</c>
    /// was supplied). Null when unset (failed/not-signed) or when verification was consistency-only
    /// (no trusted keys supplied, so no fingerprint was matched against a trust anchor).</summary>
    public string? MatchedFingerprint { get; init; }

    /// <summary>Payload file names whose actual embedded content did not match the hash the
    /// signature declares. Empty unless the content-binding check found a discrepancy.</summary>
    public IReadOnlyList<string> MismatchedFiles { get; init; } = [];

    /// <summary>Human-readable summary of the outcome.</summary>
    public required string Message { get; init; }
}
