namespace FalkForge.Engine.Logging;

using System.Diagnostics.Metrics;
using System.Globalization;
using FalkForge.Diagnostics;
using FalkForge.Engine.Protocol;

/// <summary>
/// Central metrics façade for the FalkForge engine process.
/// Uses <see cref="System.Diagnostics.Metrics"/> which is NativeAOT/trim-safe when
/// instruments are called directly (no reflection-based collectors are instantiated here).
///
/// Metric naming follows the OpenTelemetry semantic conventions where applicable
/// and uses the <c>falkforge.engine.*</c> prefix for domain-specific instruments.
///
/// <para>
/// <strong>Snapshot bridge</strong> — <see cref="Snapshot"/> reads in-process counters
/// accumulated during the session and <see cref="FlushToLogger"/> writes them as a
/// single structured log entry (category <c>"Metrics"</c>) so counter values are
/// visible in the log file even when no OTel collector is present.
/// </para>
/// </summary>
public static class EngineMeter
{
    // ──────────────────────────────────────────────────────────────────────────
    // Meter identity
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>The meter name used to identify all FalkForge engine instruments.</summary>
    public const string MeterName = "falkforge.engine";

    // ──────────────────────────────────────────────────────────────────────────
    // Instrument name constants (public so tests can reference them without magic strings)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>Counter — one unit per phase transition. Tag: <c>phase</c>.</summary>
    public const string PhaseTransitionsCounter = "falkforge.engine.phase.transitions";

    /// <summary>Histogram — elapsed milliseconds per phase. Tag: <c>phase</c>.</summary>
    public const string PhaseDurationHistogram = "falkforge.engine.phase.duration_ms";

    /// <summary>Counter — one unit per completed download attempt. Tag: <c>result</c> (success|failure).</summary>
    public const string PayloadDownloadsCounter = "falkforge.engine.payload.downloads";

    /// <summary>Histogram — size in bytes of a downloaded payload. Tag: <c>kind</c> (msi|msp|msu|exe|…).</summary>
    public const string PayloadSizeHistogram = "falkforge.engine.payload.size_bytes";

    /// <summary>Counter — one unit per retry. Tag: <c>operation</c> (download|msi-install|…).</summary>
    public const string RetryCounter = "falkforge.engine.retry.count";

    /// <summary>Counter — one unit per terminal pipeline error. Tag: <c>error_kind</c>.</summary>
    public const string ErrorCounter = "falkforge.engine.error.count";

    // ──────────────────────────────────────────────────────────────────────────
    // Snapshot key constants (used as property keys in FlushToLogger entries)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// String keys used in the <see cref="Snapshot"/> dictionary and in the structured
    /// log entry written by <see cref="FlushToLogger"/>.  Public so tests can reference
    /// them without magic strings.
    /// </summary>
    public static class SnapshotKey
    {
        /// <summary>Total number of phase transitions recorded this session.</summary>
        public const string PhaseTransitions = "phase_transitions";

        /// <summary>Number of successful payload downloads this session.</summary>
        public const string PayloadDownloadsSuccess = "payload_downloads_success";

        /// <summary>Number of failed payload downloads this session.</summary>
        public const string PayloadDownloadsFailure = "payload_downloads_failure";

        /// <summary>Total number of retries across all operations this session.</summary>
        public const string Retries = "retries";

        /// <summary>Number of terminal pipeline errors this session.</summary>
        public const string Errors = "errors";
    }

    // ──────────────────────────────────────────────────────────────────────────
    // In-process snapshot counters
    // WHY: System.Diagnostics.Metrics instruments push values to listeners but do
    // not expose a readable accumulated total. We maintain separate long fields
    // using Interlocked so that Snapshot() can read them without a lock and without
    // reflection — both requirements for NativeAOT compatibility.
    // ──────────────────────────────────────────────────────────────────────────

    private static long _snapPhaseTransitions;
    private static long _snapPayloadDownloadsSuccess;
    private static long _snapPayloadDownloadsFailure;
    private static long _snapRetries;
    private static long _snapErrors;

    // ──────────────────────────────────────────────────────────────────────────
    // Tag-value enums (bounded cardinality — prevents metric cardinality explosion)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Installer payload kinds used as the <c>kind</c> tag value in
    /// <see cref="RecordPayloadDownload"/>.
    /// </summary>
    public enum PayloadKind { Msi, Msp, Msu, Exe, Bundle }

    /// <summary>
    /// Retryable operations used as the <c>operation</c> tag value in
    /// <see cref="RecordRetry"/>.
    /// </summary>
    public enum RetryOperation { Download, MsiInstall, MspInstall, ExeRun }

    // ──────────────────────────────────────────────────────────────────────────
    // Instruments (lazy-initialised once; static field = single allocation)
    // ──────────────────────────────────────────────────────────────────────────

    // NOTE: Meter and instrument instances are module-level singletons.
    // The CLR guarantees that static field initialisers run once (type initialiser),
    // which is thread-safe without explicit locking and AOT-safe (no reflection).
    private static readonly Meter _meter = new(MeterName, version: "1.0");

    private static readonly Counter<long> _phaseTransitions =
        _meter.CreateCounter<long>(
            PhaseTransitionsCounter,
            unit: "{transition}",
            description: "Number of engine phase transitions");

    private static readonly Histogram<double> _phaseDuration =
        _meter.CreateHistogram<double>(
            PhaseDurationHistogram,
            unit: "ms",
            description: "Duration of each engine phase in milliseconds");

    private static readonly Counter<long> _payloadDownloads =
        _meter.CreateCounter<long>(
            PayloadDownloadsCounter,
            unit: "{download}",
            description: "Number of payload download attempts (tagged by result)");

    private static readonly Histogram<long> _payloadSize =
        _meter.CreateHistogram<long>(
            PayloadSizeHistogram,
            unit: "By",
            description: "Size in bytes of a downloaded installer payload");

    private static readonly Counter<long> _retries =
        _meter.CreateCounter<long>(
            RetryCounter,
            unit: "{retry}",
            description: "Number of operation retries (tagged by operation name)");

    private static readonly Counter<long> _errors =
        _meter.CreateCounter<long>(
            ErrorCounter,
            unit: "{error}",
            description: "Engine pipeline errors by ErrorKind");

    // ──────────────────────────────────────────────────────────────────────────
    // Recording API
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Records one phase transition and its elapsed duration.
    /// Call this when entering a new engine phase.
    /// </summary>
    /// <param name="phase">The engine phase that was entered.</param>
    /// <param name="elapsedMs">Time in milliseconds since the previous phase began.</param>
    public static void RecordPhaseTransition(EnginePhase phase, double elapsedMs)
    {
        // Stack-allocate the tag pair to avoid heap allocation on every call.
        // ToString() on the enum produces the declared member name (e.g. "Detecting"),
        // which matches the canonical tag values used in telemetry dashboards.
        var tag = new KeyValuePair<string, object?>("phase", phase.ToString());
        _phaseTransitions.Add(1, tag);
        _phaseDuration.Record(elapsedMs, tag);
        Interlocked.Increment(ref _snapPhaseTransitions);
    }

    /// <summary>
    /// Records completion of a payload download.
    /// </summary>
    /// <param name="success">Whether the download completed successfully.</param>
    /// <param name="sizeBytes">
    /// Bytes received. Pass 0 on failure (or when the size is unknown).
    /// <para>
    /// <strong>Divergence from symmetric counter design:</strong> the
    /// <c>falkforge.engine.payload.size_bytes</c> histogram is recorded only when
    /// <paramref name="success"/> is <see langword="true"/> AND
    /// <paramref name="sizeBytes"/> is greater than zero. Failed downloads are
    /// counted via the <c>falkforge.engine.payload.downloads</c> counter with
    /// <c>result=failure</c> but do not contribute a size observation, because
    /// a partial or unknown byte count would skew distribution statistics.
    /// </para>
    /// </param>
    /// <param name="kind">Package kind — bounds the cardinality of the <c>kind</c> tag.</param>
    public static void RecordPayloadDownload(bool success, long sizeBytes, PayloadKind kind)
    {
        var resultTag = new KeyValuePair<string, object?>("result", success ? "success" : "failure");
        _payloadDownloads.Add(1, resultTag);

        if (success)
        {
            Interlocked.Increment(ref _snapPayloadDownloadsSuccess);
            if (sizeBytes > 0)
            {
                // Lowercase tag value matches OpenTelemetry semantic convention for package kind.
                var kindTag = new KeyValuePair<string, object?>("kind", PayloadKindToTag(kind));
                _payloadSize.Record(sizeBytes, kindTag);
            }
        }
        else
        {
            Interlocked.Increment(ref _snapPayloadDownloadsFailure);
        }
    }

    /// <summary>
    /// Records one retry event for the given operation.
    /// </summary>
    /// <param name="operation">Retryable operation — bounds the cardinality of the <c>operation</c> tag.</param>
    public static void RecordRetry(RetryOperation operation)
    {
        _retries.Add(1, new KeyValuePair<string, object?>("operation", RetryOperationToTag(operation)));
        Interlocked.Increment(ref _snapRetries);
    }

    /// <summary>
    /// Records one terminal pipeline error. Call this once per pipeline run when the
    /// run ends in a terminal failure (do NOT record at every intermediate Result conversion).
    /// </summary>
    /// <param name="kind">The <see cref="ErrorKind"/> of the terminal failure.</param>
    public static void RecordError(ErrorKind kind)
    {
        // Stack-allocate the tag pair to avoid heap allocation on every call.
        // ToString() on the enum produces the declared member name (e.g. "DownloadError"),
        // which matches the canonical tag values used in telemetry dashboards.
        _errors.Add(1, new KeyValuePair<string, object?>("error_kind", kind.ToString()));
        Interlocked.Increment(ref _snapErrors);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Snapshot + logger bridge
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a point-in-time snapshot of all in-process counters accumulated
    /// since construction (or the last <see cref="ResetForTesting"/> call).
    /// Keys are the <see cref="SnapshotKey"/> constants.
    ///
    /// <para>Thread-safe: each field is read with <see cref="Interlocked.Read"/>.</para>
    /// </summary>
    public static IReadOnlyDictionary<string, long> Snapshot() =>
        new Dictionary<string, long>(5)
        {
            [SnapshotKey.PhaseTransitions]        = Interlocked.Read(ref _snapPhaseTransitions),
            [SnapshotKey.PayloadDownloadsSuccess]  = Interlocked.Read(ref _snapPayloadDownloadsSuccess),
            [SnapshotKey.PayloadDownloadsFailure]  = Interlocked.Read(ref _snapPayloadDownloadsFailure),
            [SnapshotKey.Retries]                  = Interlocked.Read(ref _snapRetries),
            [SnapshotKey.Errors]                   = Interlocked.Read(ref _snapErrors),
        };

    /// <summary>
    /// Writes the current counter snapshot as a single structured log entry with
    /// category <c>"Metrics"</c> and <see cref="LogLevel.Info"/> level.
    /// All counter values are included as string-valued properties so log parsers
    /// can extract them without scanning individual lines.
    ///
    /// <para>
    /// Called by <see cref="EngineSession.DisposeAsync"/> so every session ends
    /// with a metrics summary in the log file regardless of whether an OTel
    /// collector is configured.
    /// </para>
    /// </summary>
    /// <param name="logger">Logger to write to. Must not be null.</param>
    public static void FlushToLogger(IFalkLogger logger)
    {
        var snap = Snapshot();

        // Build properties as string→string so they serialise correctly through
        // the existing IReadOnlyDictionary<string,string> LogEntry convention.
        var properties = new Dictionary<string, string>(snap.Count);
        foreach (var kvp in snap)
            properties[kvp.Key] = kvp.Value.ToString(CultureInfo.InvariantCulture);

        logger.Log(LogLevel.Info, category: "Metrics", message: "Session metrics snapshot", properties);
    }

    /// <summary>
    /// Resets all in-process snapshot counters to zero.
    /// <strong>Test use only</strong> — allows tests to start from a clean baseline
    /// without cross-contamination from prior test runs in the same process.
    /// </summary>
    internal static void ResetForTesting()
    {
        Interlocked.Exchange(ref _snapPhaseTransitions, 0L);
        Interlocked.Exchange(ref _snapPayloadDownloadsSuccess, 0L);
        Interlocked.Exchange(ref _snapPayloadDownloadsFailure, 0L);
        Interlocked.Exchange(ref _snapRetries, 0L);
        Interlocked.Exchange(ref _snapErrors, 0L);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Tag-value helpers (canonical lowercase strings for telemetry dashboards)
    // ──────────────────────────────────────────────────────────────────────────

    private static string PayloadKindToTag(PayloadKind kind) => kind switch
    {
        PayloadKind.Msi    => "msi",
        PayloadKind.Msp    => "msp",
        PayloadKind.Msu    => "msu",
        PayloadKind.Exe    => "exe",
        PayloadKind.Bundle => "bundle",
        _                  => kind.ToString().ToLowerInvariant()
    };

    private static string RetryOperationToTag(RetryOperation operation) => operation switch
    {
        RetryOperation.Download   => "download",
        RetryOperation.MsiInstall => "msi-install",
        RetryOperation.MspInstall => "msp-install",
        RetryOperation.ExeRun     => "exe-run",
        _                         => operation.ToString().ToLowerInvariant()
    };
}
