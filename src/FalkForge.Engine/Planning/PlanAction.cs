namespace FalkForge.Engine.Planning;

using System.Collections.ObjectModel;
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

    /// <summary>
    /// Secret MSI property values collected through <c>SetSecureProperty</c>. These never travel on the
    /// installer command line: the executor generates a runtime transform (<c>.mst</c>) that sets them and
    /// applies it via <c>TRANSFORMS=</c>, so the plaintext stays off the command line and out of the
    /// Windows Installer log. Runtime-only and never serialized. The values are owned by the UI channel for
    /// the session; the executor reads them and must not dispose them.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, SensitiveBytes> SecureProperties { get; set; } =
        ReadOnlyDictionary<string, SensitiveBytes>.Empty;

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

    /// <summary>
    /// The package's declared MSI transforms (.mst) resolved to their absolute extracted paths on
    /// the TARGET machine, keyed by transform id. <see cref="FalkForge.Engine.Pipeline.ApplyStep"/>
    /// resolves each <see cref="PackageInfo.Transforms"/> entry under the bootstrapper-forwarded payload
    /// root with the same containment guard the MSI itself uses, and the elevated executor forwards the
    /// pairs to the companion, which binds each to the publisher-SIGNED hash and the SIGNED association
    /// map before applying it. Empty on the <c>--manifest</c> / plan / offline-layout path and for any
    /// package that declares no transform. Runtime-only and machine-specific — excluded from JSON so it
    /// never leaks into plan output.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<ResolvedTransform> ResolvedTransformPaths { get; set; } = [];
}
