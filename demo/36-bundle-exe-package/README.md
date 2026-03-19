# Demo 36: EXE Package in Bundle

Bundles an EXE prerequisite (such as the Visual C++ Redistributable) alongside an MSI application package, with exit
code mapping to control how the bootstrapper interprets the EXE installer's return values.

## What This Demonstrates

- Adding an EXE package as a prerequisite in the install chain
- Mapping specific exit codes to bootstrapper behaviors (success, reboot scheduling)
- Ordering packages so prerequisites install before the main application
- Mixing EXE and MSI package types in a single bundle

## Key API Calls

| Method                                             | Purpose                                                  |
|----------------------------------------------------|----------------------------------------------------------|
| `chain.ExePackage(path, config)`                   | Add an EXE-based installer to the chain                  |
| `.ExitCode(0, ExitCodeBehavior.Success)`           | Treat exit code 0 as successful completion               |
| `.ExitCode(3010, ExitCodeBehavior.ScheduleReboot)` | Treat exit code 3010 as success with a pending reboot    |
| `.ExitCode(1638, ExitCodeBehavior.Success)`        | Treat "another version already installed" as success     |
| `.Vital(true)`                                     | Failure of this package aborts the entire bundle install |

## How to Build

```bash
dotnet build demo/36-bundle-exe-package/36-bundle-exe-package.csproj
```

## Notes

- Exit code 3010 is the standard Windows Installer code for "reboot required." Mapping it to `ScheduleReboot` defers the
  reboot until all packages finish.
- Exit code 1638 means "another version of this product is already installed." Treating it as success prevents the
  bundle from failing when the prerequisite is already present.
- The EXE package is listed first in the chain so it installs before the MSI that depends on it.
