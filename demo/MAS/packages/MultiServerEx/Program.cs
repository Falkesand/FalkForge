using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

return Installer.Build(args, package =>
{
    package.Name = "MultiServerEx";
    package.Manufacturer = "ASSA ABLOY";
    package.Version = new Version(8, 9, 0);
    package.UpgradeCode = new Guid("C3D4E5F6-A7B8-4C9D-0E1F-2A3B4C5D6E7F");
    package.Scope = InstallScope.PerMachine;
    package.UseDialogSet(MsiDialogSet.None);
    package.Reproducible();

    package.Files(files => files
        .Add("payload/MultiServerEx.exe")
        .To(KnownFolder.ProgramFiles / "ASSA ABLOY" / "MultiServerEx"));

    package.MajorUpgrade(mu =>
    {
        mu.AllowSameVersionUpgrades();
        mu.Schedule(RemoveExistingProductsSchedule.AfterInstallExecute);
    });
}, new MsiCompiler());
