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
