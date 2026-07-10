using System.Security.Cryptography;
using FalkForge;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Integrity;

/// <summary>
/// Proves the trust binding that closes the "signed bundle, TOC-hash tamper" hole: runtime
/// payload extraction verifies each payload's bytes against the (unsigned, appended-overlay)
/// <see cref="TocEntry.Sha256Hash"/>. Because the ECDSA manifest signature covers only the
/// manifest's <see cref="PackageInfo.Sha256Hash"/> values — never the overlay TOC — an attacker
/// could flip payload bytes and rewrite the matching TOC hash without invalidating the signature,
/// and the tampered bytes would extract and execute.
///
/// <para><see cref="SignedPayloadTocVerifier"/> binds the value the extractor trusts to the signed
/// hash: for a full payload that is <see cref="TocEntry.Sha256Hash"/>; for a delta payload it is
/// <see cref="TocEntry.ReconstructedSha256Hash"/> (the finished-file hash the reconstruction is
/// checked against). A TOC that disagrees with the signed manifest is rejected before any byte is
/// extracted. Stage 1 (C14) additionally requires the manifest signature to come from a trusted key.</para>
/// </summary>
public sealed class SignedPayloadTocVerifierTests
{
    private const string HashA = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string HashB = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    // Consistency-only: an engine with no baked publisher key. Preserves the pre-pin TOC-binding intent
    // for the byte-binding tests below.
    private static readonly IReadOnlySet<string> NoTrust = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static string Fingerprint(ECDsa key)
        => Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));

    private static IReadOnlySet<string> TrustSet(params string[] fps)
        => new HashSet<string>(fps, StringComparer.OrdinalIgnoreCase);

    private static InstallerManifest SignedManifest(ECDsa key, params (string id, string signedHash)[] payloads)
    {
        var files = payloads
            .Select(p => new ManifestFileEntry { Name = p.id, Sha256 = p.signedHash })
            .ToList();

        var envelope = IntegrityEnvelopeCodec.Sign(files, key);

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

    private static TocEntry FullEntry(string id, string tocHash) => new()
    {
        PackageId = id,
        Offset = 0,
        CompressedSize = 1,
        OriginalSize = 1,
        Sha256Hash = tocHash
    };

    private static TocEntry DeltaEntry(string id, string reconstructedHash) => new()
    {
        PackageId = id,
        Offset = 0,
        CompressedSize = 1,
        OriginalSize = 1,
        Sha256Hash = "0000000000000000000000000000000000000000000000000000000000000000", // delta-blob hash (unsigned, irrelevant to trust)
        IsDelta = true,
        BaseSha256Hash = HashB,
        ReconstructedSha256Hash = reconstructedHash
    };

    [Fact]
    public void CleanFullPayload_TocHashMatchesSignedHash_Accepts()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = SignedManifest(key, ("AppMsi", HashA));

        var result = SignedPayloadTocVerifier.Verify(manifest, new[] { FullEntry("AppMsi", HashA) }, TrustSet(Fingerprint(key)));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void TamperedFullPayload_TocHashDiffersFromSignedHash_Rejected()
    {
        // Attacker flipped the payload bytes and rewrote the (unsigned) TOC hash to match the
        // tampered bytes. The signed manifest still carries the original hash. Extraction would
        // verify bytes==TOC (HashB) and accept the tampered payload — the verifier must refuse.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = SignedManifest(key, ("AppMsi", HashA));

        var result = SignedPayloadTocVerifier.Verify(manifest, new[] { FullEntry("AppMsi", HashB) }, TrustSet(Fingerprint(key)));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT006", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CleanDeltaPayload_ReconstructedHashMatchesSignedHash_Accepts()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = SignedManifest(key, ("AppMsi", HashA));

        var result = SignedPayloadTocVerifier.Verify(manifest, new[] { DeltaEntry("AppMsi", HashA) }, TrustSet(Fingerprint(key)));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void TamperedDeltaPayload_ReconstructedHashDiffersFromSignedHash_Rejected()
    {
        // For a delta payload the finished bytes are checked against ReconstructedSha256Hash.
        // If that (unsigned TOC) value is rewritten to match a tampered reconstruction, the
        // reconstruction would pass its own gate — the binding to the signed hash must reject it.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = SignedManifest(key, ("AppMsi", HashA));

        var result = SignedPayloadTocVerifier.Verify(manifest, new[] { DeltaEntry("AppMsi", HashB) }, TrustSet(Fingerprint(key)));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT006", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsignedManifest_PassesThrough_ForBackwardCompatibility()
    {
        var manifest = new InstallerManifest
        {
            Name = "T",
            Manufacturer = "M",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = [],
            ManifestSignature = null
        };

        var result = SignedPayloadTocVerifier.Verify(manifest, new[] { FullEntry("AppMsi", HashB) }, NoTrust);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void UnsignedManifest_WithRequireSigned_Rejected_Int007()
    {
        // The require-signed seam (Stage 2 update path): an absent signature is refused.
        var manifest = new InstallerManifest
        {
            Name = "T",
            Manufacturer = "M",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = [],
            ManifestSignature = null
        };

        var result = SignedPayloadTocVerifier.Verify(
            manifest, new[] { FullEntry("AppMsi", HashB) }, NoTrust, requireSigned: true);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT007", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SignedByUntrustedKey_Rejected_Int001()
    {
        // A validly self-signed bundle whose key is NOT in the baked set is rejected (re-sign attack).
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = SignedManifest(key, ("AppMsi", HashA));

        var result = SignedPayloadTocVerifier.Verify(
            manifest, new[] { FullEntry("AppMsi", HashA) }, TrustSet(Fingerprint(other)));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT001", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnmatchedTocEntry_InSignedBundle_Rejected_CoverageExtension()
    {
        // §5.4 coverage extension (C14 Stage 2): every payload a signed bundle extracts/executes must
        // be inside the signed set. An attacker who appends an extra executable TOC entry — e.g. a
        // malicious "Setup.exe" that RunAsBootstrapper would pick up and launch as the UI exe — leaves
        // it OUTSIDE the signed set. The old "skip unmatched" behavior extracted and ran it; the gate
        // must now reject any TOC payload a signed bundle does not cover.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = SignedManifest(key, ("AppMsi", HashA));

        var result = SignedPayloadTocVerifier.Verify(manifest, new[]
        {
            FullEntry("AppMsi", HashA),
            FullEntry("Setup.exe", HashB) // appended, unsigned; would be launched as the UI exe
        }, TrustSet(Fingerprint(key)));

        Assert.True(result.IsFailure, "an unsigned TOC payload in a signed bundle must be rejected");
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT004", result.Error.Message, StringComparison.Ordinal);
    }

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

    [Fact]
    public void EpochBelowStored_Rejected_Int008()
    {
        // Anti-downgrade (§6.3): the client has already accepted epoch 5. A replay of an older-epoch
        // (epoch 2) signed release — e.g. one signed by a since-revoked key — is refused.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = SignedManifestWithEpoch(key, epoch: 2, revoked: [], ("AppMsi", HashA));

        var result = SignedPayloadTocVerifier.Verify(
            manifest, new[] { FullEntry("AppMsi", HashA) }, TrustSet(Fingerprint(key)),
            requireSigned: true, storedEpoch: 5, revokedFingerprints: null);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT008", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EpochAtOrAboveStored_Accepted()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = SignedManifestWithEpoch(key, epoch: 5, revoked: [], ("AppMsi", HashA));

        var result = SignedPayloadTocVerifier.Verify(
            manifest, new[] { FullEntry("AppMsi", HashA) }, TrustSet(Fingerprint(key)),
            requireSigned: true, storedEpoch: 5, revokedFingerprints: null);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void RevokedFingerprint_Rejected_Int001_EvenWhenTrusted()
    {
        // A key still present in the baked trusted set but recorded as revoked in the persisted store is
        // refused (§6.3 step 3) — the revocation overrides the stale baked trust.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = SignedManifestWithEpoch(key, epoch: 0, revoked: [], ("AppMsi", HashA));

        var result = SignedPayloadTocVerifier.Verify(
            manifest, new[] { FullEntry("AppMsi", HashA) }, TrustSet(Fingerprint(key)),
            requireSigned: true, storedEpoch: 0, revokedFingerprints: TrustSet(Fingerprint(key)));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT001", result.Error.Message, StringComparison.Ordinal);
    }

    private static InstallerManifest MultiSignedManifest(
        IReadOnlyList<ECDsa> keys, params (string id, string signedHash)[] payloads)
    {
        var files = payloads
            .Select(p => new ManifestFileEntry { Name = p.id, Sha256 = p.signedHash })
            .ToList();

        var envelope = IntegrityEnvelopeCodec.Sign(files, keys, epoch: 0, revoked: []);

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

    [Fact]
    public void DualSigned_RevokedOldKeyListedFirst_GoodNewKey_Accepted()
    {
        // Availability: a legit rotation bundle is dual-signed [old, new]. When the old key
        // has since been revoked locally, the verify-any path must keep iterating to the
        // still-good new signature instead of rejecting on the first (revoked) match — the
        // quorum path's DropRevoked already behaves this way.
        using var oldKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var newKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = MultiSignedManifest([oldKey, newKey], ("AppMsi", HashA));

        var result = SignedPayloadTocVerifier.Verify(
            manifest, new[] { FullEntry("AppMsi", HashA) },
            TrustSet(Fingerprint(oldKey), Fingerprint(newKey)),
            requireSigned: true, storedEpoch: 0,
            revokedFingerprints: TrustSet(Fingerprint(oldKey)));

        Assert.True(result.IsSuccess,
            result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void DualSigned_AllTrustedKeysRevoked_Rejected_Int001()
    {
        // Revocation must not be weakened: a bundle carrying ONLY revoked trusted
        // signatures is still rejected.
        using var oldKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var newKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = MultiSignedManifest([oldKey, newKey], ("AppMsi", HashA));

        var result = SignedPayloadTocVerifier.Verify(
            manifest, new[] { FullEntry("AppMsi", HashA) },
            TrustSet(Fingerprint(oldKey), Fingerprint(newKey)),
            requireSigned: true, storedEpoch: 0,
            revokedFingerprints: TrustSet(Fingerprint(oldKey), Fingerprint(newKey)));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT001", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("revoked", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmptyTrustedSet_WithRequireSigned_ValidSelfSigned_Rejected_FailClosed_Int009()
    {
        // B1 fail-open (C14 Stage 3 FIX 2): an engine built with NO baked trusted keys has an empty
        // trusted set. With require-signed on (the update path), consistency-only "accept ANY
        // self-verifying signature" would let an attacker re-sign a rewritten update with their own fresh
        // key and have it accepted. Require-signed with no trust anchor cannot establish authorship, so it
        // must FAIL CLOSED — not fall back to accept-any.
        using var attackerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = SignedManifest(attackerKey, ("AppMsi", HashA));

        var result = SignedPayloadTocVerifier.Verify(
            manifest, new[] { FullEntry("AppMsi", HashA) }, NoTrust, requireSigned: true);

        Assert.True(result.IsFailure, "require-signed with an empty trusted set must fail closed");
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT009", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyTrustedSet_WithoutRequireSigned_ValidSelfSigned_StillPasses_ConsistencyOnly()
    {
        // The empty-set consistency-only acceptance is permissible ONLY off the require-signed path
        // (fresh install / inspection-grade CLI): a validly self-signed bundle whose TOC binds still
        // passes. FIX 2 must NOT change this direction.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = SignedManifest(key, ("AppMsi", HashA));

        var result = SignedPayloadTocVerifier.Verify(
            manifest, new[] { FullEntry("AppMsi", HashA) }, NoTrust, requireSigned: false);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void NonEmptyTrustedSet_TrustedSig_WithRequireSigned_Passes()
    {
        // A properly-provisioned engine (baked trusted key present) still accepts a trusted-signed update
        // on the require-signed path — FIX 2 only closes the empty-set case.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = SignedManifest(key, ("AppMsi", HashA));

        var result = SignedPayloadTocVerifier.Verify(
            manifest, new[] { FullEntry("AppMsi", HashA) }, TrustSet(Fingerprint(key)), requireSigned: true);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void InvalidSignature_Rejected()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = SignedManifest(key, ("AppMsi", HashA));

        // Corrupt the signed envelope: flip a signed file hash so the ECDSA signature no longer
        // verifies over the file list.
        var tampered = manifest with
        {
            ManifestSignature = manifest.ManifestSignature!.Replace(HashA, HashB, StringComparison.Ordinal)
        };

        var result = SignedPayloadTocVerifier.Verify(tampered, new[] { FullEntry("AppMsi", HashB) }, TrustSet(Fingerprint(key)));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
    }
}
