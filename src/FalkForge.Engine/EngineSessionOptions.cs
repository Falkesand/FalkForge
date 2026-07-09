namespace FalkForge.Engine;

using FalkForge.Diagnostics;
using FalkForge.Engine.Logging;
using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Protocol.Transport;

/// <summary>
/// Optional configuration for <see cref="EngineSession.BindToPipe"/>.
/// All properties are optional; sensible defaults are used when omitted.
/// </summary>
public sealed record EngineSessionOptions
{
    /// <summary>
    /// Logger to use for the session. When <c>null</c> a default file-based
    /// <see cref="EngineLogger"/> is created at <see cref="EngineLogger.GetDefaultLogPath"/>.
    /// </summary>
    public IFalkLogger? Logger { get; init; }

    /// <summary>
    /// Directory to write log files to. Overrides the default temp-path strategy
    /// when <see cref="Logger"/> is also <c>null</c>.
    /// </summary>
    public string? LogDirectory { get; init; }

    /// <summary>
    /// Explicit log file path. When non-null and <see cref="Logger"/> is null, the
    /// session creates an <see cref="EngineLogger"/> at this path instead of using
    /// the default temp-path strategy or <see cref="LogDirectory"/>. Honoured by
    /// both <see cref="EngineSession.BindToPipe"/> and <see cref="EngineSession.BindToChannel"/>.
    /// </summary>
    public string? LogPath { get; init; }

    /// <summary>
    /// Minimum log level for the session-owned logger. When non-null and <see cref="Logger"/>
    /// is null, the freshly constructed logger has its <see cref="IFalkLogger.MinimumLevel"/>
    /// set to this value before any log call. When <see cref="Logger"/> is supplied, this
    /// value is applied to it as well so that the runtime override on the command-line
    /// overrides any default the host pre-configured.
    /// </summary>
    public LogLevel? MinimumLogLevel { get; init; }

    /// <summary>
    /// Named-pipe connection options (timeout, message size, security callback).
    /// Applied on top of the resolved <paramref name="pipeName"/> / <paramref name="sharedSecret"/>
    /// arguments in <see cref="EngineSession.BindToPipe"/>.
    /// </summary>
    public PipeConnectionOptions? PipeOptions { get; init; }

    /// <summary>
    /// Timeout for the initial HMAC handshake with the UI process.
    /// Defaults to 60 seconds when <c>null</c>.
    /// </summary>
    public TimeSpan? HandshakeTimeout { get; init; }

    /// <summary>
    /// When <c>true</c> (default), a <see cref="FileSystemJournalStore"/> is created
    /// and wired into the pipeline to support rollback.
    /// </summary>
    public bool WriteJournal { get; init; } = true;

    /// <summary>
    /// Optional clock used to stamp the log filename when <see cref="LogDirectory"/> is
    /// provided (the branch that builds <c>install_{timestamp}.log</c> inline).
    /// Also forwarded to <see cref="EngineLogger.GetDefaultLogPath"/> when neither
    /// <see cref="LogPath"/> nor <see cref="LogDirectory"/> is set.
    /// When <c>null</c>, real wall-clock time is used (production default).
    /// Primarily for test isolation — inject a <see cref="FalkForge.Testing.FakeClock"/>
    /// to make log-path assertions deterministic.
    /// </summary>
    public ISystemClock? Clock { get; init; }

    // ──────────────────────────────────────────────────────────────────────────
    // Plan-only mode options
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// When <c>true</c>, the session runs only through detection and planning,
    /// then exports the plan and exits without invoking Apply. Intended for
    /// <c>forge plan</c> / headless CI use cases.
    /// </summary>
    public bool IsPlanOnly { get; init; }

    /// <summary>
    /// Optional path for plan JSON output when <see cref="IsPlanOnly"/> is <c>true</c>.
    /// When <c>null</c>, the plan JSON is written to stdout. Ignored when
    /// <see cref="IsPlanOnly"/> is <c>false</c>.
    /// </summary>
    public string? PlanOnlyOutputPath { get; init; }

    /// <summary>
    /// When <c>true</c> (set by the engine on the require-signed update path), the pipeline advances the
    /// anti-downgrade/revocation trust store after a successful apply (C16), forwarding the manifest
    /// signature's epoch + revocations to the elevated companion. A fresh install leaves this <c>false</c>,
    /// so the store is never advanced by a first-time install.
    /// </summary>
    public bool AdvanceTrustStoreOnVerifiedApply { get; init; }

    // ──────────────────────────────────────────────────────────────────────────
    // Test-only injection point (exposed via EngineSession.BindToChannel)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Test-only: pre-built UI channel. When non-null, <see cref="EngineSession.BindToPipe"/>
    /// skips named-pipe setup and uses this channel directly.
    /// Consumed by <see cref="EngineSession.BindToChannel"/>.
    /// </summary>
    internal IUiChannel? Channel { get; init; }

    /// <summary>
    /// Test-only: installer manifest to seed into the pipeline when using
    /// <see cref="EngineSession.BindToChannel"/>. Enables plan-only integration tests
    /// without requiring a named-pipe or a real manifest file on disk.
    /// Ignored by <see cref="EngineSession.BindToPipe"/> which always loads the manifest
    /// from the file at <c>manifestPath</c>.
    /// </summary>
    internal FalkForge.Engine.Protocol.Manifest.InstallerManifest? SeedManifest { get; init; }
}
