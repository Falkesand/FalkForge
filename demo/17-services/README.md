# Demo 17: Services

Installs Windows services with automatic startup, service accounts, dependencies, failure recovery actions, and
lifecycle control. Demonstrates both service group dependencies and custom credentials.

## What This Demonstrates

- Installing a Windows service with `package.Service()`
- Setting the startup mode, display name, description, and service account
- Declaring service dependencies on individual services (`DependsOn`) and service groups (`DependsOnGroup`)
- Configuring failure recovery actions (`FailureActions`) with restart, run-command, and reboot strategies
- Running a service under a custom user account with `UserName` and `Password`
- Controlling service lifecycle with `package.ServiceControl()` (start on install, stop on install/uninstall, delete on
  uninstall)
- Waiting for service state changes with `Wait(true)`
- Stopping a service during install with `StopOnInstall()`
- Passing startup arguments to a service with `Arguments()`

## Key API Calls

```csharp
// Define a service with dependencies and failure recovery
package.Service("DemoService", svc =>
{
    svc.DisplayName = "Demo Background Service";
    svc.Description = "Demonstrates FalkForge service installation";
    svc.Executable = @"[ProgramFilesFolder]Demo\ServiceDemo\myservice.exe";
    svc.StartMode = ServiceStartMode.Automatic;
    svc.Account = ServiceAccount.LocalService;

    // Service dependency — waits for Tcpip before starting
    svc.DependsOn("Tcpip");

    // Service group dependency — waits for all services in the group
    svc.DependsOnGroup("NetworkProvider");

    // Failure recovery actions
    svc.FailureActions(fa =>
    {
        fa.OnFirstFailure = FailureAction.Restart;
        fa.OnSecondFailure = FailureAction.Restart;
        fa.OnSubsequentFailures = FailureAction.None;
        fa.ResetPeriod = TimeSpan.FromDays(1);
        fa.RestartDelay = TimeSpan.FromSeconds(30);
    });
});

// Service running under a custom user account
package.Service("DemoWorker", svc =>
{
    svc.DisplayName = "Demo Worker Service";
    svc.Executable = @"[ProgramFilesFolder]Demo\ServiceDemo\myservice.exe";
    svc.StartMode = ServiceStartMode.Manual;
    svc.UserName = @".\DemoUser";
    svc.Password = "[DEMO_PASSWORD]";

    // Run-command failure action with reboot escalation
    svc.FailureActions(fa =>
    {
        fa.OnFirstFailure = FailureAction.RunCommand;
        fa.Command = @"[ProgramFilesFolder]Demo\ServiceDemo\myservice.exe --diagnose";
        fa.OnSecondFailure = FailureAction.Restart;
        fa.OnSubsequentFailures = FailureAction.Reboot;
        fa.RebootMessage = "Demo Worker service has failed repeatedly. Rebooting.";
    });
});

// Service lifecycle control — wait for state change, stop/start/delete
package.ServiceControl(sc =>
{
    sc.ServiceName("DemoService");
    sc.StopOnUninstall();
    sc.StartOnInstall();
    sc.DeleteOnUninstall();
    sc.Wait(true);
});

// Stop during install, pass arguments on start
package.ServiceControl(sc =>
{
    sc.ServiceName("DemoWorker");
    sc.StopOnInstall();
    sc.StartOnInstall();
    sc.Arguments("--config=[INSTALLDIR]config.json");
    sc.DeleteOnUninstall();
});
```

## How to Build

```bash
dotnet build demo/17-services
```

## Notes

- The `Executable` path uses MSI directory properties (e.g., `[ProgramFilesFolder]`) which resolve at install time.
- `DependsOn("Tcpip")` maps to the MSI `ServiceDependency` table. The service will not start until the specified
  dependency is running.
- `DependsOnGroup("NetworkProvider")` depends on an entire service group rather than a single service. The service waits
  until all group members have started.
- `FailureActions` configures the Windows Service Control Manager (SCM) recovery behavior. Actions escalate across
  first, second, and subsequent failures.
- `FailureAction.RunCommand` executes an arbitrary command on failure (e.g., a diagnostic tool). `RebootMessage` is
  displayed before a `FailureAction.Reboot`.
- `UserName` and `Password` run the service under a specific user account instead of a built-in service account.
- `ServiceControl` is separate from `Service` because it controls the service during install/uninstall transitions, not
  just its definition.
- `Wait(true)` tells MSI to wait for the service to reach the desired state (started/stopped) before continuing the
  install sequence.
- `StopOnInstall()` stops a running service during installation, useful for updating service binaries that are locked by
  a running process.
- `Arguments()` passes command-line arguments to the service when it is started by `ServiceControl`.
