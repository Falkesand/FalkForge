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
///
/// <para>
/// <strong>Exemption applies to <c>exception.message</c> too.</strong> Because redaction is
/// key-based, not value-based, <see cref="FalkForge.Diagnostics.LogProperties.MergeException"/>'s
/// <c>exception.message</c> property (the raw <see cref="Exception.Message"/> text) is exempt from
/// the same free-text limitation as the <c>message</c> field: a secret embedded in an exception's
/// message (e.g. a connection string surfaced by a <c>SqlException</c>) is <em>not</em> masked.
/// Callers must not let secrets reach exception text any more than they let them reach log
/// messages.
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
    /// Default deny-list of secret-indicating key tokens, in compact lowercase form (no
    /// separators). Compound tokens only — deliberately excludes bare "key" or "auth", which
    /// would over-mask benign keys such as <c>PublicKeyThumbprint</c>, <c>KeyId</c>,
    /// <c>KeyName</c>, or <c>AuthorName</c>.
    /// <para>
    /// Matching normalizes the property key (strip <c>-</c>, <c>_</c>, whitespace; lowercase)
    /// before testing for a substring match, so hyphenated/underscored/mixed-case variants such
    /// as <c>api-key</c>, <c>connection_string</c>, or <c>Private-Key</c> are still caught by
    /// the compact <c>apikey</c> / <c>connectionstring</c> / <c>privatekey</c> tokens below —
    /// there is no need for separate separator-variant entries.
    /// </para>
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
        "privatekey",
        "passphrase",
        "authorization",
        "bearer",
        "connectionstring",
        "signingkey",
        "pfx",
        "pem",
    ];

    /// <summary>
    /// Returns <paramref name="properties"/> unchanged when there is nothing to redact
    /// (<see langword="null"/>, empty, or an empty <paramref name="secretKeyTokens"/> deny-list —
    /// the latter is the documented opt-out). Otherwise <strong>always</strong> returns a new
    /// dictionary — a point-in-time snapshot of <paramref name="properties"/> with any value
    /// whose normalized key contains a token from <paramref name="secretKeyTokens"/> replaced by
    /// <see cref="RedactedValue"/>, and all other entries copied as-is. Snapshotting on both the
    /// match and no-match path (not just the match path) matters because the caller's
    /// dictionary reference could otherwise be mutated after this call returns but before the
    /// entry reaches the log sink (the write is queued and flushed asynchronously) — see
    /// <see cref="EngineLogger.Log(LogLevel, string, string, IReadOnlyDictionary{string, string}?)"/>.
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

        // Always hand back a fresh copy — even when no key matched — so the caller's live
        // dictionary reference is never stored in the (async-flushed) log entry.
        return redacted ?? new Dictionary<string, string>(properties);
    }

    private static bool ContainsSecretToken(string key, IReadOnlyList<string> secretKeyTokens)
    {
        var normalizedKey = NormalizeKey(key);

        foreach (var token in secretKeyTokens)
        {
            if (normalizedKey.Contains(NormalizeKey(token), StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Lowercases <paramref name="value"/> and strips <c>-</c>, <c>_</c>, and whitespace, so
    /// that separator/case variants of the same logical key (e.g. <c>api-key</c>,
    /// <c>API_KEY</c>, <c>ApiKey</c>) all normalize to the same compact form (<c>apikey</c>)
    /// before substring matching.
    /// </summary>
    private static string NormalizeKey(string value)
    {
        Span<char> buffer = value.Length <= 128 ? stackalloc char[value.Length] : new char[value.Length];
        var count = 0;

        foreach (var c in value)
        {
            if (c is '-' or '_' || char.IsWhiteSpace(c))
                continue;

            buffer[count++] = char.ToLowerInvariant(c);
        }

        return new string(buffer[..count]);
    }
}
