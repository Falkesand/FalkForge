# Demo 14: Lifecycle Hooks

A teaching example that demonstrates all engine lifecycle hooks available in the FalkForge.Ui framework. The installer
collects database configuration from the user, passes properties to MSI packages (including secure password transport),
and logs every lifecycle phase to a visible status log.

> **Integration status:** running this project directly is a UI preview. The lifecycle hooks need a real engine session to execute. `UseCustomUI(projectPath)` does not build or embed the project yet.

## What This Demonstrates

- All three phase-level lifecycle hook pairs: Detect, Plan, Apply (begin/complete)
- The per-package / per-related-bundle granular hooks (WiX Burn bootstrapper granularity),
  logged once per package as it is detected, planned, and applied
- Passing user-collected configuration to MSI properties via `Engine.SetProperty`
- Secure property passing via `Engine.SetSecureProperty` using named pipes (never on command line)
- Sensitive data handling with `GetPassword` and `SharedState.SetSensitive`
- `SharedState` for passing data between pages (Config page stores, Progress page reads)
- Page validation with `PageResult.Stay(errorMessage)` for required fields
- Conditional UI visibility using `SetField` with dependent property notifications
- Real-time status logging visible to the user during installation

## Key API Calls

```csharp
// Phase-level lifecycle hooks (override in any InstallerPage).
// The three *Begin hooks return Task<bool>; returning false cancels the operation.
protected override Task<bool> OnDetectBeginAsync()       // Return false to cancel
protected override Task OnDetectCompleteAsync(DetectResult result)
protected override Task<bool> OnPlanBeginAsync(InstallAction action)
protected override Task OnPlanCompleteAsync(PlanResult result)
protected override Task<bool> OnApplyBeginAsync()
protected override Task OnApplyCompleteAsync(ApplyResult result)

// Per-package / per-related-bundle granular hooks (observational, cannot veto a package).
// Fire interleaved inside the matching phase, once per package/bundle, in chain order.
protected override Task OnDetectPackageCompleteAsync(PackageDetectInfo info)
protected override Task OnDetectRelatedBundleAsync(RelatedBundleInfo info)
protected override Task OnPlanPackageBeginAsync(PackagePlanInfo info)
protected override Task OnPlanPackageCompleteAsync(PackagePlanInfo info)
protected override Task OnApplyPackageBeginAsync(PackageApplyBeginInfo info)
protected override Task OnApplyPackageCompleteAsync(PackageApplyCompleteInfo info)

// Documented fire order:
//   OnDetectBeginAsync
//     -> (per package) OnDetectPackageCompleteAsync
//     -> (per related bundle) OnDetectRelatedBundleAsync
//   OnDetectCompleteAsync
//   OnPlanBeginAsync
//     -> (per package) OnPlanPackageBeginAsync -> OnPlanPackageCompleteAsync
//   OnPlanCompleteAsync
//   OnApplyBeginAsync
//     -> (per package) OnApplyPackageBeginAsync -> OnApplyPackageCompleteAsync
//   OnApplyCompleteAsync

// Pass properties to MSI packages during Plan phase
Engine.SetProperty("DBSERVER", dbServer);
Engine.SetProperty("DBNAME", dbName);

// Secure property -- transmitted via named pipe, not command line
using var pw = SharedState.GetSensitive("DbPassword");
Engine.SetSecureProperty("DBPASSWORD", pw);

// The bundle that hosts this UI must declare every name the UI sets, on every per-machine MSI
// package in the bundle -- the engine presents every property to every package's plan action, not
// only to the package it was meant for. The elevated install refuses any other name, on the command
// line and on the secure channel alike, so DBPASSWORD is declared here as well:
//   chain.MsiPackage("App.msi", p => p
//       .Id("App")
//       .AllowElevatedProperty("DBSERVER", "DBNAME", "INTEGRATEDSECURITY", "DBUSERNAME", "DBPASSWORD"));

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
- `Engine.SetSecureProperty` sends the value over the named pipe and the elevated companion writes it into a
  transform it generates itself, so the password never appears in a process command line or an MSI log. The
  name still has to be on the package's signed allowlist (`AllowElevatedProperty`); the secure channel changes
  where the value travels, not which names the publisher permits.
- `SharedState.SetSensitive` stores data in protected memory. The corresponding `GetSensitive` returns a
  `ReadOnlyMemory<char>` that should be disposed after use.
