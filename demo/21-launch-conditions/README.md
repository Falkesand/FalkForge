# Demo 21: Launch Conditions

Blocks installation unless prerequisite conditions are met. This demo requires both administrator privileges and Windows 10 or later.

## What This Demonstrates

- Using `package.Require()` to enforce installation prerequisites
- Built-in condition constants (`Condition.IsPrivileged`, `Condition.IsWindows10OrLater`)
- Custom error messages displayed when a condition fails

## Key API Calls

```csharp
// Require administrator privileges
package.Require(Condition.IsPrivileged, "This application requires administrator privileges.");

// Require Windows 10 or later
package.Require(Condition.IsWindows10OrLater, "This application requires Windows 10 or later.");
```

## How to Build

```bash
dotnet build demo/21-launch-conditions
```

## Notes

- Launch conditions are evaluated early in the install sequence. If any condition fails, the installer displays the error message and exits.
- The `Condition` class provides common built-in conditions. Custom conditions can be written using MSI property expressions.
- Multiple `Require()` calls are all evaluated; every condition must pass for installation to proceed.
