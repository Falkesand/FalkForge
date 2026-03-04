# Demo 28: Sequence Scheduling

Controls the ordering of actions in the MSI execute sequence. Demonstrates scheduling a custom action to run after a specific standard action with a conditional guard.

## What This Demonstrates

- Defining a custom action and scheduling it in the execute sequence
- Using `package.ExecuteSequence()` to position an action relative to standard MSI actions
- Using `package.UISequence()` to schedule an action in the UI sequence
- Applying a condition so the action only runs during installation (not repair or uninstall)

## Key API Calls

```csharp
// Define a custom action
package.CustomAction("PostInstallCleanup", ca =>
{
    ca.SetProperty("CLEANUP_FLAG", "1");
});

// Schedule it in the execute sequence after InstallFinalize
package.ExecuteSequence(seq => seq
    .Action("PostInstallCleanup")
    .After("InstallFinalize")
    .Condition(Condition.IsInstalling));

// Schedule an action in the UI sequence (runs during user interaction phase)
package.UISequence(seq => seq
    .Action("PostInstallCleanup")
    .After("ExecuteAction")
    .Condition(Condition.IsInstalling));
```

## How to Build

```bash
dotnet build demo/28-sequence-scheduling
```

## Notes

- `After("InstallFinalize")` places the action at the very end of the execute sequence, after all files are committed.
- `Condition.IsInstalling` ensures the action only runs during a first install or upgrade, not during repair or uninstall.
- The execute sequence is the elevated phase of MSI installation. Actions here run with system privileges if the installer is elevated.
- `package.UISequence()` schedules actions in the UI sequence, which runs during the user interaction phase before the execute sequence begins. The UI sequence runs with the invoking user's privileges, not elevated.
- The same action can be scheduled in both the execute and UI sequences with different positioning and conditions.
