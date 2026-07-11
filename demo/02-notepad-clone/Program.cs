using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Localization;
using FalkForge.Models;

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

    package.Localization(loc => loc
        .AddBuiltInCultures()
        .DefaultCulture("en-US")
        .DetectCulture());

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

    // Startup shortcut — launches on Windows login
    package.Shortcut("FalkPad Startup", "falkpad.exe")
        .WithArguments("--minimized")
        // WkDir is a Directory-table key, not a Formatted path: INSTALLDIR
        // resolves to this product's install folder. A bracketed path would be
        // rejected by SHC004 and ignored.
        .WithWorkingDirectory("INSTALLDIR")
        .OnStartup();

    // Registry entries
    package.Registry(reg => reg
        .Key(RegistryRoot.LocalMachine, @"Software\FalkSoftware\FalkPad", key =>
        {
            key.Value("Version", "2.1.0");
            key.Value("InstallPath", MsiProperty.InstallDir);
            key.DWord("EditorFlags", 3);
            key.DefaultValue("FalkPad Text Editor");
        }));

    // Remove registry entries on uninstall
    package.RemoveRegistry(rr => rr
        .Id("RemoveFalkPadRegKey")
        .Root(RegistryRoot.LocalMachine)
        .Key(@"Software\FalkSoftware\FalkPad")
        .RemoveKey());

    // Major upgrade support -- block downgrades
    package.MajorUpgrade(_ => { });
    package.Downgrade(d => d.Block("A newer version of FalkPad is already installed. Please uninstall it first."));
}, new MsiCompiler());