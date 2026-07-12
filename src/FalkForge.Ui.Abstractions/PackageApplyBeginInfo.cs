namespace FalkForge.Ui.Abstractions;

/// <summary>
/// Per-package apply-begin information delivered to
/// <c>InstallerPage.OnApplyPackageBeginAsync</c>, immediately before a package's installer runs
/// during the Apply phase.
/// </summary>
public readonly record struct PackageApplyBeginInfo(string PackageId, string DisplayName);
