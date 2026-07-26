using System.Security.Cryptography;
using FalkForge;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Integrity;

/// <summary>
/// End-to-end quorum enforcement through the real update-path gate (<see cref="SignedPayloadTocVerifier"/>):
/// when a per-operation policy table is supplied, the collect-all-distinct + quorum rule replaces C14's
/// verify-any. These tests encode the C19 guarantee — a single compromised key cannot ship a key change,
/// and a bare threshold below the required distinct-signature count is rejected (INT010). The distinct-key
/// rule is asserted through the gate, not just the evaluator, so the whole path is covered.
/// </summary>
public sealed class SignedPayloadTocVerifierQuorumTests
{
    private const string HashA = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private static string Fingerprint(ECDsa key)
        => Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));

    private static IReadOnlySet<string> TrustSet(params ECDsa[] keys)
        => new HashSet<string>(keys.Select(Fingerprint), StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, TrustRole> Roles(params (ECDsa key, TrustRole role)[] items)
        => items.ToDictionary(i => Fingerprint(i.key), i => i.role, StringComparer.OrdinalIgnoreCase);

    private static InstallerManifest SignedManifest(int epoch, params ECDsa[] keys)
    {
        var files = new[] { new ManifestFileEntry { Name = "AppMsi", Sha256 = HashA } };
        var envelope = IntegrityEnvelopeCodec.Sign(files, keys, epoch, revoked: []);
        return new InstallerManifest
        {
            Name = "T",
            Manufacturer = "M",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = [new PackageInfo
            {
                Id = "AppMsi",
                Type = PackageType.MsiPackage,
                DisplayName = "AppMsi",
                SourcePath = "AppMsi.msi",
                Sha256Hash = HashA
            }],
            ManifestSignature = IntegrityEnvelopeCodec.Serialize(envelope)
        };
    }

    private static TocEntry Toc() => new()
    {
        PackageId = "AppMsi",
        Offset = 0,
        CompressedSize = 1,
        OriginalSize = 1,
        Sha256Hash = HashA
    };

    // A policy table whose Update rule is a bare 2-distinct-release threshold, for the M-of-N tests.
    private static IReadOnlyDictionary<OperationKind, PolicyRule> TwoReleaseTable()
        => new Dictionary<OperationKind, PolicyRule>
        {
            [OperationKind.Install] = new([new RoleRequirement(TrustRole.Release, 1)], 1),
            [OperationKind.Update] = new([new RoleRequirement(TrustRole.Release, 2)], 2),
            [OperationKind.KeyChange] = BakedTrustPolicy.Default[OperationKind.KeyChange],
            [OperationKind.Downgrade] = BakedTrustPolicy.Default[OperationKind.Downgrade],
            [OperationKind.Revoke] = BakedTrustPolicy.Default[OperationKind.Revoke],
        };

    // ── M-of-N threshold at the gate ─────────────────────────────────────────

    [Fact]
    public void Update_RequiresTwoDistinct_TwoKeysSign_Accepts()
    {
        using var k1 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var k2 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = SignedManifest(epoch: 0, k1, k2);

        var result = SignedPayloadTocVerifier.Verify(
            manifest, [Toc()], TrustSet(k1, k2), requireSigned: true, storedEpoch: 0,
            revokedFingerprints: null,
            policyTable: TwoReleaseTable(),
            roles: Roles((k1, TrustRole.Release), (k2, TrustRole.Release)));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void Update_RequiresTwoDistinct_OneKeySigns_RejectedInt010()
    {
        using var k1 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = SignedManifest(epoch: 0, k1);

        var result = SignedPayloadTocVerifier.Verify(
            manifest, [Toc()], TrustSet(k1), requireSigned: true, storedEpoch: 0,
            revokedFingerprints: null,
            policyTable: TwoReleaseTable(),
            roles: Roles((k1, TrustRole.Release)));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT010", result.Error.Message, StringComparison.Ordinal);
    }

    // ── KeyChange (rotation) role requirement, resolved from epoch ────────────

    [Fact]
    public void KeyChange_ReleaseOnly_RejectedInt010_MissingRecovery()
    {
        // epoch (6) above stored (5) resolves to KeyChange, which the baked default requires
        // release + recovery. A release-only rotation cannot re-anchor trust alone.
        using var release = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = SignedManifest(epoch: 6, release);

        var result = SignedPayloadTocVerifier.Verify(
            manifest, [Toc()], TrustSet(release), requireSigned: true, storedEpoch: 5,
            revokedFingerprints: null,
            policyTable: BakedTrustPolicy.Default,
            roles: Roles((release, TrustRole.Release)));

        Assert.True(result.IsFailure);
        Assert.Contains("INT010", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void KeyChange_ReleasePlusRecovery_Accepts()
    {
        using var release = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var recovery = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = SignedManifest(epoch: 6, release, recovery);

        var result = SignedPayloadTocVerifier.Verify(
            manifest, [Toc()], TrustSet(release, recovery), requireSigned: true, storedEpoch: 5,
            revokedFingerprints: null,
            policyTable: BakedTrustPolicy.Default,
            roles: Roles((release, TrustRole.Release), (recovery, TrustRole.Recovery)));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void KeyChange_SingleKeyHoldingBothRoles_RejectedInt010_DistinctKeyRule()
    {
        // One key tagged release|recovery must NOT satisfy the two-distinct-key rotation rule alone.
        using var dual = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = SignedManifest(epoch: 6, dual);

        var result = SignedPayloadTocVerifier.Verify(
            manifest, [Toc()], TrustSet(dual), requireSigned: true, storedEpoch: 5,
            revokedFingerprints: null,
            policyTable: BakedTrustPolicy.Default,
            roles: Roles((dual, TrustRole.Release | TrustRole.Recovery)));

        Assert.True(result.IsFailure, "one key holding both roles must not satisfy a two-distinct-key rule");
        Assert.Contains("INT010", result.Error.Message, StringComparison.Ordinal);
    }

    // ── Normal forward update resolves to Update (1 release) ──────────────────

    [Fact]
    public void ForwardUpdate_SameEpoch_ResolvesUpdate_SingleReleaseAccepts()
    {
        using var release = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = SignedManifest(epoch: 5, release);

        var result = SignedPayloadTocVerifier.Verify(
            manifest, [Toc()], TrustSet(release), requireSigned: true, storedEpoch: 5,
            revokedFingerprints: null,
            policyTable: BakedTrustPolicy.Default,
            roles: Roles((release, TrustRole.Release)));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    // ── Backward compatibility: default policy + un-roled key = C14 ───────────

    [Fact]
    public void BackwardCompat_DefaultPolicy_UnroledKey_ExistingBundleStillVerifies()
    {
        // An existing single-signature bundle from one trusted key, with NO roles configured for it, must
        // still verify under the default policy: install/update need one release, and an un-roled key
        // resolves to Release (the roleOf default). This is exactly C14 behavior through the quorum path.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = SignedManifest(epoch: 5, key);

        var result = SignedPayloadTocVerifier.Verify(
            manifest, [Toc()], TrustSet(key), requireSigned: true, storedEpoch: 5,
            revokedFingerprints: null,
            policyTable: BakedTrustPolicy.Default,
            roles: new Dictionary<string, TrustRole>(StringComparer.OrdinalIgnoreCase)); // no roles → default Release

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void QuorumPath_RevokedKeyDroppedBeforeCounting_FailsThreshold()
    {
        // A locally-revoked key must not count toward a quorum even if still trusted. Two keys sign, one is
        // revoked → only one usable → fails the 2-distinct threshold with INT010.
        using var k1 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var k2 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = SignedManifest(epoch: 0, k1, k2);
        var revoked = new HashSet<string>(new[] { Fingerprint(k2) }, StringComparer.OrdinalIgnoreCase);

        var result = SignedPayloadTocVerifier.Verify(
            manifest, [Toc()], TrustSet(k1, k2), requireSigned: true, storedEpoch: 0,
            revokedFingerprints: revoked,
            policyTable: TwoReleaseTable(),
            roles: Roles((k1, TrustRole.Release), (k2, TrustRole.Release)));

        Assert.True(result.IsFailure);
        Assert.Contains("INT010", result.Error.Message, StringComparison.Ordinal);
    }

    // ── DropRevoked filter direction (highest-priority mutation-testing finding) ──────────────
    //
    // DropRevoked (SignedPayloadTocVerifier private helper) strips locally-revoked fingerprints from
    // the collected signature set BEFORE the quorum evaluator ever sees it. A negated filter ("keep
    // ONLY revoked signatures") passes the existing suite because every prior revocation test uses
    // same-role signers, where dropping-the-revoked-one and keeping-only-the-revoked-one both end up
    // short of the same threshold. These tests use signers with DISJOINT roles so the quorum OUTCOME
    // itself flips depending on which signer survives the filter — that is what makes the filter's
    // direction observable.

    // An Update rule that needs exactly one Recovery-rostered signer (Release alone does not satisfy
    // it), so the outcome depends on which of two disjoint-role signers survives DropRevoked.
    private static IReadOnlyDictionary<OperationKind, PolicyRule> RecoveryOnlyUpdateTable()
        => new Dictionary<OperationKind, PolicyRule>
        {
            [OperationKind.Install] = new([new RoleRequirement(TrustRole.Release, 1)], 1),
            [OperationKind.Update] = new([new RoleRequirement(TrustRole.Recovery, 1)], 1),
            [OperationKind.KeyChange] = BakedTrustPolicy.Default[OperationKind.KeyChange],
            [OperationKind.Downgrade] = BakedTrustPolicy.Default[OperationKind.Downgrade],
            [OperationKind.Revoke] = BakedTrustPolicy.Default[OperationKind.Revoke],
        };

    [Fact]
    public void QuorumPath_RevokedSignerDropped_SurvivingCoSignerSatisfiesQuorum()
    {
        // k1 (revoked) holds ONLY Release; k2 (still trusted) holds ONLY Recovery. The Update rule
        // needs a Recovery signer. Dropping the revoked k1 correctly leaves k2 (Recovery) — quorum is
        // satisfied. A negated filter would keep ONLY k1 (Release) and drop k2, so the Recovery
        // requirement would go unmet and this would fail instead.
        using var k1Revoked = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var k2Good = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = SignedManifest(epoch: 0, k1Revoked, k2Good);
        var revoked = new HashSet<string>(new[] { Fingerprint(k1Revoked) }, StringComparer.OrdinalIgnoreCase);

        var result = SignedPayloadTocVerifier.Verify(
            manifest, [Toc()], TrustSet(k1Revoked, k2Good), requireSigned: true, storedEpoch: 0,
            revokedFingerprints: revoked,
            policyTable: RecoveryOnlyUpdateTable(),
            roles: Roles((k1Revoked, TrustRole.Release), (k2Good, TrustRole.Recovery)));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void QuorumPath_EmptyRevokedSet_KeepsEverySignature_TwoReleaseThresholdMet()
    {
        // An empty (non-null) revoked set must be a no-op: DropRevoked's short-circuit returns the
        // input list unchanged, so a genuine two-distinct-release quorum still satisfies exactly as it
        // would with revokedFingerprints: null.
        using var k1 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var k2 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = SignedManifest(epoch: 0, k1, k2);

        var result = SignedPayloadTocVerifier.Verify(
            manifest, [Toc()], TrustSet(k1, k2), requireSigned: true, storedEpoch: 0,
            revokedFingerprints: new HashSet<string>(StringComparer.OrdinalIgnoreCase), // empty, not null
            policyTable: TwoReleaseTable(),
            roles: Roles((k1, TrustRole.Release), (k2, TrustRole.Release)));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void QuorumPath_AllTrustedSignaturesRevoked_FailsThreshold_Int010()
    {
        // Every trusted signer is locally revoked: DropRevoked must strip all of them, leaving zero
        // usable signatures. The min-distinct floor can never be met on an empty set, so the documented
        // outcome is a threshold failure (INT010) — revocation must never be diluted into "some signers
        // still count."
        using var k1 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var k2 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = SignedManifest(epoch: 0, k1, k2);
        var revoked = new HashSet<string>(
            new[] { Fingerprint(k1), Fingerprint(k2) }, StringComparer.OrdinalIgnoreCase);

        var result = SignedPayloadTocVerifier.Verify(
            manifest, [Toc()], TrustSet(k1, k2), requireSigned: true, storedEpoch: 0,
            revokedFingerprints: revoked,
            policyTable: TwoReleaseTable(),
            roles: Roles((k1, TrustRole.Release), (k2, TrustRole.Release)));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT010", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void QuorumPath_RevokedFingerprintMatchedCaseInsensitively_ProductionComparerHonored()
    {
        // TrustStateStore persists revocations in a StringComparer.OrdinalIgnoreCase set (§6.2).
        // DropRevoked itself does no casing normalization — it relies entirely on the caller's set
        // comparer. Pin that a differently-cased revoked fingerprint still drops the (always
        // uppercase-hex) fingerprint the codec computes, matching the exact comparer production wires.
        using var k1Revoked = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var k2Good = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = SignedManifest(epoch: 0, k1Revoked, k2Good);
        var revoked = new HashSet<string>(
            new[] { Fingerprint(k1Revoked).ToLowerInvariant() }, StringComparer.OrdinalIgnoreCase);

        var result = SignedPayloadTocVerifier.Verify(
            manifest, [Toc()], TrustSet(k1Revoked, k2Good), requireSigned: true, storedEpoch: 0,
            revokedFingerprints: revoked,
            policyTable: RecoveryOnlyUpdateTable(),
            roles: Roles((k1Revoked, TrustRole.Release), (k2Good, TrustRole.Recovery)));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }
}
