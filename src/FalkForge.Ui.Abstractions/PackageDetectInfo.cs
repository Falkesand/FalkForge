namespace FalkForge.Ui.Abstractions;

using FalkForge.Engine.Protocol;

/// <summary>
/// Per-package detection outcome delivered to <c>InstallerPage.OnDetectPackageCompleteAsync</c>
/// during the Detect phase: the package identifier, its detected installation state, and its
/// installed version when detectable (MSI packages), otherwise null.
/// </summary>
public readonly record struct PackageDetectInfo(string PackageId, InstallState State, string? Version);
