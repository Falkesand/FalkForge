using FalkForge;
using FalkForge.Builders;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

// Write configuration entries to an INI file during installation.
return Installer.Build(args, package =>
{
    package.Name = "INI Files Demo";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    package.Files(files => files
        .Add("payload/app.exe")
        .Add("payload/settings.ini")
        .To(KnownFolder.ProgramFiles / "Demo" / "IniDemo"));

    // Write entries to the INI file
    package.IniFile("settings.ini", ini =>
    {
        ini.Section("General");
        ini.Key("InstallPath");
        ini.Value(@"[ProgramFilesFolder]Demo\IniDemo");
        ini.Action(IniFileAction.CreateEntry);
    });

    package.IniFile("settings.ini", ini =>
    {
        ini.Section("General");
        ini.Key("Version");
        ini.Value("1.0.0");
        ini.Action(IniFileAction.CreateEntry);
    });

}, new MsiCompiler());
