namespace FalkForge.Engine.Protocol.Messages;

/// <summary>
/// Engine → UI notification that planning has completed for a single package during the
/// Plan phase. Emitted once per package, immediately after its
/// <see cref="PlanPackageBeginMessage"/>. Observational.
/// </summary>
public sealed class PlanPackageCompleteMessage : EngineMessage
{
    public override MessageType Type => MessageType.PlanPackageComplete;

    /// <summary>Manifest package identifier.</summary>
    public required string PackageId { get; init; }

    /// <summary>Human-readable package display name.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Planned action for this package (e.g. Install, Uninstall, Repair).</summary>
    public required string PlannedAction { get; init; }
}
