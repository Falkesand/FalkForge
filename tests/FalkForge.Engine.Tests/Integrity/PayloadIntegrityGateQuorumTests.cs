namespace FalkForge.Engine.Tests.Integrity;

using System.Security.Cryptography;
using FalkForge.Engine.Integrity;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

/// <summary>
/// C19 quorum enforcement at the fresh-install gate (<see cref="PayloadIntegrityGate"/>). A fresh install
/// is always the Install operation; under the baked default that is one release signature, and an un-roled
/// key defaults to Release, so backward compatibility is preserved. Once roles are configured the gate
/// enforces the Install rule — a developer-only bundle (which must never satisfy a production operation)
/// is rejected with INT010.
/// </summary>
public sealed class PayloadIntegrityGateQuorumTests
{
    private static string Fingerprint(ECDsa key)
        => Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));

    private static InstallerManifest ManifestSignedBy(ECDsa key)
    {
        var files = new[] { new ManifestFileEntry { Name = "A", Sha256 = "AABB" } };
        var envelope = IntegrityEnvelopeCodec.Sign(files, key);
        return new InstallerManifest
        {
            Name = "App",
            Manufacturer = "Mfg",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerUser,
            Packages = [new PackageInfo
            {
                Id = "A",
                Type = PackageType.MsiPackage,
                DisplayName = "A",
                SourcePath = "C:/cache/A.msi",
                Sha256Hash = "AABB"
            }],
            ManifestSignature = IntegrityEnvelopeCodec.Serialize(envelope)
        };
    }

    private static TrustPolicy RoleConfiguredInstall(ECDsa key, TrustRole role)
    {
        var trusted = new HashSet<string>(new[] { Fingerprint(key) }, StringComparer.OrdinalIgnoreCase);
        var roles = new Dictionary<string, TrustRole>(StringComparer.OrdinalIgnoreCase)
        {
            [Fingerprint(key)] = role,
        };
        return TrustPolicy.FreshInstall(trusted, roles, BakedTrustPolicy.Default);
    }

    [Fact]
    public void RoleConfigured_ReleaseKey_Install_Passes()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = ManifestSignedBy(key);

        var result = PayloadIntegrityGate.Verify(manifest, RoleConfiguredInstall(key, TrustRole.Release));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void RoleConfigured_DeveloperOnlyKey_Install_RejectedInt010()
    {
        // A developer key never satisfies a production operation (§3.1). Install requires release.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = ManifestSignedBy(key);

        var result = PayloadIntegrityGate.Verify(manifest, RoleConfiguredInstall(key, TrustRole.Developer));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT010", result.Error.Message, StringComparison.Ordinal);
    }

    private static InstallerManifest ManifestSignedBy(int epoch, params ECDsa[] keys)
    {
        var files = new[] { new ManifestFileEntry { Name = "A", Sha256 = "AABB" } };
        var envelope = IntegrityEnvelopeCodec.Sign(files, keys, epoch, revoked: []);
        return new InstallerManifest
        {
            Name = "App",
            Manufacturer = "Mfg",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerUser,
            Packages = [new PackageInfo
            {
                Id = "A",
                Type = PackageType.MsiPackage,
                DisplayName = "A",
                SourcePath = "C:/cache/A.msi",
                Sha256Hash = "AABB"
            }],
            ManifestSignature = IntegrityEnvelopeCodec.Serialize(envelope)
        };
    }

    private static TrustPolicy UpdatePolicy(int storedEpoch, params (ECDsa key, TrustRole role)[] items)
    {
        var trusted = new HashSet<string>(items.Select(i => Fingerprint(i.key)), StringComparer.OrdinalIgnoreCase);
        var roles = items.ToDictionary(i => Fingerprint(i.key), i => i.role, StringComparer.OrdinalIgnoreCase);
        return TrustPolicy.RequireSignedUpdate(trusted, roles, BakedTrustPolicy.Default, storedEpoch);
    }

    // ── Update-path operation resolution (quorum uniformity with the update verifiers) ──
    // The pipeline's apply-time gate runs on the require-signed update path too (the path that advances
    // the persisted anti-downgrade epoch after a completed apply). It must resolve the operation from the
    // signed epoch relative to the stored epoch — Update vs KeyChange — instead of assuming Install, or a
    // single compromised release key could jam the epoch under the weakest rule.

    [Fact]
    public void UpdatePath_EpochAboveStored_SingleReleaseKey_RejectedInt010()
    {
        using var release = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = ManifestSignedBy(epoch: 7, release);

        var result = PayloadIntegrityGate.Verify(manifest, UpdatePolicy(2, (release, TrustRole.Release)));

        Assert.True(result.IsFailure, "a single release key must not satisfy the key-change quorum");
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT010", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdatePath_EpochAboveStored_ReleasePlusRecovery_Accepts()
    {
        using var release = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var recovery = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = ManifestSignedBy(epoch: 7, release, recovery);

        var result = PayloadIntegrityGate.Verify(
            manifest, UpdatePolicy(2, (release, TrustRole.Release), (recovery, TrustRole.Recovery)));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void UpdatePath_SameEpoch_SingleReleaseKey_Accepts()
    {
        // The routine update: same key-epoch resolves to Update (one release signature).
        using var release = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = ManifestSignedBy(epoch: 5, release);

        var result = PayloadIntegrityGate.Verify(manifest, UpdatePolicy(5, (release, TrustRole.Release)));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void UpdatePath_EpochBelowStored_RejectedInt008()
    {
        // Anti-downgrade at the apply-time gate: a replayed superseded release is rejected.
        using var release = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = ManifestSignedBy(epoch: 3, release);

        var result = PayloadIntegrityGate.Verify(manifest, UpdatePolicy(5, (release, TrustRole.Release)));

        Assert.True(result.IsFailure);
        Assert.Contains("INT008", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdatePath_UnsignedManifest_RejectedInt007()
    {
        // The update-path policy is require-signed: a stripped signature is itself a rejection.
        using var release = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = ManifestSignedBy(epoch: 5, release) with { ManifestSignature = null };

        var result = PayloadIntegrityGate.Verify(manifest, UpdatePolicy(5, (release, TrustRole.Release)));

        Assert.True(result.IsFailure);
        Assert.Contains("INT007", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NoRolesConfigured_TableSupplied_FallsToC14VerifyAny()
    {
        // The default PipelineContext policy carries the baked table but EMPTY roles (un-migrated engine).
        // The gate must take the C14 verify-any path, so a trusted single-signature bundle still installs.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = ManifestSignedBy(key);

        var trusted = new HashSet<string>(new[] { Fingerprint(key) }, StringComparer.OrdinalIgnoreCase);
        var policy = TrustPolicy.FreshInstall(
            trusted,
            new Dictionary<string, TrustRole>(StringComparer.OrdinalIgnoreCase), // no roles
            BakedTrustPolicy.Default);

        var result = PayloadIntegrityGate.Verify(manifest, policy);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }
}
