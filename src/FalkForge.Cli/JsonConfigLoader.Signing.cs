using FalkForge;
using FalkForge.Cli.Models;

namespace FalkForge.Cli;

public static partial class JsonConfigLoader
{
    private static Result<SigningConfig> ValidateSigning(SigningConfig? signing)
    {
        if (signing is null)
            return Result<SigningConfig>.Success(new SigningConfig { Provider = "none" });

        // The signing section is security-sensitive: unknown keys fail closed (a typo must not
        // silently disable authentication), and secret-looking keys get the explicit
        // "secrets come from the environment" guidance instead of being silently ignored.
        if (signing.UnknownFields is { Count: > 0 })
        {
            var name = signing.UnknownFields.Keys.First();
            return SigningFailure(LooksLikeSecretName(name)
                ? $"JSN016: signing.{name} is not a valid field — secrets must come from environment variables, not the config file. Reference the environment variable NAME via the matching '*Env' field (e.g. bearerTokenEnv)."
                : $"JSN016: Unknown field 'signing.{name}'. The signing section rejects unrecognized fields.");
        }

        if (string.IsNullOrWhiteSpace(signing.Provider))
            return SigningFailure("JSN015: signing.provider is required. Valid values: none, pem, signserver");

        if (Eq(signing.Provider, "none"))
            return Result<SigningConfig>.Success(signing);

        if (Eq(signing.Provider, "pem"))
            return ValidatePemSigning(signing);

        if (Eq(signing.Provider, "signserver"))
            return ValidateSignServerSigning(signing);

        return SigningFailure($"JSN015: Unknown signing provider '{signing.Provider}'. Valid values: none, pem, signserver");
    }

    private static Result<SigningConfig> ValidatePemSigning(SigningConfig signing)
    {
        var hasPath = !string.IsNullOrWhiteSpace(signing.KeyPath);
        var hasEnv = !string.IsNullOrWhiteSpace(signing.KeyEnv);

        if (hasPath == hasEnv) // neither, or ambiguously both
            return SigningFailure("JSN017: signing provider 'pem' requires exactly one key source: 'keyPath' (PEM file path) or 'keyEnv' (environment variable name holding the PEM).");

        if (hasPath && signing.KeyPath!.Contains("-----BEGIN", StringComparison.Ordinal))
            return SigningFailure("JSN016: signing.keyPath contains inline PEM key material — secrets must come from a key FILE or an environment variable, not the config file.");

        if (hasEnv && !IsValidEnvVarName(signing.KeyEnv!))
            return SigningFailure("JSN016: signing.keyEnv must be an environment variable NAME (letters, digits, underscore) — it looks like literal key material.");

        // Hybrid post-quantum companion (optional; present ⇒ hybrid). The PQ key follows the SAME
        // secret rules as the classical key: file path or env var NAME, never inline material.
        var hasPqPath = !string.IsNullOrWhiteSpace(signing.PqKeyPath);
        var hasPqEnv = !string.IsNullOrWhiteSpace(signing.PqKeyEnv);

        if (hasPqPath && hasPqEnv) // ambiguously both — refusing beats picking one silently
            return SigningFailure("JSN017: signing provider 'pem' accepts at most one post-quantum key source: 'pqKeyPath' (ML-DSA PEM file path) or 'pqKeyEnv' (environment variable name holding the PEM).");

        if (hasPqPath && signing.PqKeyPath!.Contains("-----BEGIN", StringComparison.Ordinal))
            return SigningFailure("JSN016: signing.pqKeyPath contains inline PEM key material — secrets must come from a key FILE or an environment variable, not the config file.");

        if (hasPqEnv && !IsValidEnvVarName(signing.PqKeyEnv!))
            return SigningFailure("JSN016: signing.pqKeyEnv must be an environment variable NAME (letters, digits, underscore) — it looks like literal key material.");

        return Result<SigningConfig>.Success(signing);
    }

    private static Result<SigningConfig> ValidateSignServerSigning(SigningConfig signing)
    {
        // SignServer ML-DSA workers are a Stage-4 assessment (PQ-hybrid design §8.6): until then
        // the PQ fields are pem-only. Failing loud beats silently emitting a classical-only bundle
        // the author believes is hybrid-signed.
        if (!string.IsNullOrWhiteSpace(signing.PqKeyPath) || !string.IsNullOrWhiteSpace(signing.PqKeyEnv))
            return SigningFailure("JSN018: signing provider 'signserver' does not support 'pqKeyPath'/'pqKeyEnv' — hybrid post-quantum signing currently requires provider 'pem'.");

        if (string.IsNullOrWhiteSpace(signing.BaseUrl))
            return SigningFailure("JSN018: signing provider 'signserver' requires 'baseUrl'.");

        if (string.IsNullOrWhiteSpace(signing.Worker))
            return SigningFailure("JSN018: signing provider 'signserver' requires 'worker'.");

        var authMode = signing.AuthMode;
        if (!string.IsNullOrWhiteSpace(authMode)
            && !Eq(authMode, "none") && !Eq(authMode, "basic") && !Eq(authMode, "bearer") && !Eq(authMode, "clientcert"))
        {
            return SigningFailure($"JSN018: Unknown signing.authMode '{authMode}'. Valid values: none, basic, bearer, clientcert");
        }

        // Fail closed: a chosen auth mode must name its environment source up front — there is
        // no fallback to an unauthenticated request.
        if (Eq(authMode, "bearer") && string.IsNullOrWhiteSpace(signing.BearerTokenEnv))
            return SigningFailure("JSN018: signing.authMode 'bearer' requires 'bearerTokenEnv' (environment variable name holding the token).");

        if (Eq(authMode, "basic")
            && (string.IsNullOrWhiteSpace(signing.UsernameEnv) || string.IsNullOrWhiteSpace(signing.PasswordEnv)))
        {
            return SigningFailure("JSN018: signing.authMode 'basic' requires 'usernameEnv' and 'passwordEnv' (environment variable names).");
        }

        if (Eq(authMode, "clientcert") && string.IsNullOrWhiteSpace(signing.ClientCertPathEnv))
            return SigningFailure("JSN018: signing.authMode 'clientcert' requires 'clientCertPathEnv' (environment variable name holding the PFX path).");

        foreach (var (field, value) in new[]
                 {
                     ("bearerTokenEnv", signing.BearerTokenEnv),
                     ("usernameEnv", signing.UsernameEnv),
                     ("passwordEnv", signing.PasswordEnv),
                     ("clientCertPathEnv", signing.ClientCertPathEnv),
                     ("clientCertPasswordEnv", signing.ClientCertPasswordEnv),
                 })
        {
            if (!string.IsNullOrWhiteSpace(value) && !IsValidEnvVarName(value))
                return SigningFailure($"JSN016: signing.{field} must be an environment variable NAME (letters, digits, underscore) — it looks like a literal credential. Secrets must come from the environment, not the config file.");
        }

        return Result<SigningConfig>.Success(signing);
    }

    private static Result<SigningConfig> SigningFailure(string message) =>
        Result<SigningConfig>.Failure(new Error(ErrorKind.Validation, message));

    private static bool Eq(string? a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeSecretName(string name) =>
        name.Contains("token", StringComparison.OrdinalIgnoreCase)
        || name.Contains("password", StringComparison.OrdinalIgnoreCase)
        || name.Contains("secret", StringComparison.OrdinalIgnoreCase)
        || name.Contains("credential", StringComparison.OrdinalIgnoreCase)
        || name.Contains("passphrase", StringComparison.OrdinalIgnoreCase)
        || name.Contains("pem", StringComparison.OrdinalIgnoreCase)
        || name.Contains("key", StringComparison.OrdinalIgnoreCase);

    private static bool IsValidEnvVarName(string value)
    {
        if (value.Length == 0 || (!char.IsAsciiLetter(value[0]) && value[0] != '_'))
            return false;

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_')
                return false;
        }

        return true;
    }
}
