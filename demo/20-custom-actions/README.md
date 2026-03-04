# Demo 20: Custom Actions

Demonstrates multiple types of MSI custom actions: DLL-based, EXE-based, property-setting, deferred, rollback, commit, and error-tolerant actions.

## What This Demonstrates

- Embedding a binary DLL with `package.Binary()` for use in custom actions
- `DllFromBinary()` custom actions that call a C entry point in an embedded DLL
- `ExeFromBinary()` custom actions that run an embedded executable
- `SetProperty` custom actions that run during the UI sequence
- Deferred custom actions that run elevated during the execute sequence
- `NoImpersonate()` to run under the system account instead of the user
- Rollback custom actions that execute only when the installation fails
- `Commit()` actions that run only on successful install completion
- `ContinueOnError()` for non-critical actions that should not abort the install
- Scheduling actions relative to standard MSI actions using `After` and `Before`

## Key API Calls

```csharp
// Embed a DLL binary for use in custom actions
package.Binary("CustomActionsDll", "payload/CustomActions.dll");

// DLL-based custom action — calls a C entry point in the embedded DLL
package.CustomAction("CheckSystemRequirements", ca =>
{
    ca.DllFromBinary("CustomActionsDll", "CheckRequirements");
    ca.After = "CostFinalize";
    ca.Condition = Condition.IsInstalling.ToString();
});

// EXE-based custom action — runs an embedded executable with arguments
package.CustomAction("RunSetupTool", ca =>
{
    ca.ExeFromBinary("CustomActionsDll");
    ca.Target = "--setup --silent";
    ca.Deferred();
    ca.NoImpersonate();
    ca.After = "InstallFiles";
});

// SetProperty action — runs during UI sequence
package.CustomAction("SetInstallMode", ca =>
{
    ca.SetProperty("INSTALL_MODE", "standard");
    ca.Condition = Condition.IsInstalling.ToString();
});

// Deferred action — runs elevated after InstallFiles
package.CustomAction("ConfigureApp", ca =>
{
    ca.SetProperty("CONFIGURE_CMD", @"[ProgramFilesFolder]Demo\CustomActionDemo\app.exe --configure");
    ca.Deferred();
    ca.NoImpersonate();
    ca.After = "InstallFiles";
});

// Rollback action — undoes ConfigureApp if install fails
package.CustomAction("UndoConfigureApp", ca =>
{
    ca.SetProperty("UNDO_CMD", @"[ProgramFilesFolder]Demo\CustomActionDemo\app.exe --unconfigure");
    ca.Rollback();
    ca.NoImpersonate();
    ca.Before = "ConfigureApp";
});

// Commit action — runs only on successful install completion
package.CustomAction("NotifySuccess", ca =>
{
    ca.SetProperty("NOTIFY_CMD", @"[ProgramFilesFolder]Demo\CustomActionDemo\app.exe --notify-complete");
    ca.Commit();
    ca.NoImpersonate();
    ca.After = "ConfigureApp";
});

// ContinueOnError — installer proceeds even if this action fails
package.CustomAction("OptionalTelemetry", ca =>
{
    ca.SetProperty("TELEMETRY_CMD", @"[ProgramFilesFolder]Demo\CustomActionDemo\app.exe --telemetry");
    ca.Deferred();
    ca.ContinueOnError();
    ca.After = "NotifySuccess";
});
```

## How to Build

```bash
dotnet build demo/20-custom-actions
```

## Notes

- `package.Binary()` embeds a file (DLL, EXE) into the MSI's `Binary` table. The embedded binary is referenced by name in `DllFromBinary` and `ExeFromBinary`.
- `DllFromBinary()` calls a named C export function in an embedded DLL. This is the MSI Type 1 custom action.
- `ExeFromBinary()` runs an embedded executable. Command-line arguments are passed via the `Target` property. This is the MSI Type 2 custom action.
- Deferred actions cannot read MSI properties directly. Use `SetProperty` to pass data into the deferred action's `CustomActionData`.
- Rollback actions must be scheduled **before** the action they undo. MSI executes them in reverse order during rollback.
- `Commit()` actions only execute after the entire install sequence completes successfully. If any action fails and triggers rollback, commit actions are skipped.
- `ContinueOnError()` marks an action as non-critical. If it fails, the installer logs the error but continues instead of aborting.
- `NoImpersonate()` is required when the custom action needs elevated privileges (e.g., writing to protected directories).
