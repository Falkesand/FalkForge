namespace FalkForge.Engine.Tests.Integrity;

using System.Security.Cryptography;
using FalkForge.Engine.Integrity;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Signing;
using Xunit;

/// <summary>
/// PQ-hybrid Stage 1 threading through the Engine's gates: the companion map pinned in
/// <see cref="EngineTrustAnchor"/> / <see cref="TrustPolicy"/> reaches the envelope verifier on
/// every path that establishes trust — the apply-time <see cref="PayloadIntegrityGate"/> and the
/// bootstrap/self-extract <see cref="BundleTrustGate"/>. A hybrid-pinned publisher whose ML-DSA
/// companion is stripped fails INT011 at the gate, and the incapable-OS fallback logs loudly
/// through the gate's sink. (The staged-update path routes through the same
/// SignedPayloadTocVerifier and is covered by the codec + toc-verifier tests.)
/// </summary>
public sealed class PqHybridGateTests : IDisposable
{
    public PqHybridGateTests() => EngineTrustAnchor.ResetForTests();

    public void Dispose() => EngineTrustAnchor.ResetForTests();

    private static string Fingerprint(byte[] spki) => Convert.ToHexString(SHA256.HashData(spki));

    private static (InstallerManifest Manifest, string ClassicalFp, string PqFp) HybridSignedManifest(
        bool stripPqEntry, params (string id, string hash)[] payloads)
    {
        using var classical = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var pq = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);

        var files = payloads.Select(p => new ManifestFileEntry { Name = p.id, Sha256 = p.hash }).ToList();
        var envelope = IntegrityEnvelopeCodec.Sign(files, classical);

        if (!stripPqEntry)
        {
            var message = IntegrityEnvelopeCodec.ComputeSignedBytes(files, envelope.Epoch, envelope.Revoked);
            var pqSpki = pq.ExportSubjectPublicKeyInfo();
            var signature = new byte[pq.Algorithm.SignatureSizeInBytes];
            pq.SignData(message, signature, SignatureAlgorithms.ManifestContext);
            envelope.Signatures =
            [
                envelope.Signatures[0],
                new SignatureEntry
                {
                    KeyId = "pq",
                    Fingerprint = Fingerprint(pqSpki),
                    PublicKey = Convert.ToBase64String(pqSpki),
                    Signature = Convert.ToBase64String(signature),
                    Algorithm = IntegrityEnvelopeCodec.MlDsa65AlgorithmId
                }
            ];
        }

        var packages = payloads.Select(p => new PackageInfo
        {
            Id = p.id,
            Type = PackageType.MsiPackage,
            DisplayName = p.id,
            SourcePath = p.id + ".msi",
            Sha256Hash = p.hash
        }).ToArray();

        var manifest = new InstallerManifest
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

        return (manifest,
            Fingerprint(classical.ExportSubjectPublicKeyInfo()),
            Fingerprint(pq.ExportSubjectPublicKeyInfo()));
    }

    private static TrustPolicy HybridFreshInstallPolicy(
        string classicalFp, string pqFp, Func<bool>? isPqSupported = null)
    {
        var trusted = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { classicalFp };
        var companions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [classicalFp] = pqFp
        };
        return TrustPolicy.FreshInstall(trusted, companions, isPqSupported);
    }

    // ── PayloadIntegrityGate (the apply-time gate) ────────────────────────────

    [Fact]
    public void PayloadIntegrityGate_HybridComplete_Accepts()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");
        var (manifest, classicalFp, pqFp) = HybridSignedManifest(stripPqEntry: false, ("PkgA", "AABB"));

        var result = PayloadIntegrityGate.Verify(manifest, HybridFreshInstallPolicy(classicalFp, pqFp));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void PayloadIntegrityGate_StrippedPqCompanion_RejectsWithInt011()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");
        var (manifest, classicalFp, pqFp) = HybridSignedManifest(stripPqEntry: true, ("PkgA", "AABB"));

        var result = PayloadIntegrityGate.Verify(manifest, HybridFreshInstallPolicy(classicalFp, pqFp));

        Assert.True(result.IsFailure,
            "the apply-time gate must reject a hybrid-pinned publisher whose PQ companion was stripped");
        Assert.Contains("INT011", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PayloadIntegrityGate_IncapableOs_AcceptsOnClassical_AndLogsThroughSink()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");
        var (manifest, classicalFp, pqFp) = HybridSignedManifest(stripPqEntry: true, ("PkgA", "AABB"));
        var logged = new List<string>();

        var result = PayloadIntegrityGate.Verify(
            manifest,
            HybridFreshInstallPolicy(classicalFp, pqFp, isPqSupported: static () => false),
            logged.Add);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Contains(logged, m => m.Contains("PQ", StringComparison.Ordinal));
    }

    // ── BundleTrustGate (bootstrap / self-extract paths) ──────────────────────

    [Fact]
    public void BundleTrustGate_AnchorPinnedHybrid_StrippedPq_RejectsWithInt011()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");
        var (manifest, classicalFp, pqFp) = HybridSignedManifest(stripPqEntry: true, ("PkgA", "AABB"));
        EngineTrustAnchor.TrustHybridFingerprint(classicalFp, pqFp);
        var toc = new[] { new TocEntry { PackageId = "PkgA", Offset = 0, CompressedSize = 1, OriginalSize = 1, Sha256Hash = "AABB" } };

        var result = BundleTrustGate.Verify(manifest, toc, requireSigned: false, new TrustState());

        Assert.True(result.IsFailure,
            "the bootstrap gate must enforce the anchor's companion pins");
        Assert.Contains("INT011", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BundleTrustGate_AnchorPinnedHybrid_CompleteEnvelope_Accepts()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");
        var (manifest, classicalFp, pqFp) = HybridSignedManifest(stripPqEntry: false, ("PkgA", "AABB"));
        EngineTrustAnchor.TrustHybridFingerprint(classicalFp, pqFp);
        var toc = new[] { new TocEntry { PackageId = "PkgA", Offset = 0, CompressedSize = 1, OriginalSize = 1, Sha256Hash = "AABB" } };

        var result = BundleTrustGate.Verify(manifest, toc, requireSigned: false, new TrustState());

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }
}
