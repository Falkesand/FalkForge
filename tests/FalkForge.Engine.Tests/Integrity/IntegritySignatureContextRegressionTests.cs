using System.Text.Json;
using FalkForge.Engine.Protocol.Integrity;
using Xunit;

namespace FalkForge.Engine.Tests.Integrity;

/// <summary>
/// Regression lockdown tests for IntegritySignatureContext.
/// A test failure here means a new property was added to ManifestSignatureEnvelope or
/// ManifestFileEntry that either (a) carries a non-serializable system type, or
/// (b) matches a sensitive-name pattern without [JsonIgnore].
/// Note: "publicKey" and "signature" are cryptographic metadata required for integrity
/// verification — they are intentionally present and whitelisted.
/// Fix by adding [JsonIgnore] to any unintended sensitive property before merging.
/// </summary>
public sealed class IntegritySignatureContextRegressionTests
{
    private static readonly string[] SensitiveFragments = ["password", "apikey", "passphrase", "pin", "credential"];

    // "secret" would be flagged, but these crypto fields are intentional.
    // "signature" and "publicKey" carry public-key crypto material — not secrets.
    // We deliberately do NOT add "token" to the allowlist; no token should appear here.
    private static readonly HashSet<string> AllowedSensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "signature",   // ECDSA signature over file entries — required for integrity check
        "publicKey"    // ECDSA public key — intentionally public, required for verification
    };

    [Fact]
    public void ManifestSignatureEnvelope_RoundTrip_ProducesExpectedJson()
    {
        var envelope = BuildFullEnvelope();

        var json = JsonSerializer.Serialize(envelope, IntegrityEnvelopeJsonContext.Default.ManifestSignatureEnvelope);

        Assert.False(string.IsNullOrWhiteSpace(json));

        var parsed = JsonSerializer.Deserialize(json, IntegrityEnvelopeJsonContext.Default.ManifestSignatureEnvelope);
        Assert.NotNull(parsed);
        Assert.Equal(1, parsed.Version);
        Assert.Equal("ES256", parsed.Algorithm);
        Assert.Equal("MARKER_PUBLIC_KEY", parsed.PublicKey);
        Assert.Equal("MARKER_SIGNATURE", parsed.Signature);
    }

    [Fact]
    public void ManifestSignatureEnvelope_Json_ContainsNoForbiddenSystemTypes()
    {
        var json = JsonSerializer.Serialize(BuildFullEnvelope(), IntegrityEnvelopeJsonContext.Default.ManifestSignatureEnvelope);

        Assert.DoesNotContain("System.IntPtr", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.Delegate", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.Action", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.Func", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ManifestSignatureEnvelope_Json_ContainsNoUnwhitelistedSensitiveKeys()
    {
        var json = JsonSerializer.Serialize(BuildFullEnvelope(), IntegrityEnvelopeJsonContext.Default.ManifestSignatureEnvelope);

        using var doc = JsonDocument.Parse(json);
        AssertNoSensitiveKeys(doc.RootElement, "$");
    }

    [Fact]
    public void ManifestFileEntry_RoundTrip_AllFields()
    {
        var envelope = BuildFullEnvelope();
        var json = JsonSerializer.Serialize(envelope, IntegrityEnvelopeJsonContext.Default.ManifestSignatureEnvelope);
        var parsed = JsonSerializer.Deserialize(json, IntegrityEnvelopeJsonContext.Default.ManifestSignatureEnvelope);

        Assert.NotNull(parsed);
        Assert.Equal(2, parsed.Files.Count);
        Assert.Equal("MARKER_FILE_A.msi", parsed.Files[0].Name);
        Assert.Equal("AABBCC001122DDEEFF", parsed.Files[0].Sha256);
        Assert.Equal("MARKER_FILE_B.exe", parsed.Files[1].Name);
    }

    [Fact]
    public void V2Envelope_WithSignatureList_RoundTripsUnderSourceGen()
    {
        var envelope = BuildV2Envelope();

        var json = JsonSerializer.Serialize(envelope, IntegrityEnvelopeJsonContext.Default.ManifestSignatureEnvelope);
        var parsed = JsonSerializer.Deserialize(json, IntegrityEnvelopeJsonContext.Default.ManifestSignatureEnvelope);

        Assert.NotNull(parsed);
        Assert.Equal(2, parsed!.Version);
        Assert.Null(parsed.PublicKey);   // v1 top-level fields omitted on v2
        Assert.Null(parsed.Signature);
        Assert.Equal(2, parsed.Signatures.Count);
        Assert.Equal("falkforge-a", parsed.Signatures[0].KeyId);
        Assert.Equal("MARKER_FP_A", parsed.Signatures[0].Fingerprint);
        Assert.Equal("MARKER_PUB_A", parsed.Signatures[0].PublicKey);
        Assert.Equal("MARKER_SIG_A", parsed.Signatures[0].Signature);
        Assert.Equal("MARKER_FP_B", parsed.Signatures[1].Fingerprint);
    }

    [Fact]
    public void V2Envelope_Json_ContainsNoUnwhitelistedSensitiveKeys()
    {
        var json = JsonSerializer.Serialize(BuildV2Envelope(), IntegrityEnvelopeJsonContext.Default.ManifestSignatureEnvelope);

        using var doc = JsonDocument.Parse(json);
        AssertNoSensitiveKeys(doc.RootElement, "$");
    }

    private static ManifestSignatureEnvelope BuildFullEnvelope() => new()
    {
        Version = 1,
        Algorithm = "ES256",
        PublicKey = "MARKER_PUBLIC_KEY",
        Signature = "MARKER_SIGNATURE",
        Files =
        [
            new ManifestFileEntry { Name = "MARKER_FILE_A.msi", Sha256 = "AABBCC001122DDEEFF" },
            new ManifestFileEntry { Name = "MARKER_FILE_B.exe", Sha256 = "334455667788AABBCC" }
        ]
    };

    private static ManifestSignatureEnvelope BuildV2Envelope() => new()
    {
        Version = 2,
        Algorithm = "ECDSA-P256",
        Files =
        [
            new ManifestFileEntry { Name = "MARKER_FILE_A.msi", Sha256 = "AABBCC001122DDEEFF" }
        ],
        Signatures =
        [
            new SignatureEntry { KeyId = "falkforge-a", Fingerprint = "MARKER_FP_A", PublicKey = "MARKER_PUB_A", Signature = "MARKER_SIG_A" },
            new SignatureEntry { KeyId = "falkforge-b", Fingerprint = "MARKER_FP_B", PublicKey = "MARKER_PUB_B", Signature = "MARKER_SIG_B" }
        ]
    };

    private void AssertNoSensitiveKeys(JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var key = prop.Name;
                    if (!AllowedSensitiveKeys.Contains(key))
                    {
                        foreach (var fragment in SensitiveFragments)
                        {
                            Assert.False(
                                key.Contains(fragment, StringComparison.OrdinalIgnoreCase),
                                $"Sensitive key '{key}' found at {path}.{key}. Add [JsonIgnore] or add to AllowedSensitiveKeys with justification.");
                        }
                    }
                    AssertNoSensitiveKeys(prop.Value, $"{path}.{key}");
                }
                break;
            case JsonValueKind.Array:
                int i = 0;
                foreach (var item in element.EnumerateArray())
                    AssertNoSensitiveKeys(item, $"{path}[{i++}]");
                break;
        }
    }
}
