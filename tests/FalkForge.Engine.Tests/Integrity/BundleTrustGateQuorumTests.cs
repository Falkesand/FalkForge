namespace FalkForge.Engine.Tests.Integrity;

using System.Security.Cryptography;
using System.Text.Json;
using FalkForge.Engine.Integrity;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

/// <summary>
/// C19 quorum uniformity at the bootstrap/extract trust gate (<see cref="BundleTrustGate"/>).
///
/// <para><b>Why this matters.</b> The key-role quorum policy must be enforced on EVERY path that verifies
/// a bundle against the persisted anti-downgrade store, not only the in-app auto-updater
/// (<see cref="StagedUpdateVerifier"/>). A bundle delivered out-of-band (manual run, IT push) and executed
/// with <c>--require-signed</c> never passes through the staged-update verifier, so if this gate took the
/// C14 verify-any path an attacker holding ONE compromised release key could sign a bundle with an
/// arbitrarily high key-epoch and — after a completed apply — advance the persisted epoch under the weak
/// single-signature rule, bypassing the release+recovery quorum a key change requires and permanently
/// locking out all future lower-epoch updates (INT008 denial of service). The gate must resolve the
/// operation from the signed epoch relative to the stored epoch exactly as the auto-updater does.</para>
///
/// <para>The engine test assembly disables xUnit parallelization; the process-global anchor is reset
/// before and after every test.</para>
/// </summary>
public sealed class BundleTrustGateQuorumTests : IDisposable
{
    private const string HashA = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    public BundleTrustGateQuorumTests() => EngineTrustAnchor.ResetForTests();

    public void Dispose() => EngineTrustAnchor.ResetForTests();

    private static string Fingerprint(ECDsa key)
        => Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));

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

    private static TrustState StoredEpoch(int epoch) => new() { Epoch = epoch };

    // ── The CVE regression: out-of-band epoch jam via a single compromised release key ──

    [Fact]
    public void RequireSigned_EpochAboveStored_SingleReleaseKey_RejectedInt010()
    {
        // A signed epoch above the stored epoch is a key-change operation. Under the baked default policy
        // that demands release + recovery; one compromised release key must NOT be able to advance the
        // epoch on the bootstrapper path any more than it can on the auto-updater path.
        using var release = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        EngineTrustAnchor.TrustFingerprint(Fingerprint(release), TrustRole.Release);
        var manifest = SignedManifest(epoch: 7, release);

        var result = BundleTrustGate.Verify(manifest, [Toc()], requireSigned: true, StoredEpoch(2));

        Assert.True(result.IsFailure, "a single release key must not satisfy the key-change quorum");
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT010", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequireSigned_EpochAboveStored_ReleasePlusRecovery_Accepts()
    {
        // The legitimate rotation: a dual-signed (release + recovery) bundle satisfies the key-change
        // quorum, so a real publisher rotation still installs out-of-band.
        using var release = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var recovery = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        EngineTrustAnchor.TrustFingerprint(Fingerprint(release), TrustRole.Release);
        EngineTrustAnchor.TrustFingerprint(Fingerprint(recovery), TrustRole.Recovery);
        var manifest = SignedManifest(epoch: 7, release, recovery);

        var result = BundleTrustGate.Verify(manifest, [Toc()], requireSigned: true, StoredEpoch(2));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    // ── Legitimate flows must keep working ───────────────────────────────────

    [Fact]
    public void RequireSigned_SameEpoch_SingleReleaseKey_Accepts()
    {
        // A routine update (same key-epoch) resolves to the Update operation: one release signature,
        // exactly the C14 behavior a normal single-key publisher relies on.
        using var release = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        EngineTrustAnchor.TrustFingerprint(Fingerprint(release), TrustRole.Release);
        var manifest = SignedManifest(epoch: 5, release);

        var result = BundleTrustGate.Verify(manifest, [Toc()], requireSigned: true, StoredEpoch(5));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void RequireSigned_UnroledTrustedKey_SameEpoch_Accepts()
    {
        // An un-migrated publisher (trusted key with no explicit role) defaults to Release (§7.1), so a
        // routine single-signature update still verifies — backward compatibility through the quorum path.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        EngineTrustAnchor.TrustFingerprint(Fingerprint(key));
        var manifest = SignedManifest(epoch: 0, key);

        var result = BundleTrustGate.Verify(manifest, [Toc()], requireSigned: true, StoredEpoch(0));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void RequireSigned_EpochBelowStored_RejectedInt008()
    {
        // Anti-downgrade stays enforced through the quorum path (a replayed superseded release is INT008).
        using var release = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        EngineTrustAnchor.TrustFingerprint(Fingerprint(release), TrustRole.Release);
        var manifest = SignedManifest(epoch: 3, release);

        var result = BundleTrustGate.Verify(manifest, [Toc()], requireSigned: true, StoredEpoch(5));

        Assert.True(result.IsFailure);
        Assert.Contains("INT008", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FreshInstall_NotRequireSigned_HighEpochSingleKey_StillInstalls()
    {
        // A fresh install (no --require-signed) never consults or advances the store; a signed bundle from
        // one trusted key installs regardless of its epoch. The quorum policy must not be applied here —
        // this is the backward-compatible fresh-install posture, and the store cannot be jammed from it.
        using var release = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        EngineTrustAnchor.TrustFingerprint(Fingerprint(release), TrustRole.Release);
        var manifest = SignedManifest(epoch: 7, release);

        var result = BundleTrustGate.Verify(manifest, [Toc()], requireSigned: false, new TrustState());

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void ContentOverload_RequireSigned_EpochAboveStored_SingleReleaseKey_RejectedInt010()
    {
        // The --extract self-extraction path (BundleContent overload) must enforce the same quorum rule.
        using var release = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        EngineTrustAnchor.TrustFingerprint(Fingerprint(release), TrustRole.Release);
        var manifest = SignedManifest(epoch: 7, release);
        var content = new BundleContent
        {
            TocEntries = [Toc()],
            BundlePath = "unused.exe",
            ManifestJsonBytes = JsonSerializer.SerializeToUtf8Bytes(manifest)
        };

        var result = BundleTrustGate.Verify(content, requireSigned: true, StoredEpoch(2));

        Assert.True(result.IsFailure, "the extract path must enforce the same key-change quorum");
        Assert.Contains("INT010", result.Error.Message, StringComparison.Ordinal);
    }
}
