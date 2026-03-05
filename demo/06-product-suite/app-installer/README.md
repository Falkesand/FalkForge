# Demo 06: Product Suite -- App Installer

The desktop application MSI package for the Acme Product Suite. Installs application binaries, creates desktop and Start
Menu shortcuts, writes registry entries, and supports localized UI with user-selectable install directory.

## What This Demonstrates

- Complete MSI package definition with `Installer.Build`
- Feature-based file grouping with `p.Feature()` and `p.Files()`
- Desktop and Start Menu shortcuts via `p.Shortcut().OnDesktop()` and `OnStartMenu()`
- Registry entries via `p.Registry()` with `RegistryRoot.LocalMachine`
- Install directory with `KnownFolder.ProgramFiles` path composition using the `/` operator
- `MsiDialogSet.InstallDir` for directory selection UI
- Major upgrade and downgrade blocking
- Launch condition with `Condition.IsWindows10OrLater`

## Key API Calls

```csharp
p.UseDialogSet(MsiDialogSet.InstallDir);
p.DefaultInstallDirectory = KnownFolder.ProgramFiles / "Acme Corporation" / "AcmeApp";

p.Files(f => f
    .Add(Path.Combine(payloadDir, "acmeapp.exe"))
    .To(installDir));

p.Shortcut("Acme Application", "acmeapp.exe")
    .WithDescription("Launch Acme Application")
    .OnDesktop();

p.Registry(r => r
    .Key(RegistryRoot.LocalMachine, @"Software\Acme\AcmeApp", k => k
        .Value("Version", "2.0.0")
        .Value("InstallPath", MsiProperty.InstallFolder)));

p.Require(Condition.IsWindows10OrLater, "Acme Application requires Windows 10 or later.");
```

## How to Build

```
dotnet build demo/06-product-suite/app-installer/app-installer.csproj
```
