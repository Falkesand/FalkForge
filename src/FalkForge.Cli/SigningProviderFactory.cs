using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FalkForge.Cli.Models;
using FalkForge.Signing;
using FalkForge.Signing.SignServer;

namespace FalkForge.Cli;

/// <summary>
/// Resolves a structurally validated <see cref="SigningConfig"/> (see
/// <see cref="JsonConfigLoader.LoadSigningFromString"/>) into a concrete
/// <see cref="ISignatureProvider"/> at build time. This is where environment-referenced
/// material is dereferenced: the config carries only env var NAMES and file paths; the
/// VALUES are read here. Every unresolvable reference fails closed with JSN019 — the build
/// never falls back to unsigned output or an unauthenticated signing request.
/// </summary>
internal static class SigningProviderFactory
{
    /// <summary>
    /// Creates the provider for <paramref name="config"/>, or <see cref="ResolvedSigning.None"/>
    /// when signing is absent or explicitly <c>none</c>. Relative key paths resolve against
    /// <paramref name="baseDirectory"/> (the config file's directory), matching every other path
    /// in the JSON config.
    /// </summary>
    public static Result<ResolvedSigning> Create(SigningConfig? config, string baseDirectory)
    {
        if (config is null || config.Provider is null
            || string.Equals(config.Provider, "none", StringComparison.OrdinalIgnoreCase))
        {
            return Result<ResolvedSigning>.Success(ResolvedSigning.None);
        }

        if (string.Equals(config.Provider, "pem", StringComparison.OrdinalIgnoreCase))
            return CreatePem(config, baseDirectory);

        // "signserver" is the only other value the loader lets through.
        var signServerConfig = BuildSignServerConfig(config);
        if (signServerConfig.IsFailure)
            return Result<ResolvedSigning>.Failure(signServerConfig.Error);

        var warnings = new List<string>();
        if (signServerConfig.Value.BaseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            warnings.Add("Signing: SignServer baseUrl uses http:// — the canonical manifest message and any credential travel in cleartext. Use https:// outside local CE test containers.");
        if (signServerConfig.Value.AuthMode == SignServerAuthMode.None)
            warnings.Add("Signing: SignServer authMode is 'none' (unauthenticated NOAUTH) — production should use clientcert (mTLS) or bearer.");

        return Result<ResolvedSigning>.Success(
            new ResolvedSigning(new SignServerSignatureProvider(signServerConfig.Value), warnings));
    }

    private static Result<ResolvedSigning> CreatePem(SigningConfig config, string baseDirectory)
    {
        if (!string.IsNullOrWhiteSpace(config.KeyEnv))
        {
            var pem = RequireEnv(config.KeyEnv, "keyEnv");
            if (pem.IsFailure)
                return Result<ResolvedSigning>.Failure(pem.Error);

            return Result<ResolvedSigning>.Success(
                new ResolvedSigning(PemSignatureProvider.FromPemContent(pem.Value), []));
        }

        // The loader guarantees exactly one key source, so KeyPath is set here.
        var keyPath = Path.IsPathRooted(config.KeyPath!)
            ? config.KeyPath!
            : Path.GetFullPath(Path.Combine(baseDirectory, config.KeyPath!));

        if (!File.Exists(keyPath))
            return Result<ResolvedSigning>.Failure(new Error(ErrorKind.SecurityError,
                $"JSN019: Signing key file not found: '{keyPath}'. The build fails closed rather than producing an unsigned bundle."));

        return Result<ResolvedSigning>.Success(
            new ResolvedSigning(new PemSignatureProvider(keyPath), []));
    }

    /// <summary>
    /// Assembles the <see cref="SignServerConfig"/> from the JSON section plus the environment
    /// variables it names. Internal (not private) so tests can pin that env VALUES — not the
    /// names — end up in the config, without performing network I/O.
    /// </summary>
    internal static Result<SignServerConfig> BuildSignServerConfig(SigningConfig config)
    {
        // Loader validation guarantees BaseUrl/Worker presence and a recognized AuthMode.
        var authMode = SignServerAuthMode.None;
        if (!string.IsNullOrWhiteSpace(config.AuthMode))
            _ = Enum.TryParse(config.AuthMode, ignoreCase: true, out authMode);

        var result = new SignServerConfig
        {
            BaseUrl = config.BaseUrl!,
            Worker = config.Worker!,
            AuthMode = authMode,
            KeyId = config.KeyId ?? string.Empty,
        };

        switch (authMode)
        {
            case SignServerAuthMode.Bearer:
            {
                var token = RequireEnv(config.BearerTokenEnv!, "bearerTokenEnv");
                if (token.IsFailure)
                    return Result<SignServerConfig>.Failure(token.Error);
                result = result with { BearerToken = token.Value };
                break;
            }
            case SignServerAuthMode.Basic:
            {
                var username = RequireEnv(config.UsernameEnv!, "usernameEnv");
                if (username.IsFailure)
                    return Result<SignServerConfig>.Failure(username.Error);
                var password = RequireEnv(config.PasswordEnv!, "passwordEnv");
                if (password.IsFailure)
                    return Result<SignServerConfig>.Failure(password.Error);
                result = result with { BasicUsername = username.Value, BasicPassword = password.Value };
                break;
            }
            case SignServerAuthMode.ClientCert:
            {
                var certificate = LoadClientCertificate(config);
                if (certificate.IsFailure)
                    return Result<SignServerConfig>.Failure(certificate.Error);
                result = result with { ClientCertificate = certificate.Value };
                break;
            }
            case SignServerAuthMode.None:
            default:
                break;
        }

        return Result<SignServerConfig>.Success(result);
    }

    private static Result<X509Certificate2> LoadClientCertificate(SigningConfig config)
    {
        var pfxPath = RequireEnv(config.ClientCertPathEnv!, "clientCertPathEnv");
        if (pfxPath.IsFailure)
            return Result<X509Certificate2>.Failure(pfxPath.Error);

        if (!File.Exists(pfxPath.Value))
            return Result<X509Certificate2>.Failure(new Error(ErrorKind.SecurityError,
                $"JSN019: Client certificate file not found: '{pfxPath.Value}' (from signing.clientCertPathEnv)."));

        string? password = null;
        if (!string.IsNullOrWhiteSpace(config.ClientCertPasswordEnv))
        {
            var passwordResult = RequireEnv(config.ClientCertPasswordEnv, "clientCertPasswordEnv");
            if (passwordResult.IsFailure)
                return Result<X509Certificate2>.Failure(passwordResult.Error);
            password = passwordResult.Value;
        }

        try
        {
            return Result<X509Certificate2>.Success(
                X509CertificateLoader.LoadPkcs12FromFile(pfxPath.Value, password));
        }
        catch (CryptographicException ex)
        {
            return Result<X509Certificate2>.Failure(new Error(ErrorKind.SecurityError,
                $"JSN019: Failed to load client certificate from '{pfxPath.Value}': {ex.Message}"));
        }
    }

    private static Result<string> RequireEnv(string envName, string field)
    {
        var value = Environment.GetEnvironmentVariable(envName);
        if (string.IsNullOrEmpty(value))
            return Result<string>.Failure(new Error(ErrorKind.SecurityError,
                $"JSN019: Environment variable '{envName}' (signing.{field}) is not set — the build fails closed rather than signing without it."));

        return Result<string>.Success(value);
    }
}
