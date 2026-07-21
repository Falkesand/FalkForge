namespace FalkForge.Engine.Planning;

using System.Text.Json.Serialization;
using FalkForge.Engine.Protocol.Manifest;

public sealed class PlanAction
{
    public required string PackageId { get; init; }
    public required PlanActionType ActionType { get; init; }
    public required PackageInfo Package { get; init; }

    /// <summary>
    /// MSI property overrides forwarded to the installer at execution time.
    /// Excluded from JSON serialization to prevent secrets (bracket refs and plain values) leaking into plan output.
    /// </summary>
    [JsonIgnore]
    public Dictionary<string, string> Properties { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> SlipstreamPatchPaths { get; init; } = [];

    /// <summary>
    /// Absolute path to this package's payload as extracted on the TARGET machine, resolved by
    /// <see cref="FalkForge.Engine.Pipeline.ApplyStep"/> from the bootstrapper-forwarded payload
    /// extraction root (<see cref="FalkForge.Engine.Pipeline.PipelineContext.PayloadRoot"/>).
    /// Null on the <c>--manifest</c> / <c>forge plan</c> / offline-layout path, where the
    /// manifest's build-authored <see cref="PackageInfo.SourcePath"/> is the authoritative path.
    /// Runtime-only and machine-specific — excluded from JSON so it never leaks into plan output.
    /// </summary>
    [JsonIgnore]
    public string? ResolvedSourcePath { get; set; }

    /// <summary>
    /// The path an executor should hand to the installer: the extraction-resolved path when the
    /// bootstrapper forwarded a payload root, otherwise the manifest's build-authored
    /// <see cref="PackageInfo.SourcePath"/>. This is the single place the "resolved wins, else
    /// verbatim SourcePath" fallback is expressed so every executor stays consistent.
    /// </summary>
    [JsonIgnore]
    public string EffectiveSourcePath => ResolvedSourcePath ?? Package.SourcePath;
}
