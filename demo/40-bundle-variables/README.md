# Demo 40: Bundle Variables

Defines custom variables in the bundle with various visibility and persistence controls, and uses them as install conditions to control which packages are installed. Demonstrates secret, hidden, and persisted variable modifiers.

## What This Demonstrates

- Declaring custom bundle variables with type and default value
- `Persisted()` variables that survive bundle repair/modify sessions
- `Hidden()` variables that are excluded from install logs
- `Secret()` variables that are excluded from both logs and persisted state
- Using variables as install conditions on packages
- Conditional package installation (packages only install when their condition evaluates to true)
- Mixing mandatory and optional packages in the same chain

## Key API Calls

| Method | Purpose |
|--------|---------|
| `.Variable(name, config)` | Declare a custom bundle variable |
| `v.Numeric()` | Set the variable type to numeric |
| `v.String()` | Set the variable type to string |
| `v.Default(string)` | Set the default value for the variable |
| `v.Persisted()` | Persist the variable across repair/modify sessions |
| `v.Hidden()` | Exclude the variable value from install logs |
| `v.Secret()` | Exclude from logs and persisted state (implies Hidden) |
| `.InstallCondition(string)` | Condition expression that must be true for the package to install |
| `.Vital(false)` | Mark the package as optional; failure does not abort the bundle |

## How to Build

```bash
dotnet build demo/40-bundle-variables/40-bundle-variables.csproj
```

## Notes

- The condition `"InstallOptionalTools = 1"` is evaluated at runtime. The variable can be set by the UI or via command-line arguments (e.g., `Setup.exe InstallOptionalTools=1`).
- The core application has no install condition and always installs.
- Optional packages should be marked `Vital(false)` so their failure does not block the rest of the install.
- `Persisted()` variables retain their value across bundle repair and modify operations. Use for paths or settings the user chose during initial install.
- `Hidden()` prevents the variable value from appearing in install logs. Use for sensitive but non-secret data like license keys.
- `Secret()` is the strongest protection: the value is excluded from both logs and the persisted variable store. It implies `Hidden()`. Use for passwords and credentials that must not be stored on disk.
