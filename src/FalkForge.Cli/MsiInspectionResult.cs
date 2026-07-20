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

    /// <summary>The declared fingerprint(s) of the envelope's CLASSICAL (ECDSA-P256) signature
    /// entries, as written by the signer — displayed as-is, not re-derived or checked against a
    /// trust anchor. These are the fingerprints a <c>forge verify --trusted-key</c> value must match;
    /// see <see cref="PqCompanionFingerprints"/> for the entries it does NOT match.</summary>
    public IReadOnlyList<string> SignatureFingerprints { get; init; } = [];

    /// <summary>The declared fingerprint(s) of any hybrid post-quantum (ML-DSA) companion signature
    /// entries. Merge Gate nit: shown under a separate label from <see cref="SignatureFingerprints"/>
    /// — a zero-config Integrity() build on a PQ-capable machine signs with both a classical and an
    /// ML-DSA key, and <c>forge verify --trusted-key</c> only ever matches the classical fingerprint
    /// (see <c>PqCompanionVerifier.IsClassicalEntry</c>); showing both under one "Signing Key
    /// Fingerprint" label would let an operator copy-paste the wrong one into <c>--trusted-key</c>
    /// and get a baffling INT001.</summary>
    public IReadOnlyList<string> PqCompanionFingerprints { get; init; } = [];
}
