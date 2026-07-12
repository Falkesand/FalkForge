namespace FalkForge.Engine.Protocol.Messages;

/// <summary>
/// Engine → UI notification that applying (executing) a single package has completed during
/// the Apply phase. Emitted once per package, immediately after the package's installer
/// returns. <see cref="Succeeded"/> is false for the package whose failure aborts the apply.
/// Observational.
/// </summary>
public sealed class ApplyPackageCompleteMessage : EngineMessage
{
    public override MessageType Type => MessageType.ApplyPackageComplete;

    /// <summary>Manifest package identifier.</summary>
    public required string PackageId { get; init; }

    /// <summary>Human-readable package display name.</summary>
    public required string DisplayName { get; init; }

    /// <summary>True when the package executed successfully.</summary>
    public required bool Succeeded { get; init; }
}
