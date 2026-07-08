namespace FalkForge.Testing;

using FalkForge.Diagnostics;

/// <summary>
/// In-memory <see cref="IFalkLogger"/> for tests. All log entries are accumulated
/// in <see cref="Entries"/> for assertion. Thread-safe via a lock.
/// </summary>
public sealed class ListLogger : IFalkLogger
{
    private readonly List<LogEntry> _entries = [];
    private readonly Lock _lock = new();

    /// <inheritdoc/>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Verbose;

    /// <inheritdoc/>
    public void SetMinimumLevel(LogLevel level) => MinimumLevel = level;

    /// <inheritdoc/>
    public Guid SessionCorrelationId { get; set; }

    /// <summary>All log entries written since construction.</summary>
    public IReadOnlyList<LogEntry> Entries
    {
        get { lock (_lock) { return _entries.ToArray(); } }
    }

    /// <summary>
    /// Returns only entries at or above the given <paramref name="level"/>.
    /// </summary>
    public IReadOnlyList<LogEntry> EntriesAt(LogLevel level)
        => Entries.Where(e => e.Level >= level).ToArray();

    /// <inheritdoc/>
    public void Log(LogLevel level, string category, string message,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        if (level < MinimumLevel) return;
        lock (_lock)
        {
            _entries.Add(new LogEntry(
                Timestamp: DateTimeOffset.UtcNow,
                Level: level,
                Category: category,
                Message: message,
                Properties: properties,
                SessionCorrelationId: SessionCorrelationId));
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Folds the exception's type, message, and stack trace into structured properties
    /// (keys <c>exception.type</c>, <c>exception.message</c>, <c>exception.stackTrace</c>)
    /// before delegating to the no-exception overload, matching <c>EngineLogger</c>'s convention.
    /// </remarks>
    public void Log(LogLevel level, string category, string message, Exception? exception,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        if (exception is null)
        {
            Log(level, category, message, properties);
            return;
        }

        var merged = properties is null
            ? new Dictionary<string, string>(3)
            : new Dictionary<string, string>(properties);
        merged["exception.type"] = exception.GetType().FullName ?? exception.GetType().Name;
        merged["exception.message"] = exception.Message;
        merged["exception.stackTrace"] = exception.StackTrace ?? string.Empty;

        Log(level, category, message, merged);
    }

    /// <inheritdoc/>
    public void Verbose(string category, string message) => Log(LogLevel.Verbose, category, message);

    /// <inheritdoc/>
    public void Debug(string category, string message) => Log(LogLevel.Debug, category, message);

    /// <inheritdoc/>
    public void Info(string category, string message) => Log(LogLevel.Info, category, message);

    /// <inheritdoc/>
    public void Warning(string category, string message) => Log(LogLevel.Warning, category, message);

    /// <inheritdoc/>
    public void Error(string category, string message) => Log(LogLevel.Error, category, message);

    /// <inheritdoc/>
    public void Dispose() { }

    /// <summary>Clears all accumulated entries.</summary>
    public void Clear()
    {
        lock (_lock) { _entries.Clear(); }
    }
}
