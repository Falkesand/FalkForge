using System.Security.Cryptography;
using FalkForge;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Integrity;

/// <summary>
/// C16 truthfulness: proves anti-downgrade (INT008) and revocation (INT001) now GENUINELY enforce because
/// the store actually advances. Before C16 the store never advanced past epoch 0 (the non-elevated write was
/// ACL-denied), so these could only be tested by hand-feeding a <c>storedEpoch</c>. Here the store is driven
/// through <see cref="TrustStateStore.Advance"/> — the exact operation the elevated <c>TrustStateAdvance</c>
/// command performs — and then the LOADED epoch/revocations feed the verifier, closing the
/// advance → load → enforce loop end-to-end.
/// </summary>
public sealed class TrustStoreEnforcementTests
{
    private const string HashA = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private static string TempStorePath() =>
        Path.Combine(Path.GetTempPath(), $"falk-trustenf-{Guid.NewGuid():N}", "trust-state.json");

    private static string Fingerprint(ECDsa key)
        => Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));

    private static InstallerManifest SignedManifestWithEpoch(
        ECDsa key, int epoch, IReadOnlyList<string> revoked, params (string id, string signedHash)[] payloads)
    {
        var files = payloads
            .Select(p => new ManifestFileEntry { Name = p.id, Sha256 = p.signedHash })
            .ToList();

        var envelope = IntegrityEnvelopeCodec.Sign(files, new[] { key }, epoch, revoked);

        var packages = payloads
            .Select(p => new PackageInfo
            {
                Id = p.id,
                Type = PackageType.MsiPackage,
                DisplayName = p.id,
                SourcePath = p.id + ".msi",
                Sha256Hash = p.signedHash
            })
            .ToArray();

        return new InstallerManifest
        {
            Name = "T",
            Manufacturer = "M",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = packages,
            ManifestSignature = IntegrityEnvelopeCodec.Serialize(envelope)
        };
    }

    private static Result<Unit> VerifyAgainstStore(InstallerManifest manifest, ECDsa key, string storePath)
    {
        // Load the persisted store exactly as the production update gate does, then feed its epoch +
        // revocations into the verifier.
        var state = TrustStateStore.Load(storePath);
        IReadOnlySet<string>? revoked = state.RevokedFingerprints.Length > 0
            ? new HashSet<string>(state.RevokedFingerprints, StringComparer.OrdinalIgnoreCase)
            : null;

        var toc = new[]
        {
            new FalkForge.Engine.Protocol.Bundle.TocEntry
            {
                PackageId = "AppMsi", Offset = 0, CompressedSize = 1, OriginalSize = 1, Sha256Hash = HashA
            }
        };

        return SignedPayloadTocVerifier.Verify(
            manifest, toc,
            new HashSet<string>(new[] { Fingerprint(key) }, StringComparer.OrdinalIgnoreCase),
            requireSigned: true, storedEpoch: state.Epoch, revokedFingerprints: revoked);
    }

    private static void PreProvision(string path) =>
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

    private static void TryCleanup(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (dir is not null && Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best effort */ }
    }

    [Fact]
    public void AdvancedStore_ThenReplayedOlderEpoch_Rejected_Int008()
    {
        var path = TempStorePath();
        PreProvision(path);
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            // The store genuinely advances to epoch 5 (the operation the elevated command performs).
            var advance = TrustStateStore.Advance(path, epoch: 5, revoked: []);
            Assert.True(advance.IsSuccess, advance.IsFailure ? advance.Error.Message : null);
            Assert.Equal(5, TrustStateStore.Load(path).Epoch);

            // A replayed older-epoch (2) signed release is now refused against the advanced store.
            var manifest = SignedManifestWithEpoch(key, epoch: 2, revoked: [], ("AppMsi", HashA));
            var result = VerifyAgainstStore(manifest, key, path);

            Assert.True(result.IsFailure);
            Assert.Contains("INT008", result.Error.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryCleanup(path);
        }
    }

    [Fact]
    public void AdvancedStore_ThenRevokedKeyBundle_Rejected_Int001()
    {
        var path = TempStorePath();
        PreProvision(path);
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var revokedFp = Fingerprint(key);

            // A verified update delivers + advances a revocation of the publisher key.
            var advance = TrustStateStore.Advance(path, epoch: 3, revoked: new[] { revokedFp });
            Assert.True(advance.IsSuccess, advance.IsFailure ? advance.Error.Message : null);
            Assert.Contains(revokedFp, TrustStateStore.Load(path).RevokedFingerprints);

            // A bundle signed only by the now-revoked key is refused, even though the key is still baked.
            var manifest = SignedManifestWithEpoch(key, epoch: 3, revoked: [], ("AppMsi", HashA));
            var result = VerifyAgainstStore(manifest, key, path);

            Assert.True(result.IsFailure);
            Assert.Contains("INT001", result.Error.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryCleanup(path);
        }
    }

    [Fact]
    public void AdvancedStore_ForwardUpdate_StillApplies_NoFalseRejection()
    {
        var path = TempStorePath();
        PreProvision(path);
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            var advance = TrustStateStore.Advance(path, epoch: 5, revoked: []);
            Assert.True(advance.IsSuccess, advance.IsFailure ? advance.Error.Message : null);

            // A genuine forward update (higher epoch, non-revoked key) must still verify — the anti-downgrade
            // gate must not reject legitimate updates.
            var manifest = SignedManifestWithEpoch(key, epoch: 6, revoked: [], ("AppMsi", HashA));
            var result = VerifyAgainstStore(manifest, key, path);

            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        }
        finally
        {
            TryCleanup(path);
        }
    }
}
