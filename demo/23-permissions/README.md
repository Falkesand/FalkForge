# Demo 23: Permissions

Sets NTFS file system permissions on the installation directory, granting the BUILTIN\Users group read, write, and execute access.

## What This Demonstrates

- Setting directory permissions with `package.Permission()`
- Specifying a Windows user or group
- Using numeric permission masks for granular access control
- Setting permissions via SDDL (Security Descriptor Definition Language) strings
- Targeting a specific MSI table with `ForTable()`

## Key API Calls

```csharp
// Grant modify permissions to BUILTIN\Users
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
```

## How to Build

```bash
dotnet build demo/23-permissions
```

## Notes

- The permission value `0x001301BF` combines `FILE_GENERIC_READ`, `FILE_GENERIC_WRITE`, and `FILE_GENERIC_EXECUTE`. These are standard Win32 access mask constants.
- Permissions are applied during the install sequence and removed on uninstall when the directory is deleted.
- Use well-known group names like `BUILTIN\Users` or `BUILTIN\Administrators` for portability across localized Windows installations.
- `Sddl` allows expressing permissions as a Security Descriptor Definition Language string, which provides full control over DACLs and SACLs in a compact format.
- `ForTable("CreateFolder")` specifies which MSI table the permission applies to. This is necessary when the target identifier could exist in multiple tables (e.g., `CreateFolder`, `File`, `Registry`).
