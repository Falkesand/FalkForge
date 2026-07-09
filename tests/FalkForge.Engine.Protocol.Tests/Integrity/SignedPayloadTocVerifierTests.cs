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
/// extracted.</para>
/// </summary>
public sealed class SignedPayloadTocVerifierTests
{
    private const string HashA = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string HashB = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    private static InstallerManifest SignedManifest(params (string id, string signedHash)[] payloads)
    {
        var files = payloads
            .Select(p => new ManifestFileEntry { Name = p.id, Sha256 = p.signedHash })
            .ToList();

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
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
        var manifest = SignedManifest(("AppMsi", HashA));

        var result = SignedPayloadTocVerifier.Verify(manifest, new[] { FullEntry("AppMsi", HashA) });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void TamperedFullPayload_TocHashDiffersFromSignedHash_Rejected()
    {
        // Attacker flipped the payload bytes and rewrote the (unsigned) TOC hash to match the
        // tampered bytes. The signed manifest still carries the original hash. Extraction would
        // verify bytes==TOC (HashB) and accept the tampered payload — the verifier must refuse.
        var manifest = SignedManifest(("AppMsi", HashA));

        var result = SignedPayloadTocVerifier.Verify(manifest, new[] { FullEntry("AppMsi", HashB) });

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("INT006", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CleanDeltaPayload_ReconstructedHashMatchesSignedHash_Accepts()
    {
        var manifest = SignedManifest(("AppMsi", HashA));

        var result = SignedPayloadTocVerifier.Verify(manifest, new[] { DeltaEntry("AppMsi", HashA) });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void TamperedDeltaPayload_ReconstructedHashDiffersFromSignedHash_Rejected()
    {
        // For a delta payload the finished bytes are checked against ReconstructedSha256Hash.
        // If that (unsigned TOC) value is rewritten to match a tampered reconstruction, the
        // reconstruction would pass its own gate — the binding to the signed hash must reject it.
        var manifest = SignedManifest(("AppMsi", HashA));

        var result = SignedPayloadTocVerifier.Verify(manifest, new[] { DeltaEntry("AppMsi", HashB) });

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
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

        var result = SignedPayloadTocVerifier.Verify(manifest, new[] { FullEntry("AppMsi", HashB) });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void PayloadNotInSignedSet_IsNotBoundHere_Accepts()
    {
        // The UI/engine infrastructure payloads are not part of the signed package set. They are
        // outside this binding's scope (verified against the TOC only) and must not fail the gate.
        var manifest = SignedManifest(("AppMsi", HashA));

        var result = SignedPayloadTocVerifier.Verify(manifest, new[]
        {
            FullEntry("AppMsi", HashA),
            FullEntry("FalkForge.Ui.exe", HashB)
        });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void InvalidSignature_Rejected()
    {
        var manifest = SignedManifest(("AppMsi", HashA));

        // Corrupt the signed envelope: flip a signed file hash so the ECDSA signature no longer
        // verifies over the file list.
        var tampered = manifest with
        {
            ManifestSignature = manifest.ManifestSignature!.Replace(HashA, HashB, StringComparison.Ordinal)
        };

        var result = SignedPayloadTocVerifier.Verify(tampered, new[] { FullEntry("AppMsi", HashB) });

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
    }
}
