namespace FalkForge.Engine.Tests.Logging;

using FalkForge.Engine.Logging;
using Xunit;

/// <summary>
/// Direct unit tests for <see cref="LogRedactor.Redact"/>. Pins two contracts: (1) the
/// separator/case-normalization rule that decides whether a key matches a deny-list token, and
/// (2) the point-in-time-snapshot contract — <c>Redact</c> must never hand back the caller's
/// live dictionary reference once it has decided to redact (see <c>EngineLogger.Log</c>'s
/// async-flush race), except for the documented empty-token-list opt-out, which is exempt from
/// snapshotting because redaction (and therefore this class's involvement) is fully disabled in
/// that configuration. <see cref="EngineLoggerTests"/> covers the same behaviour end-to-end
/// through <see cref="EngineLogger"/>.
/// </summary>
public sealed class LogRedactorTests
{
    [Fact]
    public void Redact_NullProperties_ReturnsNull()
    {
        var result = LogRedactor.Redact(null, LogRedactor.DefaultSecretKeyTokens);

        Assert.Null(result);
    }

    [Fact]
    public void Redact_NoSecretMatchingKey_ReturnsNewInstanceWithEqualContent()
    {
        // WHY (CodeRabbit finding C): a no-match result must still be a fresh copy, not the
        // caller's live reference — otherwise a secret the caller adds to the same dictionary
        // after Log() returns (but before the async flush writes the entry) would reach the
        // log file unredacted. See LogRedactor_Redact's XML doc and the EngineLogger-level
        // regression test for the full async-boundary scenario.
        var properties = new Dictionary<string, string> { ["PackageId"] = "MyApp" };

        var result = LogRedactor.Redact(properties, LogRedactor.DefaultSecretKeyTokens);

        Assert.NotSame(properties, result);
        var entry = Assert.Single(result!);
        Assert.Equal("PackageId", entry.Key);
        Assert.Equal("MyApp", entry.Value);
    }

    [Fact]
    public void Redact_EmptyTokenList_ReturnsSameInstance()
    {
        // WHY: an empty deny-list is the documented opt-out — redaction is fully disabled, so
        // Redact short-circuits before doing any work (including snapshotting) and hands back
        // the caller's own reference. The token list, not the property keys, decides whether
        // there is a copy.
        var properties = new Dictionary<string, string> { ["Password"] = "hunter2" };

        var result = LogRedactor.Redact(properties, []);

        Assert.Same(properties, result);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Separator/case normalization (CodeRabbit finding A): hyphenated/underscored/mixed-case
    // key variants must still match the compact deny-list tokens.
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("api-key")]
    [InlineData("connection-string")]
    [InlineData("private-key")]
    [InlineData("signing-key")]
    [InlineData("API_KEY")]
    [InlineData("Api-Key")]
    public void Redact_SeparatorOrCaseVariantSecretKey_ValueMasked(string key)
    {
        var properties = new Dictionary<string, string> { [key] = "hunter2" };

        var result = LogRedactor.Redact(properties, LogRedactor.DefaultSecretKeyTokens);

        Assert.Equal(LogRedactor.RedactedValue, result![key]);
    }

    [Theory]
    [InlineData("PublicKeyThumbprint")]
    [InlineData("KeyId")]
    [InlineData("KeyName")]
    [InlineData("Public-Key-Thumbprint")]
    public void Redact_BenignKeyAfterNormalization_ValueNotMasked(string key)
    {
        // WHY: guards against the normalization rule over-masking. Stripping separators must
        // not cause a benign compound key to accidentally form a secret token.
        var properties = new Dictionary<string, string> { [key] = "ABCD1234" };

        var result = LogRedactor.Redact(properties, LogRedactor.DefaultSecretKeyTokens);

        Assert.Equal("ABCD1234", result![key]);
    }
}
