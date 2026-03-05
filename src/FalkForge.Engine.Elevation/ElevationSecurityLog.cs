namespace FalkForge.Engine.Elevation;

using System.Globalization;

/// <summary>
/// Simple file-based security logger for the elevated process.
/// Writes security events to %TEMP%\FalkForge\elevation_{timestamp}_{pid}.log.
/// Thread-safe via lock. NativeAOT compatible (no reflection, no globalization).
/// </summary>
internal static class ElevationSecurityLog
{
    private static readonly object Lock = new();
    private static StreamWriter? _writer;
    private static bool _initialized;

    /// <summary>
    /// Initializes the log file. Safe to call multiple times; only the first call takes effect.
    /// </summary>
    internal static void Initialize()
    {
        lock (Lock)
        {
            if (_initialized)
                return;

            _initialized = true;

            try
            {
                var directory = Path.Combine(Path.GetTempPath(), "FalkForge");
                Directory.CreateDirectory(directory);

                var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                var pid = Environment.ProcessId;
                var filePath = Path.Combine(directory, $"elevation_{timestamp}_{pid}.log");

                var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
                _writer = new StreamWriter(fileStream, System.Text.Encoding.UTF8)
                {
                    AutoFlush = true
                };

                WriteEntryUnsafe("INFO", "ElevationSecurityLog", "Security log initialized");
            }
            catch
            {
                // If we cannot create the log file, degrade gracefully.
                // The elevated process must not crash due to logging failures.
                _writer = null;
            }
        }
    }

    /// <summary>
    /// Logs a security event at WARNING level.
    /// </summary>
    internal static void SecurityEvent(string category, string message)
    {
        WriteEntry("WARNING", category, message);
    }

    /// <summary>
    /// Logs an error event.
    /// </summary>
    internal static void Error(string category, string message)
    {
        WriteEntry("ERROR", category, message);
    }

    /// <summary>
    /// Logs an informational event.
    /// </summary>
    internal static void Info(string category, string message)
    {
        WriteEntry("INFO", category, message);
    }

    private static void WriteEntry(string level, string category, string message)
    {
        lock (Lock)
        {
            if (_writer is null)
                return;

            WriteEntryUnsafe(level, category, message);
        }
    }

    /// <summary>
    /// Writes an entry without acquiring the lock. Caller must hold <see cref="Lock"/>.
    /// </summary>
    private static void WriteEntryUnsafe(string level, string category, string message)
    {
        var timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        _writer!.Write(timestamp);
        _writer.Write('\t');
        _writer.Write(level);
        _writer.Write('\t');
        _writer.Write(category);
        _writer.Write('\t');
        _writer.WriteLine(message);
    }

    /// <summary>
    /// Flushes and closes the log file.
    /// </summary>
    internal static void Shutdown()
    {
        lock (Lock)
        {
            if (_writer is null)
                return;

            try
            {
                _writer.Flush();
                _writer.Dispose();
            }
            catch
            {
                // Best-effort cleanup
            }

            _writer = null;
        }
    }
}
