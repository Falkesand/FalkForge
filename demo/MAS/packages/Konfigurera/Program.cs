using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

return Installer.Build(args, package =>
{
    package.Name = "Konfigurera";
    package.Manufacturer = "ASSA ABLOY";
    package.Version = new Version(8, 9, 0);
    package.UpgradeCode = new Guid("D4E5F6A7-B8C9-4D0E-1F2A-3B4C5D6E7F80");
    package.Scope = InstallScope.PerMachine;
    package.UseDialogSet(MsiDialogSet.None);
    package.Reproducible();

    package.Files(files => files
        .Add("payload/Konfigurera.exe")
        .To(KnownFolder.ProgramFiles / "ASSA ABLOY" / "Konfigurera"));

    package.MajorUpgrade(mu =>
    {
        mu.AllowSameVersionUpgrades();
        mu.Schedule(RemoveExistingProductsSchedule.AfterInstallExecute);
    });
}, new MsiCompiler());
