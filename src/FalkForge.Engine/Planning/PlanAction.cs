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
}
