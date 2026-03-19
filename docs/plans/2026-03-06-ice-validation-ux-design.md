# ICE Validation UX Design

## Overview

Expose FalkForge's existing `IceValidator` to users via CLI flags, fluent API configuration, and `forge validate --ice` command with optional JSON report export.

## Architecture

**Components:**

1. **IceConfiguration** (Core) — immutable config record: enabled flag, custom CUB path, suppressed ICE list, warnings-as-errors flag, report path
2. **IceConfigurationBuilder** (Core) — fluent builder for IceConfiguration
3. **PackageBuilder.Ice()** — fluent method storing IceConfiguration on PackageModel
4. **IceValidator overload** (Compiler.Msi) — accepts IceConfiguration for suppression, CUB path, warning promotion
5. **IceReportExporter** (Compiler.Msi) — JSON serializer for IceValidationResult
6. **CLI flags** — `--ice`, `--no-ice`, `--ice-cub-path`, `--suppress-ice`, `--ice-warnings-as-errors`, `--ice-report` on `forge build` and `forge validate`

**Data flow:**

```
User -> CLI flags / fluent API -> IceConfiguration -> MsiCompiler -> IceValidator -> IceValidationResult -> Console + optional JSON report
```

No new projects. Changes span Core (config model + builder), Compiler.Msi (validator + exporter), and Cli (flags + output).

## IceConfiguration Model & Builder

```csharp
// src/FalkForge.Core/Models/IceConfiguration.cs
public sealed record IceConfiguration
{
    public bool Enabled { get; init; } = true;
    public string? CubFilePath { get; init; }
    public IReadOnlyList<string> SuppressedIces { get; init; } = [];
    public bool WarningsAsErrors { get; init; }
    public string? ReportPath { get; init; }
}
```

```csharp
// src/FalkForge.Core/Builders/IceConfigurationBuilder.cs
public sealed class IceConfigurationBuilder
{
    public IceConfigurationBuilder Disable();
    public IceConfigurationBuilder CubFilePath(string path);
    public IceConfigurationBuilder Suppress(params string[] iceNames);
    public IceConfigurationBuilder WarningsAsErrors(bool value = true);
    public IceConfigurationBuilder ReportPath(string path);
    public IceConfiguration Build();
}
```

**PackageBuilder integration:**

```csharp
public PackageBuilder Ice(Action<IceConfigurationBuilder> configure)
```

Stores `IceConfiguration` on `PackageModel` as a new property:

```csharp
public IceConfiguration? IceConfiguration { get; init; }
```

## IceValidator Changes & Report Export

New overload accepting configuration:

```csharp
public Result<IceValidationResult> Validate(string msiPath, IceConfiguration config)
```

Behavior:
- Uses `config.CubFilePath` if set, otherwise auto-discovers darice.cub
- Filters out messages where `IceName` is in `config.SuppressedIces`
- If `config.WarningsAsErrors`, promotes Warning severity to Error before building the result

**MsiCompiler step 9** reads `IceConfiguration` from the model:

```csharp
var iceConfig = model.IceConfiguration ?? new IceConfiguration();
if (!iceConfig.Enabled) { /* skip ICE */ }
else { var result = _iceValidator.Validate(msiPath, iceConfig); }
```

After validation, export if configured:

```csharp
if (iceConfig.ReportPath is not null)
    IceReportExporter.Export(result, iceConfig.ReportPath);
```

**IceReportExporter** — JSON serializer using source-generated `JsonSerializerContext`:

```json
{
  "isValid": true,
  "messages": [
    { "iceName": "ICE03", "severity": "Warning", "description": "...", "table": "File", "column": "Version" }
  ],
  "summary": { "errors": 0, "warnings": 1, "failures": 0, "information": 2 }
}
```

## CLI Integration

**BuildSettings + ValidateSettings** — 5 new flags each:

```
--ice / --no-ice              Enable/disable ICE validation (default: enabled)
--ice-cub-path <path>         Path to custom darice.cub file
--suppress-ice <ICE03,ICE82>  Comma-separated ICE names to suppress
--ice-warnings-as-errors      Treat ICE warnings as errors
--ice-report <path.json>      Export ICE results to JSON file
```

**ValidateCommand** extended:

- Input `.msi` + `--ice` flag: run `IceValidator.Validate()` directly on the MSI
- Input `.csx`/`.json`: existing model validation (unchanged)
- Display results as Spectre.Console table (ICE name, severity color-coded, description, table/column)
- Return `ExitCodes.Validation` (1) if ICE errors/failures found

**Console output:**

```
ICE Validation Results for MyApp.msi
+--------+----------+-----------------------------+-------+
| ICE    | Severity | Description                 | Table |
+--------+----------+-----------------------------+-------+
| ICE03  | Warning  | String overflow in column... | File  |
| ICE33  | Error    | Missing entry in Registry...| Reg   |
+--------+----------+-----------------------------+-------+
2 issues (1 error, 1 warning). Validation FAILED.
```

## Testing Strategy

**Compiler.Msi.Tests (6 tests):**
1. IceValidator_WithConfiguration_UsesCubPath
2. IceValidator_SuppressedIces_FiltersMessages
3. IceValidator_WarningsAsErrors_PromotesWarnings
4. IceValidator_Disabled_ReturnsSuccess
5. IceReportExporter_WritesValidJson
6. IceReportExporter_IncludesSummary

**Core.Tests (3 tests):**
7. IceConfigurationBuilder_Defaults
8. IceConfigurationBuilder_FluentApi
9. PackageBuilder_Ice_SetsConfiguration

**Cli.Tests (3 tests):**
10. BuildSettings_IceFlags_Validate
11. ValidateCommand_MsiWithIce_RunsIceValidator
12. ValidateCommand_CsxIgnoresIce

No integration tests needed — existing IceValidatorTests cover real ICE execution. New tests mock the validator to test configuration plumbing.

## Scope

- **Global suppression only** — no per-package ICE suppression
- **JSON report only** — no XML export
- **Console + file** — no other report formats
