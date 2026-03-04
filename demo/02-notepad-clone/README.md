# Demo 02: Notepad Clone

A realistic application installer for a text editor called "FalkPad." Demonstrates shortcuts, registry entries, major upgrade support, and license agreement display.

## What This Demonstrates

- Per-machine x64 installer with a description and license file
- `MsiDialogSet.InstallDir` dialog set letting users choose the install directory
- Desktop and Start Menu shortcuts with custom icons
- Writing registry keys and values (including MSI property references, DWord values, and default values)
- Removing registry keys on uninstall with `RemoveRegistry`
- Startup folder shortcut with `OnStartup()`, custom arguments, and working directory
- Major upgrade and downgrade blocking

## Key API Calls

```csharp
// Scope and architecture
package.Scope = InstallScope.PerMachine;
package.Architecture = ProcessorArchitecture.X64;
package.LicenseFile = "payload/license.rtf";

// Dialog set with install-directory picker
package.UseDialogSet(MsiDialogSet.InstallDir);

// Multiple files deployed to one directory
package.Files(files => files
    .Add("payload/falkpad.exe")
    .Add("payload/falkpad.dll")
    .Add("payload/readme.txt")
    .Add("payload/license.rtf")
    .To(KnownFolder.ProgramFiles / "Falk Software" / "FalkPad"));

// Shortcuts — desktop, start menu, and startup folder
package.Shortcut("FalkPad", "falkpad.exe")
    .WithIcon("payload/falkpad.ico")
    .WithDescription("Launch FalkPad text editor")
    .OnDesktop();

package.Shortcut("FalkPad", "falkpad.exe")
    .WithIcon("payload/falkpad.ico")
    .OnStartMenu("Falk Software");

// Startup shortcut — launches on Windows login with arguments and working directory
package.Shortcut("FalkPad Startup", "falkpad.exe")
    .WithArguments("--minimized")
    .WithWorkingDirectory(@"[ProgramFilesFolder]Falk Software\FalkPad")
    .OnStartup();

// Registry entries — string values, DWord, default value, and MSI property references
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
    .Root(RegistryRoot.LocalMachine)
    .Key(@"Software\FalkSoftware\FalkPad")
    .RemoveKey());

// Major upgrade support with downgrade blocking
package.MajorUpgrade(_ => { });
package.Downgrade(d => d.Block("A newer version of FalkPad is already installed."));
```

## How to Build

```bash
dotnet build demo/02-notepad-clone
```

## Notes

- `MsiProperty.InstallDir` resolves at install time to the user's chosen directory, making registry values dynamic.
- `DWord()` writes a REG_DWORD (32-bit integer) value. `DefaultValue()` sets the `(Default)` value of the registry key.
- `RemoveRegistry` ensures the specified registry key is cleaned up on uninstall. Without it, registry entries created by `package.Registry()` may be left behind.
- `OnStartup()` places a shortcut in the Windows Startup folder so the application launches automatically at login.
- `WithArguments()` passes command-line arguments to the shortcut target. `WithWorkingDirectory()` sets the working directory for the launched process.
- `MajorUpgrade` with default options removes older versions before installing. `Downgrade.Block` prevents installing an older version over a newer one.
- Start menu shortcuts use a subfolder via `.OnStartMenu("Falk Software")`.
