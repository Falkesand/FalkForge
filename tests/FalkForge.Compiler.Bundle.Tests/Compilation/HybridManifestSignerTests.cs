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
    public void Sign_HybridKeyPaths_EmitsClassicalAndPqEntries_BothVerify()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");

        // Stage 3 ergonomics: the config shape IntegrityBuilder.HybridKey(classical, pq) produces —
        // a classical key path plus its ML-DSA companion path — must yield one classical and one
        // algorithm-tagged ML-DSA entry over the same canonical message.
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var mldsa = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
        var classicalPem = Path.Combine(_tempDir, "classical.pem");
        var pqPem = Path.Combine(_tempDir, "mldsa.pem");
        File.WriteAllText(classicalPem, ecdsa.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(pqPem, mldsa.ExportPkcs8PrivateKeyPem());
        var config = new IntegrityConfiguration
        {
            SigningKeyPaths = [classicalPem],
            PqSigningKeyPaths = [pqPem]
        };

        var result = EcdsaManifestSigner.Sign(Entries(("PkgA", "AABBCC")), config);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var envelope = IntegrityEnvelopeCodec.Parse(result.Value)!;
        Assert.Equal(2, envelope.Signatures.Count);

        var classical = envelope.Signatures[0];
        Assert.Null(classical.Algorithm);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(ecdsa.ExportSubjectPublicKeyInfo())),
            classical.Fingerprint);
        Assert.True(IntegrityEnvelopeCodec.VerifySignature(envelope));

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
    public void Sign_PqKeyWithoutAnyClassicalKey_FailsLoudWithSgn012()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");

        // Hybrid requires both halves (design §2.2): ML-DSA entries are companions consulted only
        // AFTER a classical entry verifies — they are never matched against the trust set on their
        // own. An envelope whose only signature is ML-DSA could therefore never verify on ANY
        // engine; emitting it silently would ship an unverifiable artifact, so the signer fails
        // loud instead.
        using var mldsa = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
        var pqPem = Path.Combine(_tempDir, "mldsa-only.pem");
        File.WriteAllText(pqPem, mldsa.ExportPkcs8PrivateKeyPem());
        var config = new IntegrityConfiguration { PqSigningKeyPaths = [pqPem] };

        var result = EcdsaManifestSigner.Sign(Entries(("PkgA", "AABBCC")), config);

        Assert.True(result.IsFailure, "a PQ-only envelope is unverifiable by design and must be rejected");
        Assert.Contains("SGN012", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Sign_PqOnlyCustomProvider_FailsLoudWithSgn012()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");

        // Same guard for the provider seam: a custom provider list that yields only ML-DSA
        // signatures produces an unverifiable envelope and must be rejected at assembly time.
        using var mldsa = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
        var config = new IntegrityConfiguration
        {
            SignatureProviders = new ISignatureProvider[]
            {
                MLDsaPemSignatureProvider.FromPemContent(mldsa.ExportPkcs8PrivateKeyPem())
            }
        };

        var result = EcdsaManifestSigner.Sign(Entries(("PkgA", "AABBCC")), config);

        Assert.True(result.IsFailure, "a PQ-only envelope is unverifiable by design and must be rejected");
        Assert.Contains("SGN012", result.Error.Message, StringComparison.Ordinal);
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
