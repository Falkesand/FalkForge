using FalkForge;
using FalkForge.Builders;
using FalkForge.Compiler.Msi;
using FalkForge.Models;
using FalkForge.Platform.Windows;

// A small application installer with shortcuts, registry, major upgrade, and license.
return Installer.Build(args, package =>
{
    package.Name = "FalkPad";
    package.Manufacturer = "Falk Software";
    package.Version = new Version(2, 1, 0);
    package.Scope = InstallScope.PerMachine;
    package.Architecture = ProcessorArchitecture.X64;
    package.Description = "A simple text editor";
    package.LicenseFile = "payload/license.rtf";

    package.UseDialogSet(MsiDialogSet.InstallDir);

    // Application files
    package.Files(files => files
        .Add("payload/falkpad.exe")
        .Add("payload/falkpad.dll")
        .Add("payload/readme.txt")
        .Add("payload/license.rtf")
        .To(KnownFolder.ProgramFiles / "Falk Software" / "FalkPad"));

    // Desktop shortcut with icon
    package.Shortcut("FalkPad", "falkpad.exe")
        .WithIcon("payload/falkpad.ico")
        .WithDescription("Launch FalkPad text editor")
        .OnDesktop();

    // Start menu shortcut under company subfolder
    package.Shortcut("FalkPad", "falkpad.exe")
        .WithIcon("payload/falkpad.ico")
        .WithDescription("Launch FalkPad text editor")
        .OnStartMenu("Falk Software");

    // Registry entries
    package.Registry(reg => reg
        .Key(RegistryRoot.LocalMachine, @"Software\FalkSoftware\FalkPad", key =>
        {
            key.Value("Version", "2.1.0");
            key.Value("InstallPath", "[INSTALLDIR]");
        }));

    // Major upgrade support -- block downgrades
    package.MajorUpgrade(upgrade =>
    {
        upgrade.DowngradeErrorMessage(
            "A newer version of FalkPad is already installed. Please uninstall it first.");
    });

}, new MsiCompiler(new WindowsFileSystem()));
