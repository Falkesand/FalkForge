using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using FalkForge.Cli.Models;
using FalkForge.Signing;
using FalkForge.Signing.SignServer;
using Xunit;

namespace FalkForge.Cli.Tests;

/// <summary>
/// Build-time resolution of a validated <c>signing</c> config into a concrete
/// <see cref="ISignatureProvider"/> (the C17 seam). Pins that secret material is read from the
/// ENVIRONMENT VALUES the config names (never from the config itself), that unresolvable auth
/// fails closed with JSN019 instead of silently signing unauthenticated, and that the SignServer
/// http/NOAUTH cases surface warnings matching the SignServerConfig guidance.
/// </summary>
public sealed class SigningProviderFactoryTests : IDisposable
{
    private static readonly byte[] Message = Encoding.UTF8.GetBytes("{\"canonical\":\"message\"}");

    private readonly string _tempDir;
    private readonly List<string> _envVarsToClear = [];

    public SigningProviderFactoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SigningFactoryTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        foreach (var name in _envVarsToClear)
            Environment.SetEnvironmentVariable(name, null);
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string SetEnv(string value)
    {
        var name = $"C20_TEST_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(name, value);
        _envVarsToClear.Add(name);
        return name;
    }

    /// <summary>Reserves a unique env var name that is guaranteed unset.</summary>
    private static string UnsetEnvName() => $"C20_UNSET_{Guid.NewGuid():N}";

    private static async Task AssertSignsVerifiablyWith(ISignatureProvider provider, ECDsa expectedKey)
    {
        var signResult = await provider.SignAsync(Message);
        Assert.True(signResult.IsSuccess, signResult.IsFailure ? signResult.Error.Message : null);
        Assert.Equal(expectedKey.ExportSubjectPublicKeyInfo(), signResult.Value.SubjectPublicKeyInfo);

        using var pub = ECDsa.Create();
        pub.ImportSubjectPublicKeyInfo(signResult.Value.SubjectPublicKeyInfo, out _);
        Assert.True(pub.VerifyHash(SHA256.HashData(Message), signResult.Value.Signature));
    }

    // ── none ─────────────────────────────────────────────────────────────────

    [Fact]
    public void NullConfig_ResolvesToNoSigning()
    {
        var result = SigningProviderFactory.Create(null, _tempDir);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsEnabled);
    }

    [Fact]
    public void ProviderNone_ResolvesToNoSigning()
    {
        var result = SigningProviderFactory.Create(new SigningConfig { Provider = "none" }, _tempDir);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsEnabled);
    }

    // ── pem: keyPath ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Pem_KeyPath_YieldsProviderThatSignsWithThatKey()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pemPath = Path.Combine(_tempDir, "release.pem");
        File.WriteAllText(pemPath, key.ExportPkcs8PrivateKeyPem());

        var result = SigningProviderFactory.Create(
            new SigningConfig { Provider = "pem", KeyPath = pemPath }, _tempDir);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        await AssertSignsVerifiablyWith(result.Value.Provider!, key);
    }

    [Fact]
    public async Task Pem_RelativeKeyPath_ResolvesAgainstBaseDirectory()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Directory.CreateDirectory(Path.Combine(_tempDir, "keys"));
        File.WriteAllText(Path.Combine(_tempDir, "keys", "k.pem"), key.ExportPkcs8PrivateKeyPem());

        // Relative paths resolve against the config file's directory, like every other
        // path in the JSON config (see JsonConfigLoader.ResolvePath).
        var result = SigningProviderFactory.Create(
            new SigningConfig { Provider = "pem", KeyPath = "keys/k.pem" }, _tempDir);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        await AssertSignsVerifiablyWith(result.Value.Provider!, key);
    }

    [Fact]
    public void Pem_MissingKeyFile_FailsJsn019()
    {
        var result = SigningProviderFactory.Create(
            new SigningConfig { Provider = "pem", KeyPath = Path.Combine(_tempDir, "missing.pem") }, _tempDir);

        Assert.True(result.IsFailure);
        Assert.Contains("JSN019", result.Error.Message);
    }

    // ── pem: keyEnv ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Pem_KeyEnv_ReadsPemFromEnvironmentValue()
    {
        // The config names the env var; the provider must sign with the key stored in its
        // VALUE — proving the factory dereferences the environment rather than treating the
        // name as key material.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var envName = SetEnv(key.ExportPkcs8PrivateKeyPem());

        var result = SigningProviderFactory.Create(
            new SigningConfig { Provider = "pem", KeyEnv = envName }, _tempDir);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        await AssertSignsVerifiablyWith(result.Value.Provider!, key);
    }

    [Fact]
    public void Pem_KeyEnvUnset_FailsClosedJsn019()
    {
        var result = SigningProviderFactory.Create(
            new SigningConfig { Provider = "pem", KeyEnv = UnsetEnvName() }, _tempDir);

        Assert.True(result.IsFailure);
        Assert.Contains("JSN019", result.Error.Message);
    }

    // ── signserver: env-sourced auth material ────────────────────────────────

    [Fact]
    public void SignServer_Bearer_ReadsTokenFromEnvironmentValue()
    {
        var tokenEnv = SetEnv("secret-bearer-token-value");

        var result = SigningProviderFactory.BuildSignServerConfig(new SigningConfig
        {
            Provider = "signserver",
            BaseUrl = "https://sign.example.com",
            Worker = "PlainSigner",
            AuthMode = "bearer",
            BearerTokenEnv = tokenEnv,
            KeyId = "release-2026",
        });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal("https://sign.example.com", result.Value.BaseUrl);
        Assert.Equal("PlainSigner", result.Value.Worker);
        Assert.Equal(SignServerAuthMode.Bearer, result.Value.AuthMode);
        Assert.Equal("secret-bearer-token-value", result.Value.BearerToken);
        Assert.Equal("release-2026", result.Value.KeyId);
    }

    [Fact]
    public void SignServer_Basic_ReadsCredentialsFromEnvironmentValues()
    {
        var userEnv = SetEnv("builder");
        var passEnv = SetEnv("s3cret");

        var result = SigningProviderFactory.BuildSignServerConfig(new SigningConfig
        {
            Provider = "signserver",
            BaseUrl = "https://sign.example.com",
            Worker = "W",
            AuthMode = "basic",
            UsernameEnv = userEnv,
            PasswordEnv = passEnv,
        });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(SignServerAuthMode.Basic, result.Value.AuthMode);
        Assert.Equal("builder", result.Value.BasicUsername);
        Assert.Equal("s3cret", result.Value.BasicPassword);
    }

    [Fact]
    public void SignServer_BearerTokenEnvUnset_FailsClosedJsn019()
    {
        // An unset auth env var must ERROR — never degrade to an unauthenticated request.
        var result = SigningProviderFactory.BuildSignServerConfig(new SigningConfig
        {
            Provider = "signserver",
            BaseUrl = "https://sign.example.com",
            Worker = "W",
            AuthMode = "bearer",
            BearerTokenEnv = UnsetEnvName(),
        });

        Assert.True(result.IsFailure);
        Assert.Contains("JSN019", result.Error.Message);
    }

    [Fact]
    public void SignServer_PasswordEnvUnset_FailsClosedJsn019()
    {
        var userEnv = SetEnv("builder");

        var result = SigningProviderFactory.BuildSignServerConfig(new SigningConfig
        {
            Provider = "signserver",
            BaseUrl = "https://sign.example.com",
            Worker = "W",
            AuthMode = "basic",
            UsernameEnv = userEnv,
            PasswordEnv = UnsetEnvName(),
        });

        Assert.True(result.IsFailure);
        Assert.Contains("JSN019", result.Error.Message);
    }

    [Fact]
    public void SignServer_ClientCert_LoadsPfxFromEnvNamedPath()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=C20 Test Client", key, HashAlgorithmName.SHA256);
        using var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        var pfxPath = Path.Combine(_tempDir, "client.pfx");
        File.WriteAllBytes(pfxPath, cert.Export(X509ContentType.Pfx, "pfx-pass"));
        var certPathEnv = SetEnv(pfxPath);
        var certPassEnv = SetEnv("pfx-pass");

        var result = SigningProviderFactory.BuildSignServerConfig(new SigningConfig
        {
            Provider = "signserver",
            BaseUrl = "https://sign.example.com",
            Worker = "W",
            AuthMode = "clientcert",
            ClientCertPathEnv = certPathEnv,
            ClientCertPasswordEnv = certPassEnv,
        });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(SignServerAuthMode.ClientCert, result.Value.AuthMode);
        Assert.NotNull(result.Value.ClientCertificate);
        Assert.Equal(cert.Thumbprint, result.Value.ClientCertificate!.Thumbprint);
        result.Value.ClientCertificate.Dispose();
    }

    [Fact]
    public void SignServer_ClientCertPathEnvUnset_FailsClosedJsn019()
    {
        var result = SigningProviderFactory.BuildSignServerConfig(new SigningConfig
        {
            Provider = "signserver",
            BaseUrl = "https://sign.example.com",
            Worker = "W",
            AuthMode = "clientcert",
            ClientCertPathEnv = UnsetEnvName(),
        });

        Assert.True(result.IsFailure);
        Assert.Contains("JSN019", result.Error.Message);
    }

    [Fact]
    public void SignServer_Create_YieldsSignServerProvider()
    {
        var tokenEnv = SetEnv("tok");

        var result = SigningProviderFactory.Create(new SigningConfig
        {
            Provider = "signserver",
            BaseUrl = "https://sign.example.com",
            Worker = "W",
            AuthMode = "bearer",
            BearerTokenEnv = tokenEnv,
        }, _tempDir);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var provider = Assert.IsType<SignServerSignatureProvider>(result.Value.Provider!);
        provider.Dispose();
    }

    // ── warnings (match SignServerConfig doc guidance: warn, don't fail) ─────

    [Fact]
    public void SignServer_HttpBaseUrl_SurfacesWarning()
    {
        var tokenEnv = SetEnv("tok");

        var result = SigningProviderFactory.Create(new SigningConfig
        {
            Provider = "signserver",
            BaseUrl = "http://localhost:8080",
            Worker = "W",
            AuthMode = "bearer",
            BearerTokenEnv = tokenEnv,
        }, _tempDir);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Contains(result.Value.Warnings, w => w.Contains("http://", StringComparison.OrdinalIgnoreCase));
        (result.Value.Provider as IDisposable)?.Dispose();
    }

    [Fact]
    public void SignServer_AuthModeNone_SurfacesWarning()
    {
        var result = SigningProviderFactory.Create(new SigningConfig
        {
            Provider = "signserver",
            BaseUrl = "https://sign.example.com",
            Worker = "W",
            AuthMode = "none",
        }, _tempDir);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Contains(result.Value.Warnings, w => w.Contains("unauthenticated", StringComparison.OrdinalIgnoreCase));
        (result.Value.Provider as IDisposable)?.Dispose();
    }

    [Fact]
    public void SignServer_HttpsWithBearer_ProducesNoWarnings()
    {
        var tokenEnv = SetEnv("tok");

        var result = SigningProviderFactory.Create(new SigningConfig
        {
            Provider = "signserver",
            BaseUrl = "https://sign.example.com",
            Worker = "W",
            AuthMode = "bearer",
            BearerTokenEnv = tokenEnv,
        }, _tempDir);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Empty(result.Value.Warnings);
        (result.Value.Provider as IDisposable)?.Dispose();
    }
}
