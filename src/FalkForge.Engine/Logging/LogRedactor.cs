namespace FalkForge.Engine.Logging;

/// <summary>
/// Masks secret-valued log properties by a deny-list of key tokens before a
/// <see cref="FalkForge.Diagnostics.LogEntry"/> is constructed, so neither the file sink
/// nor the pipe-callback sink ever persists or forwards a secret value.
///
/// <para>
/// <strong>Scope (v1) — properties only.</strong> This redacts structured property
/// <em>values</em> keyed by a secret-indicating token. It does <em>not</em> scan the free-text
/// <c>message</c> field: <see cref="FalkForge.Diagnostics.LogEntry.Message"/> is a pre-formatted
/// opaque string (already interpolated by the caller), so there is no reliable, low-cost way to
/// detect embedded secrets there without a false-positive-prone heuristic scanner. Callers must
/// not interpolate secret values into log messages — pass them as properties instead, where this
/// redactor can see and mask them.
/// </para>
/// </summary>
internal static class LogRedactor
{
    /// <summary>
    /// Replacement value written in place of a masked property value. The key is preserved so an
    /// operator can still see which property was present, just not its value.
    /// </summary>
    public const string RedactedValue = "***REDACTED***";

    /// <summary>
    /// Default deny-list of secret-indicating key tokens (matched case-insensitively as a
    /// substring of the property key). Compound tokens only — deliberately excludes bare
    /// "key" or "auth", which would over-mask benign keys such as <c>PublicKeyThumbprint</c>,
    /// <c>KeyId</c>, <c>KeyName</c>, or <c>AuthorName</c>.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultSecretKeyTokens =
    [
        "password",
        "passwd",
        "pwd",
        "secret",
        "token",
        "credential",
        "apikey",
        "api_key",
        "privatekey",
        "private_key",
        "passphrase",
        "authorization",
        "bearer",
        "connectionstring",
    ];

    /// <summary>
    /// Returns <paramref name="properties"/> unchanged (same instance, no allocation) when there
    /// is nothing to redact. Otherwise returns a new dictionary with any value whose key contains
    /// (case-insensitive) a token from <paramref name="secretKeyTokens"/> replaced by
    /// <see cref="RedactedValue"/>; all other entries are copied as-is.
    /// </summary>
    public static IReadOnlyDictionary<string, string>? Redact(
        IReadOnlyDictionary<string, string>? properties,
        IReadOnlyList<string> secretKeyTokens)
    {
        if (properties is null || properties.Count == 0 || secretKeyTokens.Count == 0)
            return properties;

        Dictionary<string, string>? redacted = null;

        foreach (var kvp in properties)
        {
            if (!ContainsSecretToken(kvp.Key, secretKeyTokens))
                continue;

            // First match: materialise the copy, seeded with everything seen so far.
            redacted ??= new Dictionary<string, string>(properties);
            redacted[kvp.Key] = RedactedValue;
        }

        return redacted ?? properties;
    }

    private static bool ContainsSecretToken(string key, IReadOnlyList<string> secretKeyTokens)
    {
        foreach (var token in secretKeyTokens)
        {
            if (key.Contains(token, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
