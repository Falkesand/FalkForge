# Demo 25: File Operations

Demonstrates MSI file operations beyond simple deployment: creating empty directories, duplicating files, and removing files on uninstall.

## What This Demonstrates

- Creating empty directories with `package.CreateFolder()`
- Duplicating an installed file under a new name with `package.DuplicateFile()`
- Removing files matching a wildcard pattern on uninstall with `package.RemoveFile()`
- Conditional file installation with `ComponentCondition()` on a `FileSetBuilder`

## Key API Calls

```csharp
// Create an empty data directory
package.CreateFolder(cf => cf
    .Directory(@"[ProgramFilesFolder]Demo\FileOpsDemo\Data"));

// Duplicate template.cfg as default.cfg in the same folder
package.DuplicateFile(df => df
    .FileRef("template.cfg")
    .DestFileName("default.cfg")
    .DestDirectory(@"[ProgramFilesFolder]Demo\FileOpsDemo"));

// Remove all .log files from the Data directory on uninstall
package.RemoveFile(rf => rf
    .Directory(@"[ProgramFilesFolder]Demo\FileOpsDemo\Data")
    .FileName("*.log")
    .OnUninstall());

// Conditional file installation — only installs when property is set
package.Files(files => files
    .Add("payload/debug-tools.exe")
    .To(KnownFolder.ProgramFiles / "Demo" / "FileOpsDemo" / "Debug")
    .ComponentCondition("INSTALL_DEBUG_TOOLS"));
```

## How to Build

```bash
dotnet build demo/25-file-operations
```

## Notes

- `DuplicateFile` creates a copy at install time. The original and the copy are both tracked by MSI.
- `RemoveFile` with `OnUninstall()` cleans up files that the application may have created at runtime (e.g., log files). These are not tracked by MSI otherwise.
- `CreateFolder` ensures the directory exists even if no files are installed into it.
- `ComponentCondition()` on the `FileSetBuilder` sets an MSI condition on the component. The files are only installed when the condition evaluates to true (e.g., when the `INSTALL_DEBUG_TOOLS` property is set).
