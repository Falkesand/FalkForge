namespace FalkForge.Engine.Logging;

using FalkForge.Engine.Protocol;

/// <summary>
/// Immutable structured log entry.
/// </summary>
public readonly record struct LogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    string Message,
    IReadOnlyDictionary<string, string>? Properties);
