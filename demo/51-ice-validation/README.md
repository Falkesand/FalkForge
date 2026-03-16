# Demo 51: ICE Validation

Configures MSI Internal Consistency Evaluators (ICE) to validate the installer package during compilation, with rule
suppression, warnings-as-errors mode, and JSON report generation.

## What This Demonstrates

- `package.Ice()` to configure ICE validation settings
- `Suppress()` to skip specific ICE rules that don't apply
- `WarningsAsErrors()` for strict validation — any ICE warning fails the build
- `ReportPath()` for JSON report output suitable for CI/CD pipelines

## Key API Calls

```csharp
package.Ice(ice =>
{
    // Suppress rules that don't apply to this package
    ice.Suppress("ICE61", "ICE91");

    // Treat warnings as errors for strict compliance
    ice.WarningsAsErrors();

    // Write JSON report for CI/CD integration
    ice.ReportPath("output/ice-report.json");
});
```

## How to Build

```bash
dotnet build demo/51-ice-validation
```

## Notes

- ICE validation runs the standard Microsoft validation rules (ICE01-ICE99+) against the compiled MSI database.
- `Suppress()` accepts multiple ICE names and prevents those specific rules from running.
- `WarningsAsErrors()` causes the build to fail if any ICE rule reports a warning (not just errors).
- The JSON report includes the ICE rule name, severity, description, and affected table/row for each finding.
- Use `CubFilePath()` to specify a custom .cub validation database instead of the default `darice.cub`.
- ICE validation requires Windows with `msi.dll` available.
