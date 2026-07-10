using System.Security.Cryptography;
using System.Text.Json;
using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine.Integrity;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Signing;
using Xunit;

namespace FalkForge.Integration.Tests;

/// <summary>
/// PQ-hybrid Stage 3 ergonomics, end to end: a bundle authored with the fluent
/// <c>BundleBuilder.Integrity(i =&gt; i.HybridKey(classicalPem, pqPem))</c> API must compile into a
/// real bundle whose manifest envelope carries BOTH signature entries (classical ECDSA-P256 first,
/// then ML-DSA-65) over the same signed bytes — and that bundle must pass the engine's real trust
/// gate (<see cref="BundleTrustGate"/>) when the pair is companion-pinned via
/// <see cref="EngineTrustAnchor.TrustHybridKey"/>. This is the full authoring→compile→verify loop
/// a hybrid publisher ships with.
/// </summary>
public sealed class HybridBundleFluentEndToEndTests : IDisposable
{
    private readonly string _tempDir;

    public HybridBundleFluentEndToEndTests()
    {
        EngineTrustAnchor.ResetForTests();
        _tempDir = Path.Combine(Path.GetTempPath(), $"HybridFluentE2E_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        EngineTrustAnchor.ResetForTests();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void HybridKeyBundle_CarriesBothSignatures_AndPassesTrustGate_WhenCompanionPinned()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");

        // 1. The publisher's hybrid identity: one classical ECDSA-P256 key + its ML-DSA-65 companion.
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var mldsa = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
        var classicalPem = Path.Combine(_tempDir, "classical.pem");
        var pqPem = Path.Combine(_tempDir, "mldsa.pem");
        File.WriteAllText(classicalPem, ecdsa.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(pqPem, mldsa.ExportPkcs8PrivateKeyPem());

        // 2. Author + compile the bundle through the fluent hybrid API.
        var payloadPath = Path.Combine(_tempDir, "App.msi");
        File.WriteAllBytes(payloadPath, RandomNumberGenerator.GetBytes(512));

        var model = new BundleBuilder()
            .Name("HybridFluent")
            .Manufacturer("Integration Tests")
            .Version("1.0.0")
            .UseSilentUI()
            .Integrity(i => i.HybridKey(classicalPem, pqPem))
            .Chain(chain => chain.MsiPackage(payloadPath, pkg => pkg.Id("AppMsi").Version("1.0.0")))
            .Build();

        var buildResult = new BundleCompiler().Compile(model, Path.Combine(_tempDir, "out"));
        Assert.True(buildResult.IsSuccess, buildResult.IsFailure ? buildResult.Error.Message : null);

        // 3. The compiled bundle's envelope carries both entries, classical first, correct identities.
        var content = PayloadEmbedder.Extract(buildResult.Value);
        Assert.True(content.IsSuccess, content.IsFailure ? content.Error.Message : null);
        var manifest = JsonSerializer.Deserialize<InstallerManifest>(content.Value.ManifestJsonBytes!)!;
        Assert.NotNull(manifest.ManifestSignature);

        var envelope = IntegrityEnvelopeCodec.Parse(manifest.ManifestSignature!)!;
        Assert.Equal(2, envelope.Signatures.Count);
        Assert.Null(envelope.Signatures[0].Algorithm); // classical first (absent field = ECDSA-P256)
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(ecdsa.ExportSubjectPublicKeyInfo())),
            envelope.Signatures[0].Fingerprint);
        Assert.Equal(IntegrityEnvelopeCodec.MlDsa65AlgorithmId, envelope.Signatures[1].Algorithm);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(mldsa.ExportSubjectPublicKeyInfo())),
            envelope.Signatures[1].Fingerprint);

        // Both signatures cover the SAME signed bytes.
        var message = IntegrityEnvelopeCodec.ComputeSignedBytes(envelope.Files, envelope.Epoch, envelope.Revoked);
        using var pqPub = MLDsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(envelope.Signatures[1].PublicKey));
        Assert.True(pqPub.VerifyData(message, Convert.FromBase64String(envelope.Signatures[1].Signature),
            SignatureAlgorithms.ManifestContext));

        // 4. Pin the hybrid pair in the engine's trust anchor and run the REAL trust gate: the
        //    classical entry must verify AND its pinned ML-DSA companion must verify (INT011 rule).
        EngineTrustAnchor.TrustHybridKey(
            ecdsa.ExportSubjectPublicKeyInfo(), mldsa.ExportSubjectPublicKeyInfo());

        var gate = BundleTrustGate.Verify(
            manifest, content.Value.TocEntries, requireSigned: false, new TrustState());

        Assert.True(gate.IsSuccess, gate.IsFailure ? gate.Error.Message : null);
    }
}
