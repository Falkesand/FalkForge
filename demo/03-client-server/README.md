# Demo 03: Client-Server

A multi-component installer with a FeatureTree dialog that lets the user choose which components to install: a desktop client, a Windows service, and documentation.

## What This Demonstrates

- `MsiDialogSet.FeatureTree` for user-selectable feature installation
- Multiple independent `Feature()` definitions (Client, Server, Documentation)
- Windows service installation with `Service()`: `StartMode`, `Account`, `Arguments`
- Config file deployment with `NeverOverwrite()` and `Permanent()` to preserve user edits across upgrades
- Shortcuts on Desktop and Start Menu with `OnDesktop()` and `OnStartMenu()`
- Registry entries referencing `MsiProperty.InstallFolder`
- System environment variables with `EnvironmentVariableAction.Set`
- Major upgrade with `AllowSameVersionUpgrades()`
- Launch condition using `Condition.IsWindows10OrLater`

## Key API Calls

```csharp
// Feature tree dialog
p.UseDialogSet(MsiDialogSet.FeatureTree);

// Feature definition
p.Feature("Server", f =>
{
    f.Title = "Server Application";
    f.IsDefault = true;
    f.IsRequired = false;
});

// Windows service
p.Service("AcmeServer", svc =>
{
    svc.DisplayName = "Acme Server";
    svc.Executable = "server.exe";
    svc.StartMode = ServiceStartMode.Automatic;
    svc.Account = ServiceAccount.LocalSystem;
    svc.Arguments = "--port=8080 --config=appsettings.json";
});

// Preserve config on upgrade
p.Files(f => f
    .Add("payload/server/appsettings.json")
    .NeverOverwrite()
    .Permanent()
    .To(KnownFolder.ProgramFiles / "Acme Corporation" / "ClientServer" / "Server"));

// Registry with MsiProperty reference
p.Registry(r => r
    .Key(RegistryRoot.LocalMachine, @"Software\Acme\ClientServer\Server", k => k
        .Value("InstallPath", MsiProperty.InstallFolder)));

// System environment variable
p.EnvironmentVariable("ACME_SERVER_PORT", "8080", ev =>
{
    ev.IsSystem = true;
    ev.Action = EnvironmentVariableAction.Set;
});

// Major upgrade + launch condition
p.MajorUpgrade(mu => mu.AllowSameVersionUpgrades());
p.Require(Condition.IsWindows10OrLater, "This application requires Windows 10 or later.");
```

## How to Build

```bash
dotnet build demo/03-client-server/
```

## How to Run

Produces a `.msi` file. Requires Windows with `msi.dll`.

```bash
dotnet run --project demo/03-client-server/ -- -o ./output
```

## Notes

- `NeverOverwrite()` prevents the installer from overwriting an existing file during upgrade, preserving user configuration changes.
- `Permanent()` leaves the file on disk when the feature is uninstalled, useful for config files that may contain user data.
- `MsiProperty.InstallFolder` is a type-safe reference to the `[INSTALLFOLDER]` MSI property.
