namespace FalkForge.Engine.Protocol.Messages;

/// <summary>
/// Engine → UI notification that planning is about to begin for a single package during the
/// Plan phase. Emitted once per package, in manifest chain order. Observational: the UI is
/// notified but cannot skip the package (per-package veto is not wired end-to-end).
/// </summary>
public sealed class PlanPackageBeginMessage : EngineMessage
{
    public override MessageType Type => MessageType.PlanPackageBegin;

    /// <summary>Manifest package identifier.</summary>
    public required string PackageId { get; init; }

    /// <summary>Human-readable package display name.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Planned action for this package (e.g. Install, Uninstall, Repair).</summary>
    public required string PlannedAction { get; init; }
}
