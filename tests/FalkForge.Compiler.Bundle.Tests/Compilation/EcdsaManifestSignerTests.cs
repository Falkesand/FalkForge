using System.Security.Cryptography;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Models;
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
        Assert.Equal(1, envelope!.Version);
        Assert.False(string.IsNullOrEmpty(envelope.PublicKey));
        Assert.False(string.IsNullOrEmpty(envelope.Signature));
        Assert.Equal(2, envelope.Files.Count);
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

        var pub1 = IntegrityEnvelopeCodec.Parse(first.Value)!.PublicKey;
        var pub2 = IntegrityEnvelopeCodec.Parse(second.Value)!.PublicKey;
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
        Assert.Equal(expectedPub, IntegrityEnvelopeCodec.Parse(first.Value)!.PublicKey);
        Assert.Equal(
            IntegrityEnvelopeCodec.Parse(first.Value)!.PublicKey,
            IntegrityEnvelopeCodec.Parse(second.Value)!.PublicKey);
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
    public void Sign_EmptyEntries_StillProducesVerifiableEnvelope()
    {
        var result = EcdsaManifestSigner.Sign(Entries(), config: null);

        Assert.True(result.IsSuccess);
        var envelope = IntegrityEnvelopeCodec.Parse(result.Value)!;
        Assert.Empty(envelope.Files);
        Assert.True(IntegrityEnvelopeCodec.VerifySignature(envelope));
    }
}
