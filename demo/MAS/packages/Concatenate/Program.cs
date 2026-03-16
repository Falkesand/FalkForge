using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

// ---------------------------------------------------------------------------
// Concatenate MSI -- Utility tool
// WiX parity: setup/Concatenate/*.wxs
// UpgradeCode matches WiX: C503075E-2076-4A2A-9E72-4C06D25DAF39
// Installs into ProgramFiles\Aptus\MultiAccess\Utilities (shared with MultiAccess)
// Also places a readme in the MultiAccess root directory.
// ---------------------------------------------------------------------------

var installFolder = KnownFolder.ProgramFiles / "Aptus" / "MultiAccess";
var utilitiesFolder = installFolder / "Utilities";

return Installer.Build(args, package =>
{
    // --- Product Information ---
    package.Name = "Concatenate";
    package.Manufacturer = "ASSA ABLOY Opening Solutions Sweden AB";
    package.Version = new Version(8, 9, 0);
    package.UpgradeCode = new Guid("C503075E-2076-4A2A-9E72-4C06D25DAF39");
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
    // Concatenate executable in Utilities folder
    package.Files(files => files
        .Add("payload/Concatenate.exe")
        .To(utilitiesFolder));

    // Documentation readme in the MultiAccess root
    package.Files(files => files
        .Add("payload/Concatenate-README.txt")
        .To(installFolder));

}, new MsiCompiler());
