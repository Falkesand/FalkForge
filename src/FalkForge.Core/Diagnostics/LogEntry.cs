namespace FalkForge.Diagnostics;

/// <summary>
/// Immutable structured log entry.
/// </summary>
public readonly record struct LogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    string Message,
    IReadOnlyDictionary<string, string>? Properties,
    Guid SessionCorrelationId = default);
