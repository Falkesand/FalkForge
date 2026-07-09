using FalkForge.Signing.SignServer;
using Xunit;

namespace FalkForge.Signing.SignServer.Tests;

/// <summary>
/// Pins <see cref="SignServerConfig.FromEnvironment"/>: it must build a usable config from environment
/// variables without any secret in source, and fail loud (SGN024) when required material is missing so a
/// misconfigured CI run cannot silently fall back to no signing. All SIGNSERVER_* variables touched here are
/// unique to this suite and restored after each test, so the suite is self-contained.
/// </summary>
public sealed class SignServerConfigTests : IDisposable
{
    private static readonly string[] Vars =
    [
        "SIGNSERVER_URL", "SIGNSERVER_WORKER", "SIGNSERVER_AUTH", "SIGNSERVER_BEARER_TOKEN",
        "SIGNSERVER_BASIC_USER", "SIGNSERVER_BASIC_PASS", "SIGNSERVER_CLIENT_CERT",
        "SIGNSERVER_CLIENT_CERT_PASSWORD", "SIGNSERVER_KEY_ID"
    ];

    private readonly Dictionary<string, string?> _saved = new();

    public SignServerConfigTests()
    {
        foreach (var v in Vars)
        {
            _saved[v] = Environment.GetEnvironmentVariable(v);
            Environment.SetEnvironmentVariable(v, null);
        }
    }

    public void Dispose()
    {
        foreach (var v in Vars)
            Environment.SetEnvironmentVariable(v, _saved[v]);
    }

    [Fact]
    public void FromEnvironment_MissingUrl_FailsWithSgn024()
    {
        Environment.SetEnvironmentVariable("SIGNSERVER_WORKER", "w");

        var result = SignServerConfig.FromEnvironment();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("SGN024", result.Error.Message);
    }

    [Fact]
    public void FromEnvironment_BearerWithoutToken_FailsWithSgn024()
    {
        Environment.SetEnvironmentVariable("SIGNSERVER_URL", "https://sign.example:8443");
        Environment.SetEnvironmentVariable("SIGNSERVER_WORKER", "PlainECDSA");
        Environment.SetEnvironmentVariable("SIGNSERVER_AUTH", "bearer");

        var result = SignServerConfig.FromEnvironment();

        Assert.True(result.IsFailure);
        Assert.Contains("SGN024", result.Error.Message);
    }

    [Fact]
    public void FromEnvironment_UnknownAuthMode_FailsWithSgn024()
    {
        Environment.SetEnvironmentVariable("SIGNSERVER_URL", "https://sign.example:8443");
        Environment.SetEnvironmentVariable("SIGNSERVER_WORKER", "PlainECDSA");
        Environment.SetEnvironmentVariable("SIGNSERVER_AUTH", "kerberos");

        var result = SignServerConfig.FromEnvironment();

        Assert.True(result.IsFailure);
        Assert.Contains("SGN024", result.Error.Message);
    }

    [Fact]
    public void FromEnvironment_ValidBearer_BuildsConfig()
    {
        Environment.SetEnvironmentVariable("SIGNSERVER_URL", "https://sign.example:8443");
        Environment.SetEnvironmentVariable("SIGNSERVER_WORKER", "PlainECDSA");
        Environment.SetEnvironmentVariable("SIGNSERVER_AUTH", "bearer");
        Environment.SetEnvironmentVariable("SIGNSERVER_BEARER_TOKEN", "tok");
        Environment.SetEnvironmentVariable("SIGNSERVER_KEY_ID", "label");

        var result = SignServerConfig.FromEnvironment();

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal("https://sign.example:8443", result.Value.BaseUrl);
        Assert.Equal("PlainECDSA", result.Value.Worker);
        Assert.Equal(SignServerAuthMode.Bearer, result.Value.AuthMode);
        Assert.Equal("tok", result.Value.BearerToken);
        Assert.Equal("label", result.Value.KeyId);
    }
}
