namespace FalkForge.Engine.Protocol.Messages;

/// <summary>
/// Engine → UI notification that applying (executing) a single package is about to begin
/// during the Apply phase. Emitted once per package, in execution (chain) order, immediately
/// before the package's installer runs. Observational.
/// </summary>
public sealed class ApplyPackageBeginMessage : EngineMessage
{
    public override MessageType Type => MessageType.ApplyPackageBegin;

    /// <summary>Manifest package identifier.</summary>
    public required string PackageId { get; init; }

    /// <summary>Human-readable package display name.</summary>
    public required string DisplayName { get; init; }
}
