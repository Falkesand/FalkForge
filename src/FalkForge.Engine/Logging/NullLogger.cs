namespace FalkForge.Engine.Logging;

using FalkForge.Engine.Protocol;

/// <summary>
/// No-op logger implementation for testing and default initialization.
/// </summary>
public sealed class NullLogger : IEngineLogger
{
    public LogLevel MinimumLevel { get; set; } = LogLevel.Info;
    public Guid SessionCorrelationId { get; set; }

    public void Log(LogLevel level, string category, string message, IReadOnlyDictionary<string, string>? properties = null)
    {
        // Intentionally empty
    }

    public void Verbose(string category, string message) { }
    public void Debug(string category, string message) { }
    public void Info(string category, string message) { }
    public void Warning(string category, string message) { }
    public void Error(string category, string message) { }

    public void Dispose()
    {
        // Nothing to dispose
    }
}
