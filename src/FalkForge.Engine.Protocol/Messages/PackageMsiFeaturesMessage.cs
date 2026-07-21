namespace FalkForge.Engine.Protocol.Messages;

/// <summary>
/// Engine → UI advertisement of the features found inside one MSI package, so the UI can
/// present a per-package feature picker. Emitted once per feature-selectable MSI. The
/// selection the user makes comes back as a <see cref="SetPackageFeatureSelectionMessage"/>.
/// </summary>
public sealed class PackageMsiFeaturesMessage : EngineMessage
{
    public override MessageType Type => MessageType.PackageMsiFeatures;

    /// <summary>Manifest package identifier the features belong to.</summary>
    public required string PackageId { get; init; }

    /// <summary>The MSI's <c>Feature</c> rows, in table order.</summary>
    public required MsiFeatureInfo[] Features { get; init; }
}
