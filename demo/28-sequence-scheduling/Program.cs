using FalkForge;
using FalkForge.Compiler.Msi;

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
    package.CustomAction("PostInstallCleanup", ca => { ca.SetProperty("CLEANUP_FLAG", "1"); });

    package.ExecuteSequence(seq => seq
        .Action("PostInstallCleanup")
        .After("InstallFinalize")
        .Condition(Condition.IsInstalling));

    // Schedule an action in the UI sequence (runs during user interaction phase)
    package.UISequence(seq => seq
        .Action("PostInstallCleanup")
        .After("ExecuteAction")
        .Condition(Condition.IsInstalling));
}, new MsiCompiler());