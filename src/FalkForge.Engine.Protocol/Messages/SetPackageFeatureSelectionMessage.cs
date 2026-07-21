namespace FalkForge.Engine.Protocol.Messages;

/// <summary>
/// UI → Engine per-package feature selection: the full set of feature ids the user chose
/// to install for one MSI package. The engine turns this into an <c>ADDLOCAL</c> property
/// for that package's install action. Distinct from
/// <see cref="SetFeatureSelectionMessage"/>, which toggles a single bundle-level (whole
/// package) feature.
/// </summary>
public sealed class SetPackageFeatureSelectionMessage : EngineMessage
{
    public override MessageType Type => MessageType.SetPackageFeatureSelection;

    /// <summary>Manifest package identifier the selection applies to.</summary>
    public required string PackageId { get; init; }

    /// <summary>The feature ids selected for install within that package.</summary>
    public required string[] SelectedFeatureIds { get; init; }
}
