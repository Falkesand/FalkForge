using FalkForge.Engine.Protocol.Manifest;

namespace FalkForge.Compiler.Bundle.Models;

/// <summary>
/// Compiler-side model for a pre-UI prerequisite package.
/// Produced by <see cref="FalkForge.Compiler.Bundle.Builders.PreUIPackageBuilder"/>
/// and consumed by <see cref="FalkForge.Compiler.Bundle.Compilation.ManifestGenerator"/>
/// and <see cref="FalkForge.Compiler.Bundle.Compilation.PayloadEmbedder"/>.
/// </summary>
public sealed record PreUIPackageModel
{
    /// <summary>Unique identifier within the bundle. Used as the filename in the preui cache subdir.</summary>
    public required string Id { get; init; }

    /// <summary>Human-readable name shown in the native bootstrap TaskDialog (Phase 3).</summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Absolute or relative path to the prerequisite installer file on the build machine.
    /// Empty string when <see cref="PayloadMode"/> is <see cref="PreUIPayloadMode.Remote"/>.
    /// </summary>
    public required string SourcePath { get; init; }

    /// <summary>Command-line arguments passed to the installer process (e.g., <c>/quiet /norestart</c>).</summary>
    public required string Arguments { get; init; }

    /// <summary>
    /// Governs the engine response to exit code 3010 (reboot required).
    /// Defaults to <see cref="PreUIRebootBehavior.IgnoreAndContinue"/>.
    /// </summary>
    public PreUIRebootBehavior RebootBehavior { get; init; } = PreUIRebootBehavior.IgnoreAndContinue;

    /// <summary>
    /// Whether the payload is embedded in the bundle TOC or downloaded at install time.
    /// Defaults to <see cref="PreUIPayloadMode.Embedded"/>.
    /// </summary>
    public PreUIPayloadMode PayloadMode { get; init; } = PreUIPayloadMode.Embedded;

    /// <summary>
    /// Remote payload metadata. Non-null when <see cref="PayloadMode"/> is <see cref="PreUIPayloadMode.Remote"/>.
    /// Null when payload is embedded.
    /// </summary>
    public PreUIRemotePayload? RemotePayload { get; init; }

    /// <summary>
    /// Search conditions evaluated at runtime to determine if this prerequisite is already installed.
    /// At least one condition is required (enforced by BDL028).
    /// </summary>
    public IReadOnlyList<SearchCondition> SearchConditions { get; init; } = [];

    /// <summary>
    /// Per-exit-code behaviour overrides. Null means standard handling applies.
    /// </summary>
    public IReadOnlyDictionary<int, ExitCodeBehavior>? ExitCodes { get; init; }
}
