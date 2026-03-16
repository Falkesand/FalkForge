using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

// ---------------------------------------------------------------------------
// Konfigurera MSI -- Configuration tool
// WiX parity: setup/Konfigurera/*.wxs
// UpgradeCode matches WiX: 976E7E9D-30C0-47AB-AF56-FBA9C00E0CE1
// Installs into ProgramFiles\Aptus\MultiAccess\Utilities (shared with MultiAccess)
// ---------------------------------------------------------------------------

var utilitiesFolder = KnownFolder.ProgramFiles / "Aptus" / "MultiAccess" / "Utilities";

return Installer.Build(args, package =>
{
    // --- Product Information ---
    package.Name = "Konfigurera";
    package.Manufacturer = "ASSA ABLOY Opening Solutions Sweden AB";
    package.Version = new Version(8, 9, 0);
    package.UpgradeCode = new Guid("976E7E9D-30C0-47AB-AF56-FBA9C00E0CE1");
    package.Scope = InstallScope.PerMachine;
    package.DefaultInstallDirectory = utilitiesFolder;
    package.UseDialogSet(MsiDialogSet.None);

    // --- Major Upgrade ---
    package.MajorUpgrade(mu =>
        mu.Schedule(RemoveExistingProductsSchedule.AfterInstallValidate));

    // --- Launch Condition (downgrade prevention) ---
    package.Require(
        Condition.IsInstalled | Condition.Raw("NOT NEWER_VERSION_FOUND"),
        "A newer version of [ProductName] is already installed. Exiting installation.");

    // --- Files ---
    // Single executable installed to the Utilities subfolder
    package.Files(files => files
        .Add("payload/Konfigurera.exe")
        .To(utilitiesFolder));

}, new MsiCompiler());
