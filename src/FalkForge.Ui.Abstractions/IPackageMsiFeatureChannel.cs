namespace FalkForge.Ui.Abstractions;

using FalkForge.Engine.Protocol;

/// <summary>
/// Optional engine capability exposing per-package MSI feature selection to the UI.
/// The engine advertises the <c>Feature</c> rows of each feature-selectable MSI package at
/// detect time (keyed by manifest package id); the UI presents a picker and sends the chosen
/// feature-id set back per package, which the engine turns into that package's <c>ADDLOCAL</c>.
/// <para>
/// Modelled as a side interface — mirroring <see cref="IPackageLifecycleEvents"/> — rather than
/// widening <see cref="IInstallerEngine"/>, because only the real pipe-backed engine advertises
/// features; design-time and headless engines have nothing to expose and would otherwise carry
/// no-op members. Consumers detect the capability with <c>engine is IPackageMsiFeatureChannel</c>.
/// </para>
/// </summary>
public interface IPackageMsiFeatureChannel
{
    /// <summary>
    /// Per-package advertised MSI features, keyed by manifest package id. Empty until the engine
    /// advertises (which it only does for packages authored with per-package feature selection).
    /// </summary>
    IReadOnlyDictionary<string, IReadOnlyList<MsiFeatureInfo>> PackageMsiFeatures { get; }

    /// <summary>
    /// Sends the full set of feature ids the user selected to install for one MSI package.
    /// Replaces any previous selection for that package.
    /// </summary>
    /// <param name="packageId">Manifest package id the selection applies to.</param>
    /// <param name="selectedFeatureIds">The feature ids selected for install within that package.</param>
    void SetPackageFeatureSelection(string packageId, IReadOnlyList<string> selectedFeatureIds);
}
