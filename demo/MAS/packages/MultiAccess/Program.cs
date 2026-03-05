using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

return Installer.Build(args, package =>
{
    package.Name = "MultiAccess";
    package.Manufacturer = "ASSA ABLOY";
    package.Version = new Version(8, 9, 0);
    package.UpgradeCode = new Guid("A1B2C3D4-E5F6-4A7B-8C9D-0E1F2A3B4C5D");
    package.Scope = InstallScope.PerMachine;
    package.UseDialogSet(MsiDialogSet.None);
    package.Reproducible();

    package.Files(files => files
        .Add("payload/MultiAccess.exe")
        .To(KnownFolder.ProgramFiles / "ASSA ABLOY" / "MultiAccess"));

    package.MajorUpgrade(mu =>
    {
        mu.AllowSameVersionUpgrades();
        mu.Schedule(RemoveExistingProductsSchedule.AfterInstallExecute);
    });
}, new MsiCompiler());
