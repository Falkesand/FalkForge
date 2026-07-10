using FalkForge.Cli.Models;
using Xunit;

namespace FalkForge.Cli.Tests;

/// <summary>
/// Structural validation of the optional <c>signing</c> section in the forge JSON config.
/// The section selects a bundle-integrity signature backend (C17 seam) but must never carry
/// secret material itself: keys and credentials are referenced by file path or environment
/// variable NAME only. These tests pin the fail-closed validation contract (JSN015-JSN018)
/// and the no-inline-secrets enforcement (JSN016).
/// </summary>
public sealed class SigningConfigLoaderTests
{
    private static Result<SigningConfig> Load(string signingJson) =>
        JsonConfigLoader.LoadSigningFromString($$"""
        {
            "product": { "name": "App", "manufacturer": "Corp" },
            "signing": {{signingJson}}
        }
        """);

    // ── absence / none ───────────────────────────────────────────────────────

    [Fact]
    public void NoSigningSection_NormalizesToProviderNone()
    {
        // Result<T> forbids null payloads, so "no signing" is modeled as an explicit
        // provider "none" — the factory then resolves it to ResolvedSigning.None.
        var result = JsonConfigLoader.LoadSigningFromString(
            """{ "product": { "name": "App", "manufacturer": "Corp" } }""");

        Assert.True(result.IsSuccess);
        Assert.Equal("none", result.Value.Provider);
    }

    [Fact]
    public void ProviderNone_IsValid()
    {
        var result = Load("""{ "provider": "none" }""");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal("none", result.Value.Provider);
    }

    // ── provider selection (JSN015) ──────────────────────────────────────────

    [Fact]
    public void SigningSectionWithoutProvider_FailsJsn015()
    {
        // A signing section that never says which provider it wants must not silently
        // default to unsigned — fail-closed forces an explicit choice.
        var result = Load("""{ "keyPath": "release.pem" }""");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("JSN015", result.Error.Message);
    }

    [Fact]
    public void UnknownProvider_FailsJsn015()
    {
        var result = Load("""{ "provider": "hsm" }""");

        Assert.True(result.IsFailure);
        Assert.Contains("JSN015", result.Error.Message);
        Assert.Contains("hsm", result.Error.Message);
    }

    // ── inline-secret enforcement (JSN016) ───────────────────────────────────

    [Fact]
    public void UnknownSecretLookingField_FailsJsn016_PointingToEnvironment()
    {
        // "bearerToken" is not a recognized field — the schema only accepts bearerTokenEnv.
        // The error must tell the author that secrets belong in the environment, not the file.
        var result = Load(
            """{ "provider": "signserver", "baseUrl": "https://s", "worker": "W", "authMode": "bearer", "bearerToken": "eyJhbGciOiJIUzI1NiJ9.x.y" }""");

        Assert.True(result.IsFailure);
        Assert.Contains("JSN016", result.Error.Message);
        Assert.Contains("environment", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnknownField_FailsJsn016()
    {
        // The signing section is security-sensitive, so unknown keys fail closed instead of
        // being silently ignored like elsewhere in the config (typos must not disable auth).
        var result = Load("""{ "provider": "pem", "keyPath": "k.pem", "keyPth": "typo.pem" }""");

        Assert.True(result.IsFailure);
        Assert.Contains("JSN016", result.Error.Message);
        Assert.Contains("keyPth", result.Error.Message);
    }

    [Fact]
    public void InlinePemBlockInKeyPath_FailsJsn016()
    {
        var result = Load(
            """{ "provider": "pem", "keyPath": "-----BEGIN PRIVATE KEY-----\nMIG...\n-----END PRIVATE KEY-----" }""");

        Assert.True(result.IsFailure);
        Assert.Contains("JSN016", result.Error.Message);
    }

    [Fact]
    public void EnvNameFieldHoldingLiteralSecret_FailsJsn016()
    {
        // A JWT pasted where an environment variable NAME belongs — dots are not valid in
        // env var names, so this is a literal credential and must be rejected.
        var result = Load(
            """{ "provider": "signserver", "baseUrl": "https://s", "worker": "W", "authMode": "bearer", "bearerTokenEnv": "eyJhbGciOiJIUzI1NiJ9.payload.sig" }""");

        Assert.True(result.IsFailure);
        Assert.Contains("JSN016", result.Error.Message);
    }

    // ── pem provider (JSN017) ────────────────────────────────────────────────

    [Fact]
    public void Pem_WithKeyPath_Succeeds()
    {
        var result = Load("""{ "provider": "pem", "keyPath": "keys/release.pem" }""");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal("keys/release.pem", result.Value.KeyPath);
    }

    [Fact]
    public void Pem_WithKeyEnv_Succeeds()
    {
        var result = Load("""{ "provider": "pem", "keyEnv": "RELEASE_SIGNING_KEY" }""");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal("RELEASE_SIGNING_KEY", result.Value.KeyEnv);
    }

    [Fact]
    public void Pem_WithoutKeySource_FailsJsn017()
    {
        var result = Load("""{ "provider": "pem" }""");

        Assert.True(result.IsFailure);
        Assert.Contains("JSN017", result.Error.Message);
    }

    [Fact]
    public void Pem_WithBothKeySources_FailsJsn017()
    {
        // Ambiguous key source: refusing is safer than picking one silently.
        var result = Load("""{ "provider": "pem", "keyPath": "k.pem", "keyEnv": "KEY" }""");

        Assert.True(result.IsFailure);
        Assert.Contains("JSN017", result.Error.Message);
    }

    // ── signserver provider (JSN018) ─────────────────────────────────────────

    [Fact]
    public void SignServer_Valid_Succeeds()
    {
        var result = Load(
            """{ "provider": "signserver", "baseUrl": "https://sign.example.com", "worker": "PlainSigner", "authMode": "bearer", "bearerTokenEnv": "SIGN_TOKEN" }""");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal("https://sign.example.com", result.Value.BaseUrl);
        Assert.Equal("PlainSigner", result.Value.Worker);
    }

    [Fact]
    public void SignServer_MissingBaseUrl_FailsJsn018()
    {
        var result = Load("""{ "provider": "signserver", "worker": "W" }""");

        Assert.True(result.IsFailure);
        Assert.Contains("JSN018", result.Error.Message);
    }

    [Fact]
    public void SignServer_MissingWorker_FailsJsn018()
    {
        var result = Load("""{ "provider": "signserver", "baseUrl": "https://s" }""");

        Assert.True(result.IsFailure);
        Assert.Contains("JSN018", result.Error.Message);
    }

    [Fact]
    public void SignServer_InvalidAuthMode_FailsJsn018()
    {
        var result = Load(
            """{ "provider": "signserver", "baseUrl": "https://s", "worker": "W", "authMode": "kerberos" }""");

        Assert.True(result.IsFailure);
        Assert.Contains("JSN018", result.Error.Message);
        Assert.Contains("kerberos", result.Error.Message);
    }

    [Fact]
    public void SignServer_Bearer_WithoutTokenEnv_FailsJsn018()
    {
        // authMode=bearer with no way to obtain the token must not fall back to
        // an unauthenticated request — fail-closed.
        var result = Load(
            """{ "provider": "signserver", "baseUrl": "https://s", "worker": "W", "authMode": "bearer" }""");

        Assert.True(result.IsFailure);
        Assert.Contains("JSN018", result.Error.Message);
    }

    [Fact]
    public void SignServer_Basic_MissingPasswordEnv_FailsJsn018()
    {
        var result = Load(
            """{ "provider": "signserver", "baseUrl": "https://s", "worker": "W", "authMode": "basic", "usernameEnv": "SIGN_USER" }""");

        Assert.True(result.IsFailure);
        Assert.Contains("JSN018", result.Error.Message);
    }

    [Fact]
    public void SignServer_ClientCert_WithoutCertPathEnv_FailsJsn018()
    {
        var result = Load(
            """{ "provider": "signserver", "baseUrl": "https://s", "worker": "W", "authMode": "clientcert" }""");

        Assert.True(result.IsFailure);
        Assert.Contains("JSN018", result.Error.Message);
    }

    // ── file-level entry point ───────────────────────────────────────────────

    [Fact]
    public void LoadSigningFromFile_MissingFile_FailsFileNotFound()
    {
        var result = JsonConfigLoader.LoadSigningFromFile(
            Path.Combine(Path.GetTempPath(), $"no_such_{Guid.NewGuid():N}.json"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.FileNotFound, result.Error.Kind);
    }
}
