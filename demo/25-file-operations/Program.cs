using FalkForge;
using FalkForge.Builders;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

// File operations: move, duplicate, remove, and create folders.
return Installer.Build(args, package =>
{
    package.Name = "File Operations Demo";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    package.Files(files => files
        .Add("payload/app.exe")
        .Add("payload/template.cfg")
        .To(KnownFolder.ProgramFiles / "Demo" / "FileOpsDemo"));

    // Create an empty data directory
    package.CreateFolder(cf => cf
        .Directory(@"[ProgramFilesFolder]Demo\FileOpsDemo\Data"));

    // Duplicate template.cfg as default.cfg in the same folder
    package.DuplicateFile(df => df
        .FileRef("template.cfg")
        .DestFileName("default.cfg")
        .DestDirectory(@"[ProgramFilesFolder]Demo\FileOpsDemo"));

    // Remove log files on uninstall
    package.RemoveFile(rf => rf
        .Directory(@"[ProgramFilesFolder]Demo\FileOpsDemo\Data")
        .FileName("*.log")
        .OnUninstall());

    // Conditional file installation — only installs when property is set
    package.Files(files => files
        .Add("payload/debug-tools.exe")
        .To(KnownFolder.ProgramFiles / "Demo" / "FileOpsDemo" / "Debug")
        .ComponentCondition("INSTALL_DEBUG_TOOLS"));

}, new MsiCompiler());
