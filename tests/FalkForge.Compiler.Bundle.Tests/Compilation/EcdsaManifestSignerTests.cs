using System.Security.Cryptography;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Models;
using FalkForge.Signing;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Compilation;

/// <summary>
/// Unit A: pure-.NET ECDSA manifest signer.
/// Pins the wire contract the engine's PayloadIntegrityGate consumes: an envelope
/// carrying a base64 SubjectPublicKeyInfo public key, a list of {name, sha256}
/// entries, and a base64 ECDSA signature over SHA-256 of the canonically
/// serialized entries array. The signer must work WITHOUT the sigil CLI, using an
/// ephemeral P-256 key by default so zero-config tamper detection is possible.
/// Self-verification here uses <see cref="IntegrityEnvelopeCodec"/> — the same
/// canonical byte computation the engine verifier uses — so a green test proves
/// genuine signer/verifier compatibility, not a re-implementation that happens to agree.
/// </summary>
public sealed class EcdsaManifestSignerTests : IDisposable
{
    private readonly string _tempDir;

    public EcdsaManifestSignerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"EcdsaSignerTest_{Guid.NewGuid():N}");
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
    public void Sign_Ephemeral_ProducesEnvelopeThatSelfVerifies()
    {
        var entries = Entries(("PkgA", "AABBCC"), ("PkgB", "DDEEFF"));

        var result = EcdsaManifestSigner.Sign(entries, config: null);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var envelope = IntegrityEnvelopeCodec.Parse(result.Value);
        Assert.NotNull(envelope);
        Assert.Equal(2, envelope!.Version);
        var signature = Assert.Single(envelope.Signatures);
        Assert.False(string.IsNullOrEmpty(signature.PublicKey));
        Assert.False(string.IsNullOrEmpty(signature.Signature));
        Assert.False(string.IsNullOrEmpty(signature.Fingerprint));
        Assert.Equal(2, envelope.Files.Count);
        Assert.True(IntegrityEnvelopeCodec.VerifySignature(envelope));
    }

    [Fact]
    public void Sign_WithMultiplePemKeys_ProducesOneSignatureEntryPerKey()
    {
        // Rotation-safe dual-sign: two PEM keys each contribute a signature entry over the same files.
        using var k1 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var k2 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pem1 = Path.Combine(_tempDir, "k1.pem");
        var pem2 = Path.Combine(_tempDir, "k2.pem");
        File.WriteAllText(pem1, k1.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(pem2, k2.ExportPkcs8PrivateKeyPem());
        var config = new IntegrityConfiguration { SigningKeyPaths = new[] { pem1, pem2 } };

        var result = EcdsaManifestSigner.Sign(Entries(("PkgA", "AABBCC")), config);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var envelope = IntegrityEnvelopeCodec.Parse(result.Value)!;
        Assert.Equal(2, envelope.Signatures.Count);
        var fp1 = Convert.ToHexString(SHA256.HashData(k1.ExportSubjectPublicKeyInfo()));
        var fp2 = Convert.ToHexString(SHA256.HashData(k2.ExportSubjectPublicKeyInfo()));
        Assert.Equal(fp1, envelope.Signatures[0].Fingerprint);
        Assert.Equal(fp2, envelope.Signatures[1].Fingerprint);
        Assert.True(IntegrityEnvelopeCodec.VerifySignature(envelope));
    }

    [Fact]
    public void Sign_EntriesAppearInEnvelopeWithCorrectNamesAndHashes()
    {
        var entries = Entries(("Core.msi", "0011AABB"), ("Tools.exe", "CCDD2233"));

        var result = EcdsaManifestSigner.Sign(entries, config: null);

        Assert.True(result.IsSuccess);
        var envelope = IntegrityEnvelopeCodec.Parse(result.Value)!;
        Assert.Equal("Core.msi", envelope.Files[0].Name);
        Assert.Equal("0011AABB", envelope.Files[0].Sha256);
        Assert.Equal("Tools.exe", envelope.Files[1].Name);
    }

    [Fact]
    public void Sign_TwoEphemeralRuns_UseDifferentKeys()
    {
        // Ephemeral keys mean each build gets a unique key (design: key-compromise scope).
        var entries = Entries(("PkgA", "AABBCC"));

        var first = EcdsaManifestSigner.Sign(entries, config: null);
        var second = EcdsaManifestSigner.Sign(entries, config: null);

        var pub1 = IntegrityEnvelopeCodec.Parse(first.Value)!.Signatures[0].PublicKey;
        var pub2 = IntegrityEnvelopeCodec.Parse(second.Value)!.Signatures[0].PublicKey;
        Assert.NotEqual(pub1, pub2);
    }

    [Fact]
    public void Sign_WithPemKeyFile_UsesThatKeyDeterministically()
    {
        // A configured PEM key gives a stable public key across builds (authorship proof).
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pemPath = Path.Combine(_tempDir, "signing.pem");
        File.WriteAllText(pemPath, key.ExportPkcs8PrivateKeyPem());
        var config = new IntegrityConfiguration { SigningKeyPath = pemPath };
        var entries = Entries(("PkgA", "AABBCC"));

        var first = EcdsaManifestSigner.Sign(entries, config);
        var second = EcdsaManifestSigner.Sign(entries, config);

        Assert.True(first.IsSuccess);
        var expectedPub = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        Assert.Equal(expectedPub, IntegrityEnvelopeCodec.Parse(first.Value)!.Signatures[0].PublicKey);
        Assert.Equal(
            IntegrityEnvelopeCodec.Parse(first.Value)!.Signatures[0].PublicKey,
            IntegrityEnvelopeCodec.Parse(second.Value)!.Signatures[0].PublicKey);
        Assert.True(IntegrityEnvelopeCodec.VerifySignature(IntegrityEnvelopeCodec.Parse(first.Value)!));
    }

    [Fact]
    public void Sign_WithMissingKeyFile_FailsWithSgn002()
    {
        var config = new IntegrityConfiguration { SigningKeyPath = Path.Combine(_tempDir, "does-not-exist.pem") };
        var entries = Entries(("PkgA", "AABBCC"));

        var result = EcdsaManifestSigner.Sign(entries, config);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("SGN002", result.Error.Message);
    }

    [Fact]
    public void Sign_WithEpochAndRevocations_FoldsThemIntoSignedEnvelope()
    {
        // C14 Stage 2: a publisher can author an epoch-bearing, revocation-declaring bundle. The epoch and
        // revocation list must appear on the envelope AND be cryptographically covered (the envelope still
        // self-verifies, and — proven in the codec tests — tampering either breaks the signature).
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pemPath = Path.Combine(_tempDir, "rotate.pem");
        File.WriteAllText(pemPath, key.ExportPkcs8PrivateKeyPem());
        var config = new IntegrityConfiguration
        {
            SigningKeyPath = pemPath,
            Epoch = 4,
            RevokedFingerprints = new[] { "OLDKEYFINGERPRINT" }
        };

        var result = EcdsaManifestSigner.Sign(Entries(("PkgA", "AABBCC")), config);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var envelope = IntegrityEnvelopeCodec.Parse(result.Value)!;
        Assert.Equal(4, envelope.Epoch);
        Assert.Contains("OLDKEYFINGERPRINT", envelope.Revoked);
        Assert.True(IntegrityEnvelopeCodec.VerifySignature(envelope));
    }

    [Fact]
    public void Sign_EmptyEntries_StillProducesVerifiableEnvelope()
    {
        var result = EcdsaManifestSigner.Sign(Entries(), config: null);

        Assert.True(result.IsSuccess);
        var envelope = IntegrityEnvelopeCodec.Parse(result.Value)!;
        Assert.Empty(envelope.Files);
        Assert.True(IntegrityEnvelopeCodec.VerifySignature(envelope));
    }

    [Fact]
    public void Sign_WithCustomProvider_ProducesAnEntryThatVerifies()
    {
        // C17: a custom ISignatureProvider (stand-in for a remote signing service) contributes its own
        // signature entry over the canonical message. It must self-verify through the real codec, proving
        // the provider seam produces a wire-compatible envelope — not a re-implementation that only agrees
        // with itself.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var config = new IntegrityConfiguration
        {
            SignatureProviders = new ISignatureProvider[] { new FixedKeySignatureProvider(key, "remote-hsm") }
        };

        var result = EcdsaManifestSigner.Sign(Entries(("PkgA", "AABBCC")), config);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var envelope = IntegrityEnvelopeCodec.Parse(result.Value)!;
        var entry = Assert.Single(envelope.Signatures);
        Assert.Equal("remote-hsm", entry.KeyId);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo())), entry.Fingerprint);
        Assert.True(IntegrityEnvelopeCodec.VerifySignature(envelope));
    }

    [Fact]
    public void Sign_WithPemKeyAndCustomProvider_SignsWithBoth()
    {
        // Mixed backends / dual-sign: a file PEM key AND a custom provider each add one verifiable entry,
        // in that order (file keys first, then custom providers).
        using var pemKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var providerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pemPath = Path.Combine(_tempDir, "file.pem");
        File.WriteAllText(pemPath, pemKey.ExportPkcs8PrivateKeyPem());
        var config = new IntegrityConfiguration
        {
            SigningKeyPath = pemPath,
            SignatureProviders = new ISignatureProvider[] { new FixedKeySignatureProvider(providerKey, "custom") }
        };

        var result = EcdsaManifestSigner.Sign(Entries(("PkgA", "AABBCC")), config);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var envelope = IntegrityEnvelopeCodec.Parse(result.Value)!;
        Assert.Equal(2, envelope.Signatures.Count);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(pemKey.ExportSubjectPublicKeyInfo())),
            envelope.Signatures[0].Fingerprint);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(providerKey.ExportSubjectPublicKeyInfo())),
            envelope.Signatures[1].Fingerprint);
        Assert.True(IntegrityEnvelopeCodec.VerifySignature(envelope));
    }

    /// <summary>Test double: signs the canonical message with a fixed key in the same P1363 form as the built-ins.</summary>
    private sealed class FixedKeySignatureProvider(ECDsa key, string keyId) : ISignatureProvider
    {
        public ValueTask<Result<ProviderSignature>> SignAsync(
            ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default)
        {
            var hash = SHA256.HashData(message.Span);
            return new ValueTask<Result<ProviderSignature>>(Result<ProviderSignature>.Success(new ProviderSignature
            {
                SubjectPublicKeyInfo = key.ExportSubjectPublicKeyInfo(),
                Signature = key.SignHash(hash),
                KeyId = keyId
            }));
        }
    }
}
