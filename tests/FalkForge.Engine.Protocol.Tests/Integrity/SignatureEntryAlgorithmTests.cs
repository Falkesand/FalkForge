using System.Security.Cryptography;
using System.Text.Json;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Signing;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Integrity;

/// <summary>
/// The PQ-hybrid Stage 1 wire change: <see cref="SignatureEntry"/> gains an OPTIONAL
/// <c>algorithm</c> field. These tests pin the two properties the whole migration hinges on:
/// (1) an entry with no algorithm serializes byte-identically to today's envelopes (absent field,
/// not <c>null</c>), so every already-signed bundle round-trips unchanged; and (2) an already-shipped
/// ECDSA-only verifier that is ignorant of the field survives an ML-DSA entry — the 3309-byte
/// signature fails the 64-byte low-S length gate and iteration continues to the classical entry.
/// </summary>
public sealed class SignatureEntryAlgorithmTests
{
    private static IReadOnlyList<ManifestFileEntry> Files(params (string name, string sha)[] items)
        => items.Select(i => new ManifestFileEntry { Name = i.name, Sha256 = i.sha }).ToList();

    private static IReadOnlySet<string> TrustSet(params string[] fingerprints)
        => new HashSet<string>(fingerprints, StringComparer.OrdinalIgnoreCase);

    private static string Fingerprint(byte[] spki)
        => Convert.ToHexString(SHA256.HashData(spki));

    [Fact]
    public void Serialize_ClassicalEntry_OmitsAlgorithmField_WireBytesUnchanged()
    {
        // Backward compatibility to the byte: an ECDSA entry (Algorithm null) must not emit an
        // "algorithm" property at all — existing envelopes stay byte-identical, and old verifiers
        // that match on exact JSON shape see exactly what they saw before this change.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var envelope = IntegrityEnvelopeCodec.Sign(Files(("A", "AABB")), key);

        var json = IntegrityEnvelopeCodec.Serialize(envelope);

        using var doc = JsonDocument.Parse(json);
        var entry = doc.RootElement.GetProperty("signatures")[0];
        Assert.False(entry.TryGetProperty("algorithm", out _),
            "a classical entry must not carry an algorithm property (wire compat)");
    }

    [Fact]
    public void Parse_EntryWithoutAlgorithm_DefaultsToClassical()
    {
        // Absent algorithm ⇒ ECDSA-P256. Every envelope signed before this change parses with a
        // null Algorithm and must be treated as the classical algorithm it always was.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var json = IntegrityEnvelopeCodec.Serialize(IntegrityEnvelopeCodec.Sign(Files(("A", "AABB")), key));

        var parsed = IntegrityEnvelopeCodec.Parse(json);

        Assert.NotNull(parsed);
        var entry = Assert.Single(parsed!.Signatures);
        Assert.Null(entry.Algorithm);
        Assert.True(IntegrityEnvelopeCodec.VerifySignature(parsed));
    }

    [Fact]
    public void Serialize_MlDsaEntry_RoundTripsAlgorithmField()
    {
        var envelope = new ManifestSignatureEnvelope
        {
            Version = 2,
            Algorithm = IntegrityEnvelopeCodec.AlgorithmId,
            Files = Files(("A", "AABB")),
            Signatures =
            [
                new SignatureEntry
                {
                    KeyId = "pq",
                    Fingerprint = new string('A', 64),
                    PublicKey = Convert.ToBase64String(new byte[] { 1, 2, 3 }),
                    Signature = Convert.ToBase64String(new byte[] { 4, 5, 6 }),
                    Algorithm = IntegrityEnvelopeCodec.MlDsa65AlgorithmId
                }
            ]
        };

        var parsed = IntegrityEnvelopeCodec.Parse(IntegrityEnvelopeCodec.Serialize(envelope));

        Assert.NotNull(parsed);
        Assert.Equal("ML-DSA-65", Assert.Single(parsed!.Signatures).Algorithm);
    }

    [Fact]
    public void MlDsa65AlgorithmId_IsTheFrozenWireValue()
    {
        // The algorithm identifier is part of the wire contract forever — pin the exact strings.
        Assert.Equal("ML-DSA-65", IntegrityEnvelopeCodec.MlDsa65AlgorithmId);
        Assert.Equal("ECDSA-P256", IntegrityEnvelopeCodec.AlgorithmId);
    }

    [Fact]
    public void EcdsaOnlyIterationPath_MlDsaSizedSignature_IsSkippedAndIterationContinues()
    {
        // Simulates the ALREADY-SHIPPED verifier meeting a hybrid envelope. An old engine ignores
        // the unknown "algorithm" JSON field, so an ML-DSA entry looks like a classical entry with a
        // 3309-byte signature. The low-S length gate (P-256 P1363 = exactly 64 bytes) must reject it
        // as non-canonical WITHOUT crashing, and iteration must continue to the valid ECDSA entry.
        // We reproduce the old verifier's view by leaving Algorithm null on the oversized entry.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var files = Files(("A", "AABB"));
        var envelope = IntegrityEnvelopeCodec.Sign(files, key);
        var classicalEntry = envelope.Signatures[0];

        var fakePqSpki = new byte[1974]; // ML-DSA-65 SPKI size
        RandomNumberGenerator.Fill(fakePqSpki);
        var fakePqEntry = new SignatureEntry
        {
            KeyId = "pq-as-seen-by-old-engine",
            Fingerprint = Fingerprint(fakePqSpki),           // honest fingerprint — passes gate (a)
            PublicKey = Convert.ToBase64String(fakePqSpki),
            Signature = Convert.ToBase64String(new byte[3309]), // ML-DSA-65 signature size
            Algorithm = null                                    // old engines never see the field
        };
        envelope.Signatures = new[] { fakePqEntry, classicalEntry };

        // Empty trust set drives the entry past the trust check into the low-S length gate,
        // exercising the deepest point old code reaches with an ML-DSA-sized signature.
        var result = IntegrityEnvelopeCodec.MatchTrustedSignature(envelope, TrustSet());

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(classicalEntry.Fingerprint, result.Value);
    }
}
