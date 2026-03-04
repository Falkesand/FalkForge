# Per-MSI Internal Progress Reporting — Design

## Context

The FalkForge engine currently reports progress at package-level granularity only. For a 5-package bundle, the progress bar jumps 0→20→40→60→80→100%. Each jump blocks while the MSI installs (potentially minutes). This design adds real-time per-MSI progress so the bar moves smoothly during each package's installation.

## Goals

- Smooth progress bar: continuous 0–100% across all packages
- Developer controls display text per package (one string for entire package install)
- Backwards compatible: existing UIs and executors unaffected if they don't opt in

## Architecture

### InstallProgress Enhancement

Add `PackagePercent` field to the existing progress struct:

```csharp
public readonly record struct InstallProgress(
    int Current,           // package index (1-based)
    int Total,             // total packages
    string CurrentPackage, // package ID
    int PackagePercent);   // 0-100 within current package (NEW, default 0)
```

UI calculates overall percent as:
```
overall = ((Current - 1) * 100 + PackagePercent) / Total
```

For 5 packages, package 3 at 50% internal = (2*100 + 50) / 5 = 50% overall.

### P/Invoke: MsiSetExternalUIW

Add to `NativeMethods.Msi`:

```csharp
[LibraryImport("msi.dll", StringMarshalling = StringMarshalling.Utf16)]
internal static partial nint MsiSetExternalUIW(
    MsiInstallUIHandler handler,
    uint messageFilter,
    nint context);

[UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
internal delegate int MsiInstallUIHandler(
    nint context,
    uint messageType,
    string message);
```

Filter for `CYCLEINSTALLACTIONPROGRESS` messages (message type flags) that contain progress tick data.

### MsiExecutor Changes

`ExecuteDirect()` registers the external UI handler before calling `MsiInstallProductW()`:

1. Create a managed callback that parses MSI progress messages
2. Pin the delegate with GCHandle to prevent collection during native call
3. Register with `MsiSetExternalUIW()`
4. Call `MsiInstallProductW()` (blocks, but callback fires during execution)
5. Unregister handler and free GCHandle after install completes
6. Report progress via `IProgress<int>` parameter (0–100%)

### Executor Interface

Add optional progress parameter:

```csharp
Task<Result<int>> ExecuteAsync(
    PlanAction action,
    CancellationToken ct,
    IProgress<int>? packageProgress = null);
```

Only `MsiExecutor` reports ticks. Other executors (`ExeExecutor`, `MsuExecutor`, `BundleExecutor`) ignore the parameter. Backwards compatible — default null means no per-package progress.

### ApplyingHandler

Creates `IProgress<int>` per package that:
1. Maps executor's 0–100% to a `ProgressMessage` with `PackagePercent`
2. Sends via UI pipe
3. Throttles to max 1 update per 100ms to avoid flooding

### Elevated Path

The elevation service's `MsiInstallCommand` also registers `MsiSetExternalUIW`. Progress ticks are sent back through the elevation pipe as a lightweight message, forwarded by the engine to the UI pipe.

### Developer API (Display Text)

Developers set display text at bundle build time via `BundlePackageBuilder.DisplayName()`:

```csharp
.MsiPackage(path, p => p
    .DisplayName("Installing MultiAccess v8.9.0")
    .Vital(true))
```

This string is stored in `PackageInfo.DisplayName` in the manifest. The UI page maps `CurrentPackage` (package ID) to a localized display string. The engine only reports IDs and percentages — never auto-generated action text.

### UI Changes

`InstallProgressPage` changes percent calculation:
```csharp
var overall = progress.Total > 0
    ? ((progress.Current - 1) * 100 + progress.PackagePercent) / progress.Total
    : 0;
ProgressPercent = Math.Clamp(overall, 0, 100);
```

Status text unchanged — uses localized per-package string mapped from package ID.

### Backwards Compatibility

- `PackagePercent` defaults to 0. Old engines or executors that don't report it still work — the UI falls back to package-level granularity
- Executor interface has default `null` for progress parameter
- Existing pipe protocol: `ProgressMessage` already carries `InstallProgress` — adding a field to the record struct is wire-compatible if using source-generated JSON with default values

## Components Changed

| Component | File | Change |
|-----------|------|--------|
| Protocol | `Engine.Protocol/InstallProgress.cs` | Add `PackagePercent` field |
| P/Invoke | `Platform.Windows/NativeMethods.Msi.cs` | Add `MsiSetExternalUIW`, delegate, constants |
| MSI Executor | `Engine/Execution/MsiExecutor.cs` | Register UI handler, report progress via `IProgress<int>` |
| Package Executor | `Engine/Execution/PackageExecutor.cs` | Pass `IProgress<int>` to inner executor |
| Applying Handler | `Engine/Phases/ApplyingHandler.cs` | Create progress wrapper, throttle, send `ProgressMessage` |
| Elevation Command | `Engine.Elevation/Commands/MsiInstallCommand.cs` | Register UI handler, send progress back |
| UI Client | `Ui/EngineClient.cs` | Pass `PackagePercent` through observable |
| MAS Progress Page | `demo/MAS/Pages/InstallProgressPage.cs` | New percent formula |

## Verification

1. `dotnet build D:/Git/FalkInstaller/FalkForge.slnx` — 0 errors
2. `dotnet test D:/Git/FalkInstaller/FalkForge.slnx` — all pass
3. Manual: install a multi-package bundle and observe smooth progress bar
4. Manual: verify display text stays constant per package (no flickering action names)
5. Backwards compat: old UI still works with new engine (ignores PackagePercent)
