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

    // Grant modify permissions to BUILTIN\Users on the install folder via SDDL (MsiLockPermissionsEx).
    // MSI allows only one permission style (LockPermissions or MsiLockPermissionsEx) per database;
    // both permissions below use SDDL so they both go through MsiLockPermissionsEx.
    package.Permission(@"[ProgramFilesFolder]Demo\PermissionsDemo", perm =>
    {
        // FILE_GENERIC_READ | FILE_GENERIC_WRITE | FILE_GENERIC_EXECUTE for BUILTIN\Users
        perm.Sddl = "D:(A;;GRGWGX;;;BU)";
        perm.ForTable("File");
    });

    // Fine-grained access control on a CreateFolder entry
    package.Permission("DataFolder", p =>
    {
        p.Sddl = "D:(A;;FA;;;BA)(A;;FR;;;BU)";
        p.ForTable("CreateFolder");
    });
}, new MsiCompiler());