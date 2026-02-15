namespace FalkInstaller.Engine.Logging;

using FalkInstaller.Engine.Protocol;

/// <summary>
/// Structured logging interface for the engine process.
/// All methods are synchronous to avoid async overhead in hot paths.
/// </summary>
public interface IEngineLogger : IDisposable
{
    /// <summary>
    /// Minimum level that will be written. Entries below this level are discarded.
    /// </summary>
    LogLevel MinimumLevel { get; set; }

    /// <summary>
    /// Logs an entry at the specified level with optional structured properties.
    /// </summary>
    void Log(LogLevel level, string category, string message, IReadOnlyDictionary<string, string>? properties = null);

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
