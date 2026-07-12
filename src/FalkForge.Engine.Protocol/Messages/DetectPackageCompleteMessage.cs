namespace FalkForge.Engine.Protocol.Messages;

/// <summary>
/// Engine → UI notification that a single package's detection has completed during the
/// Detect phase. Emitted once per package, in manifest chain order, between
/// <see cref="DetectBeginMessage"/> and the overall detection completion. Observational:
/// the UI cannot veto a package from this message.
/// </summary>
public sealed class DetectPackageCompleteMessage : EngineMessage
{
    public override MessageType Type => MessageType.DetectPackageComplete;

    /// <summary>Manifest package identifier.</summary>
    public required string PackageId { get; init; }

    /// <summary>Detected installation state of this package.</summary>
    public required InstallState State { get; init; }

    /// <summary>Installed version of this package when detectable (MSI), otherwise null.</summary>
    public string? Version { get; init; }
}
