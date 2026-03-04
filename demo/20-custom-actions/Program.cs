using FalkForge;
using FalkForge.Builders;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

// Custom actions: set a property and schedule a deferred action.
return Installer.Build(args, package =>
{
    package.Name = "Custom Actions Demo";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    package.Files(files => files
        .Add("payload/app.exe")
        .To(KnownFolder.ProgramFiles / "Demo" / "CustomActionDemo"));

    // SetProperty custom action — runs during UI sequence
    package.CustomAction("SetInstallMode", ca =>
    {
        ca.SetProperty("INSTALL_MODE", "standard");
        ca.Condition = Condition.IsInstalling.ToString();
    });

    // Deferred custom action — runs elevated during execute sequence
    package.CustomAction("ConfigureApp", ca =>
    {
        ca.SetProperty("CONFIGURE_CMD", @"[ProgramFilesFolder]Demo\CustomActionDemo\app.exe --configure");
        ca.Deferred();
        ca.NoImpersonate();
        ca.After = "InstallFiles";
    });

    // Rollback custom action — undoes ConfigureApp on failure
    package.CustomAction("UndoConfigureApp", ca =>
    {
        ca.SetProperty("UNDO_CMD", @"[ProgramFilesFolder]Demo\CustomActionDemo\app.exe --unconfigure");
        ca.Rollback();
        ca.NoImpersonate();
        ca.Before = "ConfigureApp";
    });

}, new MsiCompiler());
