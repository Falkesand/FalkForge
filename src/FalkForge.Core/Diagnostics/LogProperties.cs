namespace FalkForge.Diagnostics;

/// <summary>
/// Helpers for building the structured-property dictionaries attached to log entries.
/// </summary>
public static class LogProperties
{
    /// <summary>
    /// Builds the merged property dictionary for an exception-carrying log entry: the
    /// caller's properties (if any) plus the three <c>exception.*</c> keys folded in.
    /// Centralised so the key strings live in exactly one place across all
    /// <see cref="IFalkLogger"/> implementations.
    /// </summary>
    /// <param name="exception">The exception whose type, message, and stack trace are captured.</param>
    /// <param name="callerProperties">
    ///     Optional caller-supplied properties. Copied first; the <c>exception.*</c> keys are
    ///     applied on top (a caller key named <c>exception.*</c> would be overwritten).
    /// </param>
    /// <returns>A new dictionary containing the merged properties.</returns>
    public static IReadOnlyDictionary<string, string> MergeException(
        Exception exception,
        IReadOnlyDictionary<string, string>? callerProperties)
    {
        var merged = callerProperties is null
            ? new Dictionary<string, string>(3)
            : new Dictionary<string, string>(callerProperties);
        merged["exception.type"] = exception.GetType().FullName ?? exception.GetType().Name;
        merged["exception.message"] = exception.Message;
        merged["exception.stackTrace"] = exception.StackTrace ?? string.Empty;
        return merged;
    }
}
