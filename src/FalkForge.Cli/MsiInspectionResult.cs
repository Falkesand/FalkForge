namespace FalkForge.Cli;

/// <summary>
/// Contains metadata extracted from an MSI database during inspection.
/// </summary>
public sealed class MsiInspectionResult
{
    public string? ProductName { get; init; }
    public string? Manufacturer { get; init; }
    public string? Version { get; init; }
    public string? ProductCode { get; init; }
    public IReadOnlyList<string> TableNames { get; init; } = [];
    public int TableCount { get; init; }

    /// <summary>True when the MSI carries a <c>_FalkForgeIntegrity</c>/<c>ManifestSignature</c> row.
    /// Presence only — this is a display field, not a cryptographic check; use
    /// <see cref="MsiIntegrityVerifier"/> to actually verify the signature.</summary>
    public bool SignaturePresent { get; init; }

    /// <summary>The signature row's <c>Format</c> column (e.g. <c>falkforge-ecdsa-envelope-v2</c>),
    /// or null when <see cref="SignaturePresent"/> is false.</summary>
    public string? SignatureFormatTag { get; init; }

    /// <summary>The declared fingerprint(s) from the envelope's signature entries, as written by
    /// the signer — displayed as-is, not re-derived or checked against a trust anchor.</summary>
    public IReadOnlyList<string> SignatureFingerprints { get; init; } = [];
}
