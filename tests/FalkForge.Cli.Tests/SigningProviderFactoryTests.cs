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

    // ── pem: hybrid post-quantum companion (pqKeyPath / pqKeyEnv) ────────────

    private static async Task AssertPqSignsVerifiablyWith(ISignatureProvider pqProvider, MLDsa expectedKey)
    {
        var signResult = await pqProvider.SignAsync(Message);
        Assert.True(signResult.IsSuccess, signResult.IsFailure ? signResult.Error.Message : null);
        Assert.Equal(SignatureAlgorithms.MlDsa65, signResult.Value.Algorithm);
        Assert.Equal(expectedKey.ExportSubjectPublicKeyInfo(), signResult.Value.SubjectPublicKeyInfo);

        using var pub = MLDsa.ImportSubjectPublicKeyInfo(signResult.Value.SubjectPublicKeyInfo);
        Assert.True(pub.VerifyData(Message, signResult.Value.Signature, SignatureAlgorithms.ManifestContext));
    }

    [Fact]
    public async Task Pem_HybridKeyPaths_YieldsClassicalAndPqProviders_BothSignVerifiably()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");

        using var classical = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var pq = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
        Directory.CreateDirectory(Path.Combine(_tempDir, "keys"));
        File.WriteAllText(Path.Combine(_tempDir, "keys", "c.pem"), classical.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(Path.Combine(_tempDir, "keys", "q.pem"), pq.ExportPkcs8PrivateKeyPem());

        // Relative PQ paths resolve against the config directory, like every other config path.
        var result = SigningProviderFactory.Create(
            new SigningConfig { Provider = "pem", KeyPath = "keys/c.pem", PqKeyPath = "keys/q.pem" },
            _tempDir);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.True(result.Value.IsEnabled);
        await AssertSignsVerifiablyWith(result.Value.Provider!, classical);
        Assert.NotNull(result.Value.PqProvider);
        await AssertPqSignsVerifiablyWith(result.Value.PqProvider!, pq);
    }

    [Fact]
    public async Task Pem_PqKeyEnv_ReadsPemFromEnvironmentValue()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");

        using var classical = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var pq = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
        var classicalEnv = SetEnv(classical.ExportPkcs8PrivateKeyPem());
        var pqEnv = SetEnv(pq.ExportPkcs8PrivateKeyPem());

        var result = SigningProviderFactory.Create(
            new SigningConfig { Provider = "pem", KeyEnv = classicalEnv, PqKeyEnv = pqEnv }, _tempDir);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.NotNull(result.Value.PqProvider);
        await AssertPqSignsVerifiablyWith(result.Value.PqProvider!, pq);
    }

    [Fact]
    public void Pem_PqKeyEnvUnset_FailsClosedJsn019()
    {
        // An unset PQ env var must ERROR — never degrade to a classical-only bundle the
        // publisher believes is hybrid.
        using var classical = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var classicalEnv = SetEnv(classical.ExportPkcs8PrivateKeyPem());

        var result = SigningProviderFactory.Create(
            new SigningConfig { Provider = "pem", KeyEnv = classicalEnv, PqKeyEnv = UnsetEnvName() }, _tempDir);

        Assert.True(result.IsFailure);
        Assert.Contains("JSN019", result.Error.Message);
        Assert.Contains("pqKeyEnv", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Pem_MissingPqKeyFile_FailsJsn019()
    {
        using var classical = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pemPath = Path.Combine(_tempDir, "c.pem");
        File.WriteAllText(pemPath, classical.ExportPkcs8PrivateKeyPem());

        var result = SigningProviderFactory.Create(
            new SigningConfig
            {
                Provider = "pem",
                KeyPath = pemPath,
                PqKeyPath = Path.Combine(_tempDir, "missing-mldsa.pem")
            }, _tempDir);

        Assert.True(result.IsFailure);
        Assert.Contains("JSN019", result.Error.Message);
    }

    [Fact]
    public void Pem_PqKeyPathHoldsSecretShapedValue_ErrorNamesFieldWithoutEchoingValue()
    {
        // The C20 leak-regression rule extends to the PQ field: a secret mispasted into
        // pqKeyPath resolves to a non-existent file; the missing-file error must reference
        // signing.pqKeyPath, never print the resolved path (= the secret).
        using var classical = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pemPath = Path.Combine(_tempDir, "c.pem");
        File.WriteAllText(pemPath, classical.ExportPkcs8PrivateKeyPem());

        var result = SigningProviderFactory.Create(
            new SigningConfig { Provider = "pem", KeyPath = pemPath, PqKeyPath = SecretShapedLiteral }, _tempDir);

        Assert.True(result.IsFailure);
        Assert.Contains("JSN019", result.Error.Message);
        Assert.Contains("pqKeyPath", result.Error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretShapedLiteral, result.Error.Message, StringComparison.OrdinalIgnoreCase);
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

    // ── secret-leak regression: error messages must never echo config VALUES ─
    //
    // A real alphanumeric token (GitHub ghp_…, Stripe sk_live_…) mispasted into a
    // '*Env' or path field passes the loader's charset check (JSN016), so the
    // fail-closed JSN019 error is the last line of defense: it must name the config
    // FIELD to fix, never echo the mispasted VALUE into stdout / CI logs.

    /// <summary>
    /// A secret-shaped literal that passes IsValidEnvVarName (letters/digits/underscore only).
    /// Deliberately NOT the exact ghp_ token shape so secret scanners never flag the fixture.
    /// </summary>
    private const string SecretShapedLiteral = "ghp_FakeLeakCanary0123456789abcdef";

    [Fact]
    public void SignServer_BearerTokenEnvHoldsSecretShapedValue_ErrorNamesFieldWithoutEchoingValue()
    {
        // The env var of this literal name is (of course) unset, so the build fails closed —
        // and the error must not leak the mispasted token into the console.
        var result = SigningProviderFactory.BuildSignServerConfig(new SigningConfig
        {
            Provider = "signserver",
            BaseUrl = "https://sign.example.com",
            Worker = "W",
            AuthMode = "bearer",
            BearerTokenEnv = SecretShapedLiteral,
        });

        Assert.True(result.IsFailure);
        Assert.Contains("JSN019", result.Error.Message);
        Assert.Contains("bearerTokenEnv", result.Error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretShapedLiteral, result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Pem_KeyPathHoldsSecretShapedValue_ErrorNamesFieldWithoutEchoingValue()
    {
        // A secret mispasted into keyPath resolves to a non-existent file; the missing-file
        // error must reference signing.keyPath, not print the resolved path (= the secret).
        var result = SigningProviderFactory.Create(
            new SigningConfig { Provider = "pem", KeyPath = SecretShapedLiteral }, _tempDir);

        Assert.True(result.IsFailure);
        Assert.Contains("JSN019", result.Error.Message);
        Assert.Contains("keyPath", result.Error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretShapedLiteral, result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SignServer_ClientCertPathEnvValueIsSecretShaped_ErrorNamesFieldWithoutEchoingValue()
    {
        // Here the env var IS set, but its VALUE is a mispasted secret rather than a PFX path.
        // The file-not-found error must not echo that value.
        var certPathEnv = SetEnv(SecretShapedLiteral);

        var result = SigningProviderFactory.BuildSignServerConfig(new SigningConfig
        {
            Provider = "signserver",
            BaseUrl = "https://sign.example.com",
            Worker = "W",
            AuthMode = "clientcert",
            ClientCertPathEnv = certPathEnv,
        });

        Assert.True(result.IsFailure);
        Assert.Contains("JSN019", result.Error.Message);
        Assert.Contains("clientCertPathEnv", result.Error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretShapedLiteral, result.Error.Message, StringComparison.OrdinalIgnoreCase);
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
