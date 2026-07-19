using FalkForge.Configuration;
using Xunit;

namespace FalkForge.Core.Tests.Configuration;

/// <summary>
/// Pins the central FALKFORGE_* environment-variable catalog: name constants stay stable
/// (accidental rename is a breaking change for every operator's CI script), and each typed
/// accessor preserves the exact semantics the 25 scattered call sites had before migration —
/// including the deliberately "silent" ones (absent opt-in flag = off, no error).
/// </summary>
[Collection("SourceDateEpoch")]
public sealed class EnvVarCatalogTests : IDisposable
{
    private readonly string? _originalEpoch = Environment.GetEnvironmentVariable("SOURCE_DATE_EPOCH");
    private readonly string? _originalNoSign = Environment.GetEnvironmentVariable("FALKFORGE_NO_SIGN");
    private readonly string? _originalGenerateSbom = Environment.GetEnvironmentVariable("FALKFORGE_GENERATE_SBOM");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", _originalEpoch);
        Environment.SetEnvironmentVariable("FALKFORGE_NO_SIGN", _originalNoSign);
        Environment.SetEnvironmentVariable("FALKFORGE_GENERATE_SBOM", _originalGenerateSbom);
    }

    // ── name constants (discoverability contract) ───────────────────────────

    [Fact]
    public void NameConstants_MatchDocumentedLiterals()
    {
        Assert.Equal("SOURCE_DATE_EPOCH", EnvVarCatalog.SourceDateEpoch);
        Assert.Equal("FALKFORGE_GENERATE_SBOM", EnvVarCatalog.GenerateSbom);
        Assert.Equal("FALKFORGE_NO_SIGN", EnvVarCatalog.NoSign);
        Assert.Equal("FALKFORGE_ENGINE_STUB", EnvVarCatalog.EngineStub);
        Assert.Equal("FALKFORGE_ELEVATION_COMPANION", EnvVarCatalog.ElevationCompanion);
        Assert.Equal("SIGNSERVER_URL", EnvVarCatalog.SignServerUrl);
        Assert.Equal("SIGNSERVER_WORKER", EnvVarCatalog.SignServerWorker);
        Assert.Equal("SIGNSERVER_AUTH", EnvVarCatalog.SignServerAuth);
        Assert.Equal("SIGNSERVER_BEARER_TOKEN", EnvVarCatalog.SignServerBearerToken);
        Assert.Equal("SIGNSERVER_BASIC_USER", EnvVarCatalog.SignServerBasicUser);
        Assert.Equal("SIGNSERVER_BASIC_PASS", EnvVarCatalog.SignServerBasicPass);
        Assert.Equal("SIGNSERVER_CLIENT_CERT", EnvVarCatalog.SignServerClientCert);
        Assert.Equal("SIGNSERVER_CLIENT_CERT_PASSWORD", EnvVarCatalog.SignServerClientCertPassword);
        Assert.Equal("SIGNSERVER_KEY_ID", EnvVarCatalog.SignServerKeyId);
    }

    // ── GetRaw / SetRaw (generic primitive) ──────────────────────────────────

    [Fact]
    public void GetRaw_Unset_ReturnsNull()
    {
        var name = $"FALKFORGE_TEST_{Guid.NewGuid():N}";

        Assert.Null(EnvVarCatalog.GetRaw(name));
    }

    [Fact]
    public void SetRaw_ThenGetRaw_RoundTrips()
    {
        var name = $"FALKFORGE_TEST_{Guid.NewGuid():N}";
        try
        {
            EnvVarCatalog.SetRaw(name, "value");
            Assert.Equal("value", EnvVarCatalog.GetRaw(name));
        }
        finally
        {
            EnvVarCatalog.SetRaw(name, null);
        }
    }

    // ── SOURCE_DATE_EPOCH: tri-state (absent / valid / malformed) ────────────

    [Fact]
    public void TryGetSourceDateEpoch_Absent_ReturnsNotSet()
    {
        Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", null);

        var result = EnvVarCatalog.TryGetSourceDateEpoch();

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsSet);
    }

    [Fact]
    public void TryGetSourceDateEpoch_ValidValue_ReturnsParsedValue()
    {
        Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", "1700000000");

        var result = EnvVarCatalog.TryGetSourceDateEpoch();

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsSet);
        Assert.Equal(1700000000L, result.Value.Value);
    }

    [Fact]
    public void TryGetSourceDateEpoch_MalformedValue_FailsWithRpr001()
    {
        Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", "not-a-number");

        var result = EnvVarCatalog.TryGetSourceDateEpoch();

        Assert.True(result.IsFailure);
        Assert.Contains("RPR001", result.Error.Message);
        Assert.Contains("not-a-number", result.Error.Message);
    }

    [Fact]
    public void SetSourceDateEpoch_ThenTryGet_RoundTrips()
    {
        try
        {
            EnvVarCatalog.SetSourceDateEpoch(1234567890L);

            var result = EnvVarCatalog.TryGetSourceDateEpoch();

            Assert.True(result.IsSuccess);
            Assert.True(result.Value.IsSet);
            Assert.Equal(1234567890L, result.Value.Value);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", null);
        }
    }

    // ── presence-only opt-in flags (FALKFORGE_NO_SIGN / FALKFORGE_GENERATE_SBOM) ──
    // Current contract (preserved verbatim from the 5 pre-migration call sites):
    // ANY non-empty value counts as "on" — this is NOT a bool parse. Absence, or an
    // empty string, is "off". A malformed value can never occur, so there is nothing
    // to fail loud on; these stay lazy per the opt-in-flag design rule.

    [Fact]
    public void IsSigningDisabled_Unset_ReturnsFalse()
    {
        Environment.SetEnvironmentVariable("FALKFORGE_NO_SIGN", null);

        Assert.False(EnvVarCatalog.IsSigningDisabled());
    }

    [Fact]
    public void IsSigningDisabled_SetToEmptyString_ReturnsFalse()
    {
        Environment.SetEnvironmentVariable("FALKFORGE_NO_SIGN", "");

        Assert.False(EnvVarCatalog.IsSigningDisabled());
    }

    [Theory]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("anything")]
    public void IsSigningDisabled_SetToAnyNonEmptyValue_ReturnsTrue(string value)
    {
        // Presence, not parsing: even "0" or "false" mean "disabled" today. A future
        // reader relying on bool.Parse semantics would silently invert this — pinned here.
        Environment.SetEnvironmentVariable("FALKFORGE_NO_SIGN", value);

        Assert.True(EnvVarCatalog.IsSigningDisabled());
    }

    [Fact]
    public void DisableSigning_SetsFlag()
    {
        try
        {
            EnvVarCatalog.DisableSigning();

            Assert.True(EnvVarCatalog.IsSigningDisabled());
        }
        finally
        {
            Environment.SetEnvironmentVariable("FALKFORGE_NO_SIGN", null);
        }
    }

    [Fact]
    public void IsSbomGenerationRequested_Unset_ReturnsFalse()
    {
        Environment.SetEnvironmentVariable("FALKFORGE_GENERATE_SBOM", null);

        Assert.False(EnvVarCatalog.IsSbomGenerationRequested());
    }

    [Fact]
    public void IsSbomGenerationRequested_SetNonEmpty_ReturnsTrue()
    {
        Environment.SetEnvironmentVariable("FALKFORGE_GENERATE_SBOM", "1");

        Assert.True(EnvVarCatalog.IsSbomGenerationRequested());
    }

    [Fact]
    public void RequestSbomGeneration_SetsFlag()
    {
        try
        {
            EnvVarCatalog.RequestSbomGeneration();

            Assert.True(EnvVarCatalog.IsSbomGenerationRequested());
        }
        finally
        {
            Environment.SetEnvironmentVariable("FALKFORGE_GENERATE_SBOM", null);
        }
    }

}
