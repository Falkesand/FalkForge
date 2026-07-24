namespace FalkForge.Engine.Logging;

// Infrastructure shipped in Phase 3.3; production wiring (EngineSession using non-default
// rotation options) is introduced in a follow-up phase (Phase 3.4).
// EngineSession currently constructs EngineLogger with default options (rotation disabled),
// which is intentional — wiring is kept separate to make this change reviewable in isolation.
// See project memory: docs/plans/ and CLAUDE.md project-memory entries.

/// <summary>
/// Configuration options for <see cref="EngineLogger"/>.
/// All properties have sensible defaults so callers that do not need rotation
/// can pass <c>default</c> or omit the parameter entirely.
/// </summary>
public sealed record EngineLoggerOptions
{
    /// <summary>
    /// Rotate the active log file when it grows beyond this many bytes.
    /// Set to <see cref="long.MaxValue"/> (the default) to disable size-based rotation.
    /// </summary>
    public long RotationSizeThresholdBytes { get; init; } = long.MaxValue;

    /// <summary>
    /// Maximum number of rotated backup files to keep (e.g. <c>install.log.1</c>,
    /// <c>install.log.2</c>, …).  When a new rotation would exceed this count the
    /// oldest backup is deleted.  Must be at least 1.
    /// Default: 5.
    /// </summary>
    public int RetentionCount { get; init; } = 5;

    /// <summary>
    /// Deny-list of secret-indicating key tokens (case-insensitive substring match against a
    /// property key) applied by <see cref="LogRedactor"/> before a log entry is built, so
    /// secret-valued properties (passwords, tokens, connection strings, …) never reach the log
    /// file or the pipe-callback sink. Defaults to <see cref="LogRedactor.DefaultSecretKeyTokens"/>.
    /// Pass an empty list to disable redaction entirely.
    /// </summary>
    public IReadOnlyList<string> RedactionKeyTokens { get; init; } = LogRedactor.DefaultSecretKeyTokens;

    /// <summary>
    /// Default options: no size rotation, keep 5 backups, default redaction deny-list.
    /// </summary>
    public static EngineLoggerOptions Default { get; } = new();
}
