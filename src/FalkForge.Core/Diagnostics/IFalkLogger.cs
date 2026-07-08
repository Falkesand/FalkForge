namespace FalkForge.Diagnostics;

/// <summary>
/// Structured logging contract shared across FalkForge processes and libraries
/// (install-time Engine, build-time compilers/decompiler, plugins).
/// All methods are synchronous to avoid async overhead in hot paths.
/// </summary>
public interface IFalkLogger : IDisposable
{
    /// <summary>
    /// Minimum level that will be written. Entries below this level are discarded.
    /// </summary>
    /// <remarks>
    /// Reads return the currently effective minimum level. Writes are equivalent to
    /// <see cref="SetMinimumLevel(LogLevel)"/> and are safe to perform concurrently
    /// with <see cref="Log(LogLevel, string, string, IReadOnlyDictionary{string, string}?)"/> calls.
    /// </remarks>
    LogLevel MinimumLevel { get; set; }

    /// <summary>
    /// Atomically updates the minimum log level. Subsequent <see cref="Log(LogLevel, string, string, IReadOnlyDictionary{string, string}?)"/>
    /// calls observe the new level. Safe to call concurrently with active log writers.
    /// </summary>
    /// <param name="level">New minimum level. Entries below this level are discarded before any allocation.</param>
    void SetMinimumLevel(LogLevel level);

    /// <summary>
    /// Session correlation id stamped on every <see cref="LogEntry"/> and forwarded
    /// in <c>FalkForge.Engine.Protocol.Messages.LogMessage</c> frames so that
    /// log streams from the UI, Engine, and Elevation processes can be correlated.
    /// Set once at session start before any log calls.
    /// </summary>
    Guid SessionCorrelationId { get; set; }

    /// <summary>
    /// Logs an entry at the specified level with optional structured properties.
    /// </summary>
    void Log(LogLevel level, string category, string message, IReadOnlyDictionary<string, string>? properties = null);

    /// <summary>
    /// Logs an entry at the specified level together with the exception that triggered it.
    /// Implementations should fold the exception's type, message, and stack trace into the
    /// entry (as structured properties or an equivalent log-line detail) so the diagnostic
    /// trail is not lost to <c>ex.Message</c>-only interpolation.
    /// </summary>
    void Log(LogLevel level, string category, string message, Exception? exception, IReadOnlyDictionary<string, string>? properties = null);

    /// <summary>Logs at Verbose level.</summary>
    void Verbose(string category, string message);

    /// <summary>Logs at Debug level.</summary>
    void Debug(string category, string message);

    /// <summary>Logs at Info level.</summary>
    void Info(string category, string message);

    /// <summary>Logs at Warning level.</summary>
    void Warning(string category, string message);

    /// <summary>Logs at Error level.</summary>
    void Error(string category, string message);
}
