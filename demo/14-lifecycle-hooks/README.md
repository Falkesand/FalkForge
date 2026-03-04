# Demo 14: Lifecycle Hooks

A teaching example that demonstrates all engine lifecycle hooks available in the FalkForge.Ui framework. The installer
collects database configuration from the user, passes properties to MSI packages (including secure password transport),
and logs every lifecycle phase to a visible status log.

## What This Demonstrates

- All five lifecycle hook pairs: Detect, Plan, Apply (begin/complete)
- Passing user-collected configuration to MSI properties via `Engine.SetProperty`
- Secure property passing via `Engine.SetSecureProperty` using named pipes (never on command line)
- Sensitive data handling with `GetPassword` and `SharedState.SetSensitive`
- `SharedState` for passing data between pages (Config page stores, Progress page reads)
- Page validation with `PageResult.Stay(errorMessage)` for required fields
- Conditional UI visibility using `SetField` with dependent property notifications
- Real-time status logging visible to the user during installation

## Key API Calls

```csharp
// Lifecycle hooks (override in any InstallerPage)
protected override Task<bool> OnDetectBeginAsync()       // Return false to cancel
protected override Task OnDetectCompleteAsync(DetectResult result)
protected override Task<bool> OnPlanBeginAsync(InstallAction action)
protected override Task OnPlanCompleteAsync(PlanResult result)
protected override Task<bool> OnApplyBeginAsync()
protected override Task OnApplyCompleteAsync(ApplyResult result)

// Pass properties to MSI packages during Plan phase
Engine.SetProperty("DBSERVER", dbServer);
Engine.SetProperty("DBNAME", dbName);

// Secure property -- transmitted via named pipe, not command line
using var pw = SharedState.GetSensitive("DbPassword");
Engine.SetSecureProperty("DBPASSWORD", pw);

// Collect password securely from UI
using var pw = GetPassword("DbPassword");
if (!pw.IsEmpty)
    SharedState.SetSensitive("DbPassword", pw.Span);

// Cross-page data sharing
SharedState.Set("DbServer", _dbServer);
var dbServer = SharedState.Get<string>("DbServer");

// Dependent property notification
SetField(ref _integratedSecurity, value, [nameof(ShowCredentials)]);
```

## How to Build

```
dotnet build demo/14-lifecycle-hooks/14-lifecycle-hooks.csproj
```

## Notes

- The `OnDetectBeginAsync`, `OnPlanBeginAsync`, and `OnApplyBeginAsync` hooks return `Task<bool>`. Returning `false`
  cancels that phase.
- `DetectResult.State` reports `Installed`, `OlderVersion`, `NewerVersion`, or `NotInstalled`, allowing the UI to adapt.
- `PlanResult.PackageActions` and `PlanResult.TotalDiskSpaceRequired` provide pre-install summary data.
- `ApplyResult.ExitCode` and `ApplyResult.ErrorMessage` provide post-install diagnostics.
- `Engine.SetSecureProperty` uses named pipe transport so passwords never appear in process command lines or logs. This
  is critical for passing credentials to MSI custom actions.
- `SharedState.SetSensitive` stores data in protected memory. The corresponding `GetSensitive` returns a
  `ReadOnlyMemory<char>` that should be disposed after use.
