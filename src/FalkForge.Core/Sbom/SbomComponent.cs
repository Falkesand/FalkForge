namespace FalkForge.Sbom;

public sealed class SbomComponent
{
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required SbomComponentType Type { get; init; }
    public required string Sha256Hash { get; init; }
    public string? Publisher { get; init; }

    /// <summary>
    /// SHA-1 hex digest of exactly the bytes <see cref="Sha256Hash"/> covers, or
    /// <see langword="null"/> when the producer never observed them.
    ///
    /// <para><b>Required for SPDX output of a <see cref="SbomComponentType.File"/> component.</b>
    /// SPDX 2.3 §8.4 fixes a file's checksum cardinality at "1..1 for the SHA1 algorithm", so
    /// <see cref="SpdxSbomGenerator"/> refuses to emit a document for a file component that leaves
    /// this null rather than produce something that claims to be SPDX 2.3 and is not. Components of
    /// any other type become SPDX packages, whose checksums are optional (§7.10), so they may leave
    /// it null. CycloneDX output ignores this field entirely.</para>
    ///
    /// <para>SHA-1 is collision-broken. It appears here only because SPDX mandates it as a file
    /// identifier; no FalkForge trust decision is made on it — the signed payload manifest and
    /// <c>MsiIntegrityVerifier</c> both use <see cref="Sha256Hash"/>.</para>
    /// </summary>
    public string? Sha1Hash { get; init; }
}
