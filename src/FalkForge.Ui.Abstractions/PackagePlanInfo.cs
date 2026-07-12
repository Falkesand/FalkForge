namespace FalkForge.Ui.Abstractions;

/// <summary>
/// Per-package planning information delivered to
/// <c>InstallerPage.OnPlanPackageBeginAsync</c> / <c>OnPlanPackageCompleteAsync</c> during the
/// Plan phase: the package identifier, its display name, and the action planned for it
/// (e.g. "Install", "Uninstall", "Repair").
/// </summary>
public readonly record struct PackagePlanInfo(string PackageId, string DisplayName, string PlannedAction);
