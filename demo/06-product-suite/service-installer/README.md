# Demo 06: Product Suite -- Service Installer

The background service MSI package for the Acme Product Suite. Installs a Windows Service with automatic start, sets a system environment variable, and uses minimal UI since services do not require interactive directory selection.

## What This Demonstrates

- Windows Service installation via `p.Service()` with start mode, account, and description
- `MsiDialogSet.Minimal` for non-interactive service installers
- `InstallScope.PerMachine` for system-wide installation
- System environment variable configuration via `p.EnvironmentVariable()`
- `ServiceStartMode.Automatic` and `ServiceAccount.LocalService` for least-privilege service execution

## Key API Calls

```csharp
p.Scope = InstallScope.PerMachine;
p.UseDialogSet(MsiDialogSet.Minimal);

p.Service("AcmeService", svc =>
{
    svc.DisplayName = "Acme Background Service";
    svc.Description = "Acme data processing and synchronization service";
    svc.Executable = "acmeservice.exe";
    svc.StartMode = ServiceStartMode.Automatic;
    svc.Account = ServiceAccount.LocalService;
});

p.EnvironmentVariable("ACME_SERVICE_PORT", "8080", ev =>
{
    ev.IsSystem = true;
    ev.Action = EnvironmentVariableAction.Set;
});
```

## How to Build

```
dotnet build demo/06-product-suite/service-installer/service-installer.csproj
```
