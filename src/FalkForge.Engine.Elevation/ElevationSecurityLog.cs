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
    // WHY: volatile so that reads in WriteEntryUnsafe always see the latest value set
    // by SetCorrelationId without acquiring a lock on every log call.
    private static volatile string _correlationId = string.Empty;

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
    /// Sets the session correlation id that will be written as a fifth tab-separated
    /// column on every subsequent log entry. Call once after receiving a
    /// <c>SessionStartMessage</c> from the engine. Thread-safe; the write is
    /// atomic because <see cref="string"/> assignment is reference-sized.
    /// </summary>
    /// <remarks>
    /// Security: <paramref name="id"/> is a <see cref="Guid"/> — already strongly typed,
    /// safe by construction. Formatted as "D" (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx).
    /// </remarks>
    internal static void SetCorrelationId(Guid id)
    {
        // WHY: Guid.Empty maps to empty string so log consumers can parse lines uniformly
        // (5 fields always present; empty 5th field = no id set).
        _correlationId = id == Guid.Empty ? string.Empty : id.ToString("D");
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
    /// Log format: <c>timestamp\tlevel\tcategory\tmessage\tcorrelationId</c>.
    /// The fifth column (correlationId) is empty when no id has been set via
    /// <see cref="SetCorrelationId"/>. This is a BREAKING change from the prior
    /// 4-column format; log consumers must handle 5 tab-separated fields.
    /// </summary>
    private static void WriteEntryUnsafe(string level, string category, string message)
    {
        // Snapshot the volatile field once per entry to avoid a TOCTOU race if
        // SetCorrelationId is called concurrently (the snapshot is reference-atomic).
        var correlationId = _correlationId;

        var timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        _writer!.Write(timestamp);
        _writer.Write('\t');
        _writer.Write(level);
        _writer.Write('\t');
        _writer.Write(category);
        _writer.Write('\t');
        _writer.Write(message);
        _writer.Write('\t');
        _writer.WriteLine(correlationId);
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
