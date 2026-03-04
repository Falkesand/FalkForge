# Demo 18: Environment Variables

Sets system-level environment variables during installation, including creating a new variable and appending to the
system PATH.

## What This Demonstrates

- Creating a new system environment variable with `EnvironmentVariableAction.Set`
- Appending a value to an existing environment variable with `EnvironmentVariableAction.Append`
- Using a custom separator for appended values
- System-level variable targeting (`IsSystem = true`)
- User-scoped variable targeting (`IsSystem = false`)

## Key API Calls

```csharp
// Create a new system variable
package.EnvironmentVariable("DEMO_HOME", @"[ProgramFilesFolder]Demo\EnvVarDemo", env =>
{
    env.IsSystem = true;
    env.Action = EnvironmentVariableAction.Set;
});

// Append to the system PATH
package.EnvironmentVariable("PATH", @"[ProgramFilesFolder]Demo\EnvVarDemo", env =>
{
    env.IsSystem = true;
    env.Action = EnvironmentVariableAction.Append;
    env.Separator = ";";
});

// User-scoped variable (not system-wide)
package.EnvironmentVariable("DEMO_USER_PREF", "enabled", ev =>
{
    ev.IsSystem = false;
    ev.Action = EnvironmentVariableAction.Set;
});
```

## How to Build

```bash
dotnet build demo/18-environment-variables
```

## Notes

- `EnvironmentVariableAction.Set` replaces the variable value entirely. `Append` adds to the existing value using the
  specified separator.
- MSI directory properties like `[ProgramFilesFolder]` are resolved at install time. The environment variable will
  contain the actual resolved path.
- On uninstall, MSI automatically removes the variables it created and restores any values it appended to.
- Setting `IsSystem = false` writes the variable to the current user's environment (`HKCU\Environment`) instead of the
  machine-wide location (`HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment`).
