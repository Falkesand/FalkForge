namespace FalkForge.Engine.Protocol.Manifest;

public sealed class PackageInfo
{
    public required string Id { get; init; }
    public required PackageType Type { get; init; }
    public required string DisplayName { get; init; }
    public string? Version { get; init; }
    public bool Vital { get; init; } = true;
    public required string SourcePath { get; init; }
    public required string Sha256Hash { get; init; }
    public Dictionary<string, string> Properties { get; init; } = new();
    public string? InstallCondition { get; init; }
    public IReadOnlyDictionary<int, ExitCodeBehavior>? ExitCodes { get; init; }
    public string? KbArticle { get; init; }
    public string? PatchCode { get; init; }
    public string? TargetProductCode { get; init; }
    public string? DownloadUrl { get; init; }
    public string? ContainerId { get; init; }
    public DetectionMode DetectionMode { get; init; } = DetectionMode.Default;
    public IReadOnlyList<SearchCondition> SearchConditions { get; init; } = [];
    public string? AuthenticodeThumbprint { get; init; }

    /// <summary>
    /// Optional publisher public-key pin for a remotely-downloaded payload (<see cref="DownloadUrl"/>).
    /// The value is the SHA-256 hash (hex, 64 chars) of the signer certificate's
    /// SubjectPublicKeyInfo (DER). When non-null, the engine — after the payload's SHA-256 matches —
    /// additionally requires the downloaded file to carry a valid Authenticode signature whose
    /// signer public key hashes to this value, and aborts the install (SecurityError) on an
    /// unsigned, invalid-signature, or wrong-signer payload.
    /// <para>
    /// Unlike <see cref="AuthenticodeThumbprint"/> (which pins the whole certificate and therefore
    /// breaks on certificate reissuance), a public-key pin survives certificate rotation as long as
    /// the publisher keeps the same key pair — the intended semantics for remote payloads whose bytes
    /// may update but whose publisher is fixed. Verification is Windows-only (WinVerifyTrust); on a
    /// platform without an Authenticode validator a set pin fails closed rather than being skipped.
    /// </para>
    /// </summary>
    public string? RemotePayloadCertificatePublicKey { get; init; }

    public bool IsPrerequisite { get; init; }
    public string? SlipstreamTargetId { get; init; }
    public bool Permanent { get; init; }
    public bool EnableFeatureSelection { get; init; }

    /// <summary>
    /// Processor architecture required by this package.
    /// <see cref="PackageArchitecture.Neutral"/> (the default) means no constraint.
    /// PlanStep validates this against the host OS architecture at plan time so that
    /// an incompatible package surfaces as <see cref="FalkForge.ErrorKind.ArchitectureMismatch"/>
    /// rather than MSI error 1603 at apply time.
    /// </summary>
    public PackageArchitecture Architecture { get; init; } = PackageArchitecture.Neutral;
}
