namespace FalkForge.Engine.Tests.Integrity;

using System.Security.Cryptography;
using FalkForge.Engine.Integrity;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

/// <summary>
/// <c>TrustPolicy.FromBakedKeys(requireSigned: true, isUpdatePath: false, ...)</c> used to fall through to
/// <see cref="TrustPolicy.FreshInstall(IReadOnlySet{string}, IReadOnlyDictionary{string, TrustRole}, IReadOnlyDictionary{OperationKind, PolicyRule}, IReadOnlyDictionary{string, string}?)"/>,
/// which silently drops <c>RequireSigned</c> back to false — a fail-open bug for a caller that needs
/// exactly (require-signed, fresh-install). <see cref="TrustPolicy.RequireSignedInstall"/> is the missing
/// third shape: a signature is mandatory, but the operation always resolves as Install (never Update or
/// KeyChange), so a single release key is enough. That is what tells it apart from
/// <see cref="TrustPolicy.RequireSignedUpdate"/>, which treats a fresh signed envelope at a non-zero epoch
/// as a key-change against the zero stored epoch and demands release+recovery.
/// </summary>
public sealed class TrustPolicyRequireSignedInstallTests
{
    private static string Fingerprint(ECDsa key)
        => Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));

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

    [Fact]
    public void SingleReleaseKey_FreshInstallEnvelope_Accepts_UnlikeRequireSignedUpdate()
    {
        // A fresh install signed at a non-zero epoch with one release key. RequireSignedUpdate resolves
        // this as a KeyChange against the zero stored epoch (epoch above stored) and demands
        // release+recovery, rejecting it with INT010. RequireSignedInstall never consults the stored
        // epoch (IsUpdatePath stays false) and resolves it as a plain Install, which needs one release
        // signature — the shape this bundle actually has.
        using var release = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = ManifestSignedBy(epoch: 7, release);
        var trusted = new HashSet<string>(new[] { Fingerprint(release) }, StringComparer.OrdinalIgnoreCase);
        var roles = new Dictionary<string, TrustRole>(StringComparer.OrdinalIgnoreCase)
        {
            [Fingerprint(release)] = TrustRole.Release,
        };

        var updateResult = PayloadIntegrityGate.Verify(
            manifest, TrustPolicy.RequireSignedUpdate(trusted, roles, BakedTrustPolicy.Default, storedEpoch: 0));
        Assert.True(updateResult.IsFailure, "RequireSignedUpdate should treat this as a key change and reject it");
        Assert.Contains("INT010", updateResult.Error.Message, StringComparison.Ordinal);

        var installResult = PayloadIntegrityGate.Verify(
            manifest, TrustPolicy.RequireSignedInstall(trusted, roles, BakedTrustPolicy.Default));

        Assert.True(installResult.IsSuccess, installResult.IsFailure ? installResult.Error.Message : null);
    }

    [Fact]
    public void EmptyTrustedSet_RejectedInt009()
    {
        using var release = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = ManifestSignedBy(epoch: 1, release);
        var trusted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roles = new Dictionary<string, TrustRole>(StringComparer.OrdinalIgnoreCase);

        var result = PayloadIntegrityGate.Verify(
            manifest, TrustPolicy.RequireSignedInstall(trusted, roles, BakedTrustPolicy.Default));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT009", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NullSignature_RejectedInt007()
    {
        using var release = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = ManifestSignedBy(epoch: 1, release) with { ManifestSignature = null };
        var trusted = new HashSet<string>(new[] { Fingerprint(release) }, StringComparer.OrdinalIgnoreCase);
        var roles = new Dictionary<string, TrustRole>(StringComparer.OrdinalIgnoreCase)
        {
            [Fingerprint(release)] = TrustRole.Release,
        };

        var result = PayloadIntegrityGate.Verify(
            manifest, TrustPolicy.RequireSignedInstall(trusted, roles, BakedTrustPolicy.Default));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT007", result.Error.Message, StringComparison.Ordinal);
    }
}
