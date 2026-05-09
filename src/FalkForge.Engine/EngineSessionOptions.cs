namespace FalkForge.Engine;

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
    public IEngineLogger? Logger { get; init; }

    /// <summary>
    /// Directory to write log files to. Overrides the default temp-path strategy
    /// when <see cref="Logger"/> is also <c>null</c>.
    /// </summary>
    public string? LogDirectory { get; init; }

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

    // ──────────────────────────────────────────────────────────────────────────
    // Test-only injection point (exposed via EngineSession.BindToChannel)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Test-only: pre-built UI channel. When non-null, <see cref="EngineSession.BindToPipe"/>
    /// skips named-pipe setup and uses this channel directly.
    /// Consumed by <see cref="EngineSession.BindToChannel"/>.
    /// </summary>
    internal IUiChannel? Channel { get; init; }
}
