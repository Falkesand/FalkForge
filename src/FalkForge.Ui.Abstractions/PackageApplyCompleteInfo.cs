namespace FalkForge.Ui.Abstractions;

/// <summary>
/// Per-package apply-complete information delivered to
/// <c>InstallerPage.OnApplyPackageCompleteAsync</c>, immediately after a package's installer
/// returns during the Apply phase. <see cref="Succeeded"/> is false for the package whose
/// failure aborts the apply.
/// </summary>
public readonly record struct PackageApplyCompleteInfo(string PackageId, string DisplayName, bool Succeeded);
