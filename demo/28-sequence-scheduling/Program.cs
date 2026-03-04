using FalkForge;
using FalkForge.Builders;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

// Control installation sequence ordering and cabinet compression settings.
return Installer.Build(args, package =>
{
    package.Name = "Sequence Scheduling Demo";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    package.Files(files => files
        .Add("payload/app.exe")
        .To(KnownFolder.ProgramFiles / "Demo" / "SequenceDemo"));

    // Schedule a custom action after InstallFinalize
    package.CustomAction("PostInstallCleanup", ca =>
    {
        ca.SetProperty("CLEANUP_FLAG", "1");
    });

    package.ExecuteSequence(seq => seq
        .Action("PostInstallCleanup")
        .After("InstallFinalize")
        .Condition(Condition.IsInstalling));

}, new MsiCompiler());
