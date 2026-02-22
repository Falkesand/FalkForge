namespace FalkForge.Engine.Logging;

using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using FalkForge.Engine.Protocol;

/// <summary>
/// Production structured logger that writes tab-separated entries to a log file.
/// Thread-safe via a concurrent queue and dedicated flush mechanism.
/// Optionally invokes a callback for each entry (e.g., to forward to the UI pipe).
/// </summary>
public sealed class EngineLogger : IEngineLogger
{
    private const int FlushThreshold = 32;
    private static readonly string[] LevelNames = ["VERBOSE", "DEBUG", "INFO", "WARNING", "ERROR"];

    private readonly ConcurrentQueue<LogEntry> _queue = new();
    private readonly StreamWriter _writer;
    private readonly Action<LogEntry>? _pipeCallback;
    private readonly object _flushLock = new();
    private volatile bool _disposed;

    public EngineLogger(string filePath, Action<LogEntry>? pipeCallback = null)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // SECURITY: Log file is created with FileShare.Read (not ReadWrite) to prevent
        // concurrent writes from other processes. The log directory under %TEMP% inherits
        // default ACLs from the user's temp folder, which on standard Windows configurations
        // restricts access to the current user + SYSTEM + Administrators. Per-session unique
        // directory names (see GetDefaultLogPath) further reduce predictability.
        // Full ACL restriction via DirectorySecurity is not used because the
        // System.IO.FileSystem.AccessControl APIs are not NativeAOT-compatible.
        var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(fileStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = false
        };

        _pipeCallback = pipeCallback;
    }

    public LogLevel MinimumLevel { get; set; } = LogLevel.Info;

    public void Log(LogLevel level, string category, string message, IReadOnlyDictionary<string, string>? properties = null)
    {
        if (_disposed)
            return;

        if (level < MinimumLevel)
            return;

        var entry = new LogEntry(DateTimeOffset.UtcNow, level, category, message, properties);

        _queue.Enqueue(entry);
        _pipeCallback?.Invoke(entry);

        // Flush immediately on Error, or when queue exceeds threshold
        if (level >= LogLevel.Error || _queue.Count >= FlushThreshold)
        {
            Flush();
        }
    }

    public void Verbose(string category, string message) =>
        Log(LogLevel.Verbose, category, message);

    public void Debug(string category, string message) =>
        Log(LogLevel.Debug, category, message);

    public void Info(string category, string message) =>
        Log(LogLevel.Info, category, message);

    public void Warning(string category, string message) =>
        Log(LogLevel.Warning, category, message);

    public void Error(string category, string message) =>
        Log(LogLevel.Error, category, message);

    /// <summary>
    /// Generates a default log file path under %TEMP%\FalkForge\{session}.
    /// Uses a per-session GUID to prevent cross-process log file conflicts
    /// and reduce predictability of log file paths.
    /// </summary>
    public static string GetDefaultLogPath()
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var sessionId = Guid.NewGuid().ToString("N");
        return Path.Combine(Path.GetTempPath(), "FalkForge", sessionId, $"install_{timestamp}.log");
    }

    private void Flush()
    {
        lock (_flushLock)
        {
            if (_disposed)
                return;

            while (_queue.TryDequeue(out var entry))
            {
                WriteEntry(entry);
            }

            _writer.Flush();
        }
    }

    private void WriteEntry(LogEntry entry)
    {
        var timestamp = entry.Timestamp.ToString("o", CultureInfo.InvariantCulture);
        var levelName = GetLevelName(entry.Level);
        var propertiesJson = FormatProperties(entry.Properties);

        _writer.Write(timestamp);
        _writer.Write('\t');
        _writer.Write(levelName);
        _writer.Write('\t');
        _writer.Write(entry.Category);
        _writer.Write('\t');
        _writer.Write(entry.Message);
        _writer.Write('\t');
        _writer.Write(propertiesJson);
        _writer.WriteLine();
    }

    private static string GetLevelName(LogLevel level)
    {
        var index = (int)level;
        if (index >= 0 && index < LevelNames.Length)
            return LevelNames[index];

        return level.ToString().ToUpperInvariant();
    }

    private static string FormatProperties(IReadOnlyDictionary<string, string>? properties)
    {
        if (properties is null || properties.Count == 0)
            return string.Empty;

        var sb = new StringBuilder(64);
        sb.Append('{');
        var first = true;
        foreach (var kvp in properties)
        {
            if (!first)
                sb.Append(',');
            first = false;

            sb.Append('"');
            AppendJsonEscaped(sb, kvp.Key);
            sb.Append("\":\"");
            AppendJsonEscaped(sb, kvp.Value);
            sb.Append('"');
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static void AppendJsonEscaped(StringBuilder sb, string value)
    {
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c < '\u0020')
                    {
                        sb.Append("\\u");
                        sb.Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Flush();

        lock (_flushLock)
        {
            _disposed = true;
            _writer.Dispose();
        }
    }
}
