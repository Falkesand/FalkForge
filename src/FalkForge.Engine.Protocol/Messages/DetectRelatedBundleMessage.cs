namespace FalkForge.Engine.Protocol.Messages;

/// <summary>
/// Engine → UI notification that a related bundle was detected on the machine during the
/// Detect phase (e.g. an older version eligible for upgrade). Emitted once per detected
/// related bundle, after the per-package detection notifications. Observational.
/// </summary>
public sealed class DetectRelatedBundleMessage : EngineMessage
{
    public override MessageType Type => MessageType.DetectRelatedBundle;

    /// <summary>Related bundle identifier (upgrade code) declared in the manifest.</summary>
    public required string BundleId { get; init; }

    /// <summary>Relationship of the detected bundle to the current bundle.</summary>
    public required RelatedBundleRelation Relation { get; init; }

    /// <summary>Installed version of the detected related bundle.</summary>
    public required string InstalledVersion { get; init; }
}
