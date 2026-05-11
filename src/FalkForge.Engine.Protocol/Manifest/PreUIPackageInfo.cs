namespace FalkForge.Engine.Protocol.Manifest;

/// <summary>
/// Describes a prerequisite that the engine must detect and optionally install
/// before spawning the managed WPF UI process.
/// Pre-UI prerequisites run in the NativeAOT engine process — no managed runtime required.
/// </summary>
public sealed class PreUIPackageInfo
{
    /// <summary>Unique identifier for this prerequisite within the bundle.</summary>
    public required string Id { get; init; }

    /// <summary>Human-readable name shown in the TaskDialog progress UI (Phase 3).</summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Relative path within the extraction cache to the prerequisite executable.
    /// Empty when <see cref="PayloadMode"/> is <see cref="PreUIPayloadMode.Remote"/>.
    /// </summary>
    public required string SourcePath { get; init; }

    /// <summary>SHA-256 hex string of the prerequisite payload (upper-case, no hyphens).</summary>
    public required string Sha256Hash { get; init; }

    /// <summary>
    /// Command-line arguments passed to the prerequisite installer process.
    /// Must be non-empty — typical silent install flags (e.g., <c>/quiet /norestart</c>).
    /// </summary>
    public required string Arguments { get; init; }

    /// <summary>
    /// Conditions evaluated at runtime to determine whether this prerequisite is already installed.
    /// All conditions must evaluate to true for the prerequisite to be considered installed.
    /// At least one condition is required (enforced by BDL028).
    /// </summary>
    public IReadOnlyList<SearchCondition> SearchConditions { get; init; } = [];

    /// <summary>
    /// Download URL for <see cref="PreUIPayloadMode.Remote"/> payloads.
    /// Null when <see cref="PayloadMode"/> is <see cref="PreUIPayloadMode.Embedded"/>.
    /// </summary>
    public string? DownloadUrl { get; init; }

    /// <summary>Size in bytes of the remote payload. Used to show download progress.</summary>
    public long Size { get; init; }

    /// <summary>
    /// Governs the engine response when the prerequisite installer exits with 3010 or 1641.
    /// Defaults to <see cref="PreUIRebootBehavior.IgnoreAndContinue"/>.
    /// </summary>
    public PreUIRebootBehavior RebootBehavior { get; init; } = PreUIRebootBehavior.IgnoreAndContinue;

    /// <summary>
    /// Whether the payload is embedded in the bundle or downloaded at install time.
    /// Defaults to <see cref="PreUIPayloadMode.Embedded"/>.
    /// </summary>
    public PreUIPayloadMode PayloadMode { get; init; } = PreUIPayloadMode.Embedded;

    /// <summary>
    /// Per-exit-code behaviour overrides for this prerequisite.
    /// Null means standard exit code handling applies (0 = success, non-zero = failure, 3010/1641 = reboot).
    /// </summary>
    public IReadOnlyDictionary<int, ExitCodeBehavior>? ExitCodes { get; init; }
}
