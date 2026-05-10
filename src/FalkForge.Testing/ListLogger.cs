namespace FalkForge.Testing;

using FalkForge.Engine.Logging;
using FalkForge.Engine.Protocol;

/// <summary>
/// In-memory <see cref="IEngineLogger"/> for tests. All log entries are accumulated
/// in <see cref="Entries"/> for assertion. Thread-safe via a lock.
/// </summary>
public sealed class ListLogger : IEngineLogger
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
