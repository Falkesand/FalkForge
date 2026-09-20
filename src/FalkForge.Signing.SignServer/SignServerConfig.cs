using System.Security.Cryptography.X509Certificates;
using FalkForge.Configuration;

namespace FalkForge.Signing.SignServer;

/// <summary>
/// Connection + authentication settings for a <see cref="SignServerSignatureProvider"/>. All secret
/// material (bearer token, Basic password, client certificate) is supplied by the caller — sourced from
/// configuration or environment via <see cref="FromEnvironment"/> — and never hard-coded in the provider.
/// </summary>
public sealed record SignServerConfig
{
    /// <summary>
    /// Base URL of the SignServer instance, e.g. <c>https://signserver.example.com:8443</c>.
    /// <para>HTTPS is required unless <see cref="AllowInsecureHttpForDevelopment"/> explicitly enables
    /// unauthenticated HTTP for a local development container.</para>
    /// </summary>
    public required string BaseUrl { get; init; }

    /// <summary>The PlainSigner worker name or numeric id that produces the signature.</summary>
    public required string Worker { get; init; }

    /// <summary>
    /// How to authenticate to the endpoint. Default <see cref="SignServerAuthMode.None"/>.
    /// <para><b>Production SHOULD use <see cref="SignServerAuthMode.ClientCert"/> (mTLS) or
    /// <see cref="SignServerAuthMode.Bearer"/>.</b> <see cref="SignServerAuthMode.None"/> matches
    /// SignServer's NOAUTH worker mode and exists for local CE test containers; an unauthenticated
    /// production signer would let anyone on the network request signatures from the release key.</para>
    /// </summary>
    public SignServerAuthMode AuthMode { get; init; } = SignServerAuthMode.None;

    /// <summary>
    /// Allows plain HTTP only for an unauthenticated development SignServer. Defaults to <see langword="false"/>.
    /// When HTTP is used, this option is rejected unless <see cref="AuthMode"/> is <see cref="SignServerAuthMode.None"/>.
    /// </summary>
    public bool AllowInsecureHttpForDevelopment { get; init; }

    /// <summary>Bearer token used when <see cref="AuthMode"/> is <see cref="SignServerAuthMode.Bearer"/>.</summary>
    public string? BearerToken { get; init; }

    /// <summary>Username used when <see cref="AuthMode"/> is <see cref="SignServerAuthMode.Basic"/>.</summary>
    public string? BasicUsername { get; init; }

    /// <summary>Password used when <see cref="AuthMode"/> is <see cref="SignServerAuthMode.Basic"/>.</summary>
    public string? BasicPassword { get; init; }

    /// <summary>
    /// Client certificate (with private key) presented for mTLS when <see cref="AuthMode"/> is
    /// <see cref="SignServerAuthMode.ClientCert"/>. Only used when the provider constructs its own
    /// <c>HttpClient</c>; when a handler is injected the caller wires the certificate.
    /// </summary>
    public X509Certificate2? ClientCertificate { get; init; }

    /// <summary>
    /// Optional operator-facing key label copied verbatim into the envelope's <c>keyId</c>. Informational
    /// only — never trusted, never affects verification.
    /// </summary>
    public string KeyId { get; init; } = string.Empty;

    /// <summary>
    /// Builds a config from environment variables so <c>forge build</c>/CI can configure remote signing
    /// without secrets in source: <c>SIGNSERVER_URL</c>, <c>SIGNSERVER_WORKER</c>, <c>SIGNSERVER_AUTH</c>
    /// (<c>none|clientcert|basic|bearer</c>), <c>SIGNSERVER_BEARER_TOKEN</c>, <c>SIGNSERVER_BASIC_USER</c>,
    /// <c>SIGNSERVER_BASIC_PASS</c>, <c>SIGNSERVER_CLIENT_CERT</c> (PFX path) + <c>SIGNSERVER_CLIENT_CERT_PASSWORD</c>,
    /// <c>SIGNSERVER_KEY_ID</c>, and <c>SIGNSERVER_ALLOW_INSECURE_HTTP</c>. Fails loud (SGN024/SGN025)
    /// when configuration is missing or transport security is invalid.
    /// </summary>
    public static Result<SignServerConfig> FromEnvironment()
    {
        var baseUrl = EnvVarCatalog.GetRaw(EnvVarCatalog.SignServerUrl);
        var worker = EnvVarCatalog.GetRaw(EnvVarCatalog.SignServerWorker);

        if (string.IsNullOrWhiteSpace(baseUrl))
            return Result<SignServerConfig>.Failure(ErrorKind.SecurityError,
                "SGN024: SIGNSERVER_URL is not set.");
        if (string.IsNullOrWhiteSpace(worker))
            return Result<SignServerConfig>.Failure(ErrorKind.SecurityError,
                "SGN024: SIGNSERVER_WORKER is not set.");

        var authRaw = EnvVarCatalog.GetRaw(EnvVarCatalog.SignServerAuth);
        var authMode = SignServerAuthMode.None;
        if (!string.IsNullOrWhiteSpace(authRaw) && !Enum.TryParse(authRaw, ignoreCase: true, out authMode))
            return Result<SignServerConfig>.Failure(ErrorKind.SecurityError,
                $"SGN024: SIGNSERVER_AUTH value '{authRaw}' is not one of none|clientcert|basic|bearer.");

        var allowInsecureRaw = EnvVarCatalog.GetRaw(EnvVarCatalog.SignServerAllowInsecureHttp);
        var allowInsecureHttp = false;
        if (!string.IsNullOrWhiteSpace(allowInsecureRaw)
            && !bool.TryParse(allowInsecureRaw, out allowInsecureHttp))
        {
            return Result<SignServerConfig>.Failure(ErrorKind.SecurityError,
                "SGN024: SIGNSERVER_ALLOW_INSECURE_HTTP must be 'true' or 'false' when specified.");
        }

        var config = new SignServerConfig
        {
            BaseUrl = baseUrl,
            Worker = worker,
            AuthMode = authMode,
            AllowInsecureHttpForDevelopment = allowInsecureHttp,
            BearerToken = EnvVarCatalog.GetRaw(EnvVarCatalog.SignServerBearerToken),
            BasicUsername = EnvVarCatalog.GetRaw(EnvVarCatalog.SignServerBasicUser),
            BasicPassword = EnvVarCatalog.GetRaw(EnvVarCatalog.SignServerBasicPass),
            KeyId = EnvVarCatalog.GetRaw(EnvVarCatalog.SignServerKeyId) ?? string.Empty
        };

        var transport = config.ValidateTransportSecurity();
        if (transport.IsFailure)
            return Result<SignServerConfig>.Failure(transport.Error);

        switch (authMode)
        {
            case SignServerAuthMode.Bearer when string.IsNullOrEmpty(config.BearerToken):
                return Result<SignServerConfig>.Failure(ErrorKind.SecurityError,
                    "SGN024: SIGNSERVER_AUTH=bearer requires SIGNSERVER_BEARER_TOKEN.");
            case SignServerAuthMode.Basic when string.IsNullOrEmpty(config.BasicUsername) || config.BasicPassword is null:
                return Result<SignServerConfig>.Failure(ErrorKind.SecurityError,
                    "SGN024: SIGNSERVER_AUTH=basic requires SIGNSERVER_BASIC_USER and SIGNSERVER_BASIC_PASS.");
            case SignServerAuthMode.ClientCert:
                var certResult = LoadClientCertFromEnvironment();
                if (certResult.IsFailure)
                    return Result<SignServerConfig>.Failure(certResult.Error);
                config = config with { ClientCertificate = certResult.Value };
                break;
            default:
                break;
        }

        return config;
    }

    /// <summary>Validates that the endpoint uses HTTPS, or explicitly opted-in unauthenticated development HTTP.</summary>
    public Result<Unit> ValidateTransportSecurity()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri))
            return Result<Unit>.Failure(ErrorKind.SecurityError,
                "SGN025: SignServer base URL must be an absolute HTTPS URL.");

        if (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return Unit.Value;

        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            return Result<Unit>.Failure(ErrorKind.SecurityError,
                "SGN025: SignServer base URL must use HTTPS.");

        if (!AllowInsecureHttpForDevelopment)
            return Result<Unit>.Failure(ErrorKind.SecurityError,
                "SGN025: SignServer HTTP is disabled by default. Use HTTPS, or explicitly enable development HTTP.");

        if (AuthMode != SignServerAuthMode.None)
            return Result<Unit>.Failure(ErrorKind.SecurityError,
                "SGN025: SignServer development HTTP cannot be combined with Basic, Bearer, or client-certificate authentication.");

        return Unit.Value;
    }

    private static Result<X509Certificate2> LoadClientCertFromEnvironment()
    {
        var certPath = EnvVarCatalog.GetRaw(EnvVarCatalog.SignServerClientCert);
        if (string.IsNullOrWhiteSpace(certPath))
            return Result<X509Certificate2>.Failure(ErrorKind.SecurityError,
                "SGN024: SIGNSERVER_AUTH=clientcert requires SIGNSERVER_CLIENT_CERT (PFX path).");
        if (!File.Exists(certPath))
            return Result<X509Certificate2>.Failure(ErrorKind.SecurityError,
                $"SGN024: client certificate file not found at '{certPath}'.");

        var password = EnvVarCatalog.GetRaw(EnvVarCatalog.SignServerClientCertPassword);
        try
        {
            return X509CertificateLoader.LoadPkcs12FromFile(certPath, password);
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            return Result<X509Certificate2>.Failure(ErrorKind.SecurityError,
                $"SGN024: failed to load client certificate from '{certPath}': {ex.Message}");
        }
    }
}
