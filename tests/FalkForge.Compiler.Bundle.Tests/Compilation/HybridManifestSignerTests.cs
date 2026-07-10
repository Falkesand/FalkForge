using System.Security.Cryptography;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Models;
using FalkForge.Signing;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Compilation;

/// <summary>
/// PQ-hybrid Stage 1, signing side: the envelope assembler dispatches per
/// <see cref="ProviderSignature.Algorithm"/> — classical entries keep the exact low-S ECDSA path,
/// ML-DSA entries are emitted verbatim with the <c>algorithm</c> wire field set, and classical
/// entries are ordered first so the first-wins verifier hits the cheap path. The zero-config
/// ephemeral signer emits a hybrid pair (human decision §8.7), keeping the dev loop on the same
/// envelope shape production uses.
/// </summary>
public sealed class HybridManifestSignerTests : IDisposable
{
    private readonly string _tempDir;

    public HybridManifestSignerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"HybridSignerTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static IReadOnlyList<PayloadHashEntry> Entries(params (string id, string hash)[] items)
    {
        var list = new List<PayloadHashEntry>(items.Length);
        foreach (var (id, hash) in items)
            list.Add(new PayloadHashEntry(id, hash));
        return list;
    }

    [Fact]
    public void Sign_Ephemeral_EmitsHybridPair_ClassicalFirst()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");

        var result = EcdsaManifestSigner.Sign(Entries(("PkgA", "AABBCC")), config: null);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var envelope = IntegrityEnvelopeCodec.Parse(result.Value)!;
        Assert.Equal(2, envelope.Signatures.Count);

        // Classical first: the first-wins verifier should hit the cheap ECDSA path.
        var classical = envelope.Signatures[0];
        Assert.Null(classical.Algorithm);
        Assert.Equal(64, Convert.FromBase64String(classical.Signature).Length);

        var pq = envelope.Signatures[1];
        Assert.Equal(IntegrityEnvelopeCodec.MlDsa65AlgorithmId, pq.Algorithm);

        // The PQ entry verifies over the SAME canonical message, under the manifest context.
        var message = IntegrityEnvelopeCodec.ComputeSignedBytes(envelope.Files, envelope.Epoch, envelope.Revoked);
        using var pub = MLDsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(pq.PublicKey));
        Assert.True(pub.VerifyData(message, Convert.FromBase64String(pq.Signature), SignatureAlgorithms.ManifestContext));

        // And the classical entry still self-verifies through the real codec.
        Assert.True(IntegrityEnvelopeCodec.VerifySignature(envelope));
    }

    [Fact]
    public void Sign_WithMlDsaPemProvider_EmitsAlgorithmTaggedEntry_ThatIsNotLowSCanonicalized()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");

        // A configured hybrid: one classical PEM key + one ML-DSA PEM provider. The ML-DSA signature
        // must be emitted byte-verbatim (running a 3309-byte signature through the 64-byte low-S
        // canonicalizer would corrupt nothing today — it returns non-64-byte inputs unchanged — but
        // the entry must be tagged and verifiable, which proves the dispatch path).
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var mldsa = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
        var ecdsaPem = Path.Combine(_tempDir, "classical.pem");
        File.WriteAllText(ecdsaPem, ecdsa.ExportPkcs8PrivateKeyPem());
        var config = new IntegrityConfiguration
        {
            SigningKeyPath = ecdsaPem,
            SignatureProviders = new ISignatureProvider[]
            {
                MLDsaPemSignatureProvider.FromPemContent(mldsa.ExportPkcs8PrivateKeyPem())
            }
        };

        var result = EcdsaManifestSigner.Sign(Entries(("PkgA", "AABBCC")), config);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var envelope = IntegrityEnvelopeCodec.Parse(result.Value)!;
        Assert.Equal(2, envelope.Signatures.Count);

        Assert.Null(envelope.Signatures[0].Algorithm);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(ecdsa.ExportSubjectPublicKeyInfo())),
            envelope.Signatures[0].Fingerprint);

        var pq = envelope.Signatures[1];
        Assert.Equal(IntegrityEnvelopeCodec.MlDsa65AlgorithmId, pq.Algorithm);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(mldsa.ExportSubjectPublicKeyInfo())),
            pq.Fingerprint);

        var message = IntegrityEnvelopeCodec.ComputeSignedBytes(envelope.Files, envelope.Epoch, envelope.Revoked);
        using var pub = MLDsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(pq.PublicKey));
        Assert.True(pub.VerifyData(message, Convert.FromBase64String(pq.Signature), SignatureAlgorithms.ManifestContext));
    }

    [Fact]
    public void Sign_PqProviderListedBeforeClassical_StillOrdersClassicalEntriesFirst()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");

        // Classical-first ordering is pinned regardless of provider declaration order, so the
        // verifier's first-wins loop always meets the cheap ECDSA entry first.
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var mldsa = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
        var config = new IntegrityConfiguration
        {
            SignatureProviders = new ISignatureProvider[]
            {
                MLDsaPemSignatureProvider.FromPemContent(mldsa.ExportPkcs8PrivateKeyPem()),
                PemSignatureProvider.FromPemContent(ecdsa.ExportPkcs8PrivateKeyPem())
            }
        };

        var result = EcdsaManifestSigner.Sign(Entries(("PkgA", "AABBCC")), config);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var envelope = IntegrityEnvelopeCodec.Parse(result.Value)!;
        Assert.Equal(2, envelope.Signatures.Count);
        Assert.Null(envelope.Signatures[0].Algorithm);
        Assert.Equal(IntegrityEnvelopeCodec.MlDsa65AlgorithmId, envelope.Signatures[1].Algorithm);
    }
}
