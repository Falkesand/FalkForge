# Demo 47: PowerShell Custom Actions

Demonstrates PowerShell-based MSI custom actions using inline scripts and file-based scripts with deferred execution
and rollback support.

## What This Demonstrates

- `PowerShellScript()` for inline PowerShell custom actions
- `PowerShellFile()` for file-based PowerShell scripts (reads and embeds the content)
- Deferred PowerShell actions with `NoImpersonate()` for elevated execution
- Rollback PowerShell actions that clean up on installation failure
- Scheduling PowerShell actions relative to standard MSI actions using `After` and `Before`

## Key API Calls

```csharp
// Inline PowerShell script — runs during UI sequence
package.CustomAction("LogInstallStart", ca =>
{
    ca.PowerShellScript("Write-EventLog -LogName Application -Source 'MSIInstaller' -EventId 1000 -Message 'installation started'");
    ca.After = "CostFinalize";
    ca.Condition = Condition.IsInstalling.ToString();
});

// File-based PowerShell script — reads setup.ps1 and embeds inline
package.CustomAction("RunSetupScript", ca =>
{
    ca.PowerShellFile("payload/setup.ps1");
    ca.Deferred();
    ca.NoImpersonate();
    ca.After = "InstallFiles";
});

// Rollback action — cleans up if install fails
package.CustomAction("UndoConfigureSettings", ca =>
{
    ca.PowerShellScript("Remove-Item ... -Force");
    ca.Rollback();
    ca.NoImpersonate();
    ca.Before = "ConfigureSettings";
});
```

## How to Build

```bash
dotnet build demo/47-powershell-actions
```

## Notes

- `PowerShellScript()` creates an ExeInDir (type 34) custom action targeting `powershell.exe` in `[SystemFolder]`.
- `PowerShellFile()` reads the .ps1 file at build time and embeds the content inline via `PowerShellScript()`.
- The `-NoProfile -NonInteractive -ExecutionPolicy Bypass` flags are automatically applied to ensure reliable execution
  in the MSI context.
- Deferred actions run during the execute sequence with SYSTEM privileges when `NoImpersonate()` is set.
- Always pair deferred actions with rollback actions to ensure clean uninstall on failure.
