namespace FalkForge.Engine.Logging;

using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using FalkForge.Engine.Protocol;

/// <summary>
/// Production structured logger that writes tab-separated entries to a log file.
/// Thread-safe via a concurrent queue and dedicated flush mechanism.
/// Optionally invokes a callback for each entry (e.g., to forward to the UI pipe).
///
/// <para>
/// <strong>Log rotation</strong> — when the active log file grows beyond
/// <see cref="EngineLoggerOptions.RotationSizeThresholdBytes"/>, the logger renames
/// the existing file to <c>&lt;name&gt;.1</c>, shifts older backups up by one
/// (<c>.1</c>→<c>.2</c>, …), and deletes any backup beyond
/// <see cref="EngineLoggerOptions.RetentionCount"/>.  A new active file is then
/// opened.  All rotation paths are verified to remain inside the log directory
/// (defense-in-depth, mirroring <c>CacheLayout</c>'s containment check).
/// </para>
///
/// <para>
/// <strong>Thread safety</strong> — writes, flushes, and rotation all execute inside
/// <c>_flushLock</c>, so no rotation can race an active write.
/// </para>
/// </summary>
public sealed class EngineLogger : IEngineLogger
{
    private const int FlushThreshold = 32;
    private static readonly string[] LevelNames = ["VERBOSE", "DEBUG", "INFO", "WARNING", "ERROR"];

    private readonly ConcurrentQueue<LogEntry> _queue = new();
    // _writer is replaced under _flushLock during rotation; _currentFilePath never changes.
    private StreamWriter _writer;
    private readonly string _currentFilePath;
    private readonly Action<LogEntry>? _pipeCallback;
    private readonly EngineLoggerOptions _options;
    private readonly object _flushLock = new();
    // WHY: Track bytes written ourselves rather than calling BaseStream.Length/Position,
    // which can behave unexpectedly on write-only FileStream handles on some platforms.
    // Reset to 0 after each rotation so the threshold resets for the new file.
    private long _bytesWrittenSinceRotation;
    private volatile bool _disposed;

    /// <summary>
    /// Initialises a new <see cref="EngineLogger"/>.
    /// </summary>
    /// <param name="filePath">Absolute path of the active log file.</param>
    /// <param name="pipeCallback">
    ///     Optional callback invoked (before writing) for each accepted entry.
    ///     Used to forward log entries to the UI pipe.
    /// </param>
    /// <param name="options">
    ///     Rotation and retention options. Defaults to
    ///     <see cref="EngineLoggerOptions.Default"/> (no size rotation).
    /// </param>
    public EngineLogger(string filePath, Action<LogEntry>? pipeCallback = null, EngineLoggerOptions? options = null)
    {
        _currentFilePath = Path.GetFullPath(filePath);
        _options = options ?? EngineLoggerOptions.Default;
        _pipeCallback = pipeCallback;

        var directory = Path.GetDirectoryName(_currentFilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _writer = OpenWriter(_currentFilePath);
    }

    public LogLevel MinimumLevel { get; set; } = LogLevel.Info;

    /// <inheritdoc/>
    public Guid SessionCorrelationId { get; set; }

    public void Log(LogLevel level, string category, string message, IReadOnlyDictionary<string, string>? properties = null)
    {
        if (_disposed)
            return;

        if (level < MinimumLevel)
            return;

        var entry = new LogEntry(DateTimeOffset.UtcNow, level, category, message, properties, SessionCorrelationId);

        _queue.Enqueue(entry);
        _pipeCallback?.Invoke(entry);

        // Flush immediately on Error, or when queue exceeds threshold
        if (level >= LogLevel.Error || _queue.Count >= FlushThreshold)
            Flush();
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
                WriteEntry(entry);

            _writer.Flush();

            // Check rotation threshold after every flush (cheap: just a stream position query).
            if (_options.RotationSizeThresholdBytes < long.MaxValue)
                TryRotate();
        }
    }

    // Called under _flushLock (after _writer.Flush() has already been called).
    private void TryRotate()
    {
        if (_bytesWrittenSinceRotation < _options.RotationSizeThresholdBytes)
            return;

        RotateNow();
    }

    // Rotation algorithm (called under _flushLock):
    // 1. Close and flush the current writer.
    // 2. Shift existing backups: .{N} → .{N+1}, …, .1 → .2.
    //    If shifting would push a file beyond RetentionCount, delete it first.
    // 3. Rename the active log file to .1.
    // 4. Open a fresh writer at the original path.
    // All computed paths are verified to remain inside the log directory before
    // any file-system mutation (path containment / traversal defense).
    private void RotateNow()
    {
        // Flush and close current writer before any rename attempt.
        _writer.Flush();
        _writer.Dispose();

        var logDir = Path.GetDirectoryName(_currentFilePath)!;
        var canonicalLogDir = Path.GetFullPath(logDir);

        // Pre-delete any file at the overflow slot (e.g. left over from a prior crash).
        // WHY: Without this, a stale .{RetentionCount+1} crash remnant survives indefinitely
        // because the shift loop never writes to that slot (it evicts .{RetentionCount} instead).
        var overflow = BuildBackupPath(_currentFilePath, _options.RetentionCount + 1);
        if (IsInsideDirectory(overflow, canonicalLogDir))
            TryDelete(overflow);

        // Shift existing backups from highest to lowest index.
        // When i == RetentionCount, the destination would exceed the cap — delete the source
        // instead of shifting it forward (evict the oldest backup).
        // For i < RetentionCount, rename src → dst to make room for the next backup.
        for (var i = _options.RetentionCount; i >= 1; i--)
        {
            var src = BuildBackupPath(_currentFilePath, i);
            var dst = BuildBackupPath(_currentFilePath, i + 1);

            // SECURITY: verify both paths are inside the log directory before touching them.
            if (!IsInsideDirectory(src, canonicalLogDir) || !IsInsideDirectory(dst, canonicalLogDir))
                continue; // skip malformed path — never operate outside the log root

            if (!File.Exists(src))
                continue;

            if (i == _options.RetentionCount)
            {
                // Destination (.{RetentionCount+1}) would exceed retention cap — delete instead.
                TryDelete(src);
            }
            else
            {
                // overwrite:true avoids a separate delete + move race on Windows (NTFS atomic).
                TryMove(src, dst);
            }
        }

        // Rename the active file to .1.
        var backup1 = BuildBackupPath(_currentFilePath, 1);
        if (IsInsideDirectory(backup1, canonicalLogDir))
        {
            // overwrite:true — atomically replaces any pre-existing .1 (no TOCTOU gap).
            TryMove(_currentFilePath, backup1);
        }

        // Open a fresh writer at the same path and reset the byte counter.
        _writer = OpenWriter(_currentFilePath);
        _bytesWrittenSinceRotation = 0;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Path helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static string BuildBackupPath(string basePath, int index) =>
        // WHY: Path.GetFullPath normalises separators and ".." components before the
        // containment check.  The index is always an int so no injection here, but
        // belt-and-suspenders normalisation matches CacheLayout's defense pattern.
        Path.GetFullPath($"{basePath}.{index}");

    private static bool IsInsideDirectory(string candidatePath, string canonicalDir)
    {
        // Normalise to canonical form so symlinks, redundant separators, and ".." are resolved.
        var canonical = Path.GetFullPath(candidatePath);

        // Ensure the directory portion ends with a separator so that a prefix check cannot
        // be fooled by a sibling directory whose name starts with the same prefix
        // (e.g. /logs2 must not match /logs).
        var dirWithSep = canonicalDir.EndsWith(Path.DirectorySeparatorChar)
            ? canonicalDir
            : canonicalDir + Path.DirectorySeparatorChar;

        return canonical.StartsWith(dirWithSep, StringComparison.OrdinalIgnoreCase)
            || string.Equals(canonical, canonicalDir, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { /* best effort */ }
        catch (UnauthorizedAccessException) { /* best effort */ }
    }

    private static void TryMove(string src, string dst)
    {
        // overwrite:true avoids a separate delete-then-move race; File.Move is atomic on NTFS.
        try { File.Move(src, dst, overwrite: true); }
        catch (IOException) { /* best effort — rotation is non-critical */ }
        catch (UnauthorizedAccessException) { /* best effort — rotation is non-critical */ }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Writer factory
    // ──────────────────────────────────────────────────────────────────────────

    private static StreamWriter OpenWriter(string path)
    {
        // SECURITY: FileShare.Read — prevents concurrent writes from other processes.
        // Log directory under %TEMP% inherits default ACLs (current user + SYSTEM +
        // Administrators). Per-session GUID subdirectories (see GetDefaultLogPath) reduce
        // path predictability. Full ACL restriction via DirectorySecurity is not used
        // because the System.IO.FileSystem.AccessControl APIs are not NativeAOT-compatible.
        var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        return new StreamWriter(fileStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = false
        };
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Write helpers
    // ──────────────────────────────────────────────────────────────────────────

    private void WriteEntry(LogEntry entry)
    {
        var timestamp = entry.Timestamp.ToString("o", CultureInfo.InvariantCulture);
        var levelName = GetLevelName(entry.Level);
        var propertiesJson = FormatProperties(entry.Properties);
        var correlationId = entry.SessionCorrelationId == Guid.Empty
            ? string.Empty
            : entry.SessionCorrelationId.ToString("D");

        _writer.Write(timestamp);
        _writer.Write('\t');
        _writer.Write(levelName);
        _writer.Write('\t');
        _writer.Write(entry.Category);
        _writer.Write('\t');
        _writer.Write(entry.Message);
        _writer.Write('\t');
        _writer.Write(propertiesJson);
        _writer.Write('\t');
        // Correlation id: standard "D" format (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx).
        // Written as empty string when Guid.Empty to preserve backward-compat with existing
        // log parsers that may not know about this column.
        _writer.Write(correlationId);
        _writer.WriteLine();

        // Accumulate byte estimate for rotation threshold check.
        // WHY: We count chars here (UTF-8 multi-byte handled conservatively via
        // Encoding.UTF8.GetByteCount on each field would be expensive; using char count
        // slightly underestimates for non-ASCII content but is safe — rotation may
        // fire a little late, never too early).  We reset after each rotation.
        _bytesWrittenSinceRotation +=
            timestamp.Length + 1 +
            levelName.Length + 1 +
            entry.Category.Length + 1 +
            entry.Message.Length + 1 +
            propertiesJson.Length + 1 +
            correlationId.Length + 1; // +1 for each tab/newline separator
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
                    if (c < ' ')
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

    // ──────────────────────────────────────────────────────────────────────────
    // Disposal
    // ──────────────────────────────────────────────────────────────────────────

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
