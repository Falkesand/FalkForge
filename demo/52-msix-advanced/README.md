# Demo 52: MSIX Advanced

Demonstrates advanced MSIX packaging features including multiple applications, file type associations, protocol
handlers, auto-update settings, package dependencies, and visual elements with logos.

## What This Demonstrates

- Multiple `Application()` entries in a single MSIX package (editor, CLI tool, background service)
- `Extension()` for file type associations and protocol handlers
- `Capability()` and `RestrictedCapability()` for declaring app permissions
- `Dependency()` for declaring framework package dependencies (e.g., VCLibs)
- `UpdateSettings()` with `HoursBetweenUpdateChecks()`, `ShowPrompt()`, `AutomaticBackgroundTask()`, and
  `ForceUpdateFromAnyVersion()` for automatic update configuration
- `LogoPath()`, `Square44x44Logo()`, `Square150x150Logo()`, `Wide310x150Logo()` for visual elements
- `MinWindowsVersion()` and `MaxVersionTested()` for OS version targeting
- `EntryPoint()` for background service applications

## Key API Calls

```csharp
msix
    .Application("Editor", "editor.exe", app => app
        .DisplayName("Demo Editor")
        .BackgroundColor("#1E1E1E")
        .Square44x44Logo("assets/Square44x44Logo.png")
        .Square150x150Logo("assets/Square150x150Logo.png")
        .Wide310x150Logo("assets/Wide310x150Logo.png"))
    .Extension("windows.fileTypeAssociation", "DemoApp.Editor")
    .Extension("windows.protocol", "DemoApp.Editor")
    .Capability("internetClient")
    .RestrictedCapability("runFullTrust")
    .Dependency("Microsoft.VCLibs.140.00.UWPDesktop", "CN=Microsoft Corporation...", new Version(14, 0, 30704, 0))
    .UpdateSettings("https://releases.example.com/Demo.appinstaller", update =>
    {
        update.HoursBetweenUpdateChecks(6);
        update.ShowPrompt();
        update.AutomaticBackgroundTask();
        update.ForceUpdateFromAnyVersion();
    });
```

## How to Run

This demo uses a C# script (.csx) format:

```bash
dotnet script demo/52-msix-advanced/msix-advanced.csx -- -o ./output
```

## Notes

- Each `Application()` entry becomes a separate `<Application>` element in the AppxManifest.xml.
- `EntryPoint()` is required for background service applications that don't have a visible window.
- `Extension()` declares MSIX extensions in the manifest. The `category` parameter maps to the
  `windows.fileTypeAssociation`, `windows.protocol`, etc. extension categories.
- `UpdateSettings()` generates a companion `.appinstaller` file alongside the `.msix` package.
- `ForceUpdateFromAnyVersion()` allows the update mechanism to upgrade from any previous version, even if the version
  number is lower (useful for rollback scenarios).
- `RestrictedCapability("runFullTrust")` is required for desktop bridge (Win32) applications packaged as MSIX.
