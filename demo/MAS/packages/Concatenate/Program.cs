using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

return Installer.Build(args, package =>
{
    package.Name = "Concatenate";
    package.Manufacturer = "ASSA ABLOY";
    package.Version = new Version(8, 9, 0);
    package.UpgradeCode = new Guid("E5F6A7B8-C9D0-4E1F-2A3B-4C5D6E7F8091");
    package.Scope = InstallScope.PerMachine;
    package.UseDialogSet(MsiDialogSet.None);
    package.Reproducible();

    package.Files(files => files
        .Add("payload/Concatenate.exe")
        .To(KnownFolder.ProgramFiles / "ASSA ABLOY" / "Concatenate"));

    package.MajorUpgrade(mu =>
    {
        mu.AllowSameVersionUpgrades();
        mu.Schedule(RemoveExistingProductsSchedule.AfterInstallExecute);
    });
}, new MsiCompiler());
