using FalkForge;
using FalkForge.Compiler.Msi;

// Set NTFS permissions on the install directory.
return Installer.Build(args, package =>
{
    package.Name = "Permissions Demo";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    package.Files(files => files
        .Add("payload/app.exe")
        .To(KnownFolder.ProgramFiles / "Demo" / "PermissionsDemo"));

    // Grant modify permissions to BUILTIN\Users on the install folder via SDDL
    package.Permission(@"[ProgramFilesFolder]Demo\PermissionsDemo", perm =>
    {
        perm.User = @"BUILTIN\Users";
        perm.Permission = 0x001301BF; // FILE_GENERIC_READ | FILE_GENERIC_WRITE | FILE_GENERIC_EXECUTE
    });

    // Permission via SDDL string — fine-grained access control
    package.Permission("DataFolder", p =>
    {
        p.Sddl = "D:(A;;FA;;;BA)(A;;FR;;;BU)";
        p.ForTable("CreateFolder");
    });
}, new MsiCompiler());