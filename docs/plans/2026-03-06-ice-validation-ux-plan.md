# ICE Validation UX Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Expose the existing `IceValidator` to users via CLI flags, fluent API, and `forge validate --ice` with optional JSON report export.

**Architecture:** Add `IceConfiguration` record + builder to Core, extend `PackageModel`/`PackageBuilder`, add a config-aware `IceValidator.Validate()` overload with suppression and warning promotion, add `IceReportExporter` for JSON output, and wire CLI flags on `forge build` and `forge validate`.

**Tech Stack:** C# 13, .NET 10, xUnit, Spectre.Console, System.Text.Json (source-gen)

---

## Reference Files

- `src/FalkForge.Compiler.Msi/Validation/IceValidator.cs` — existing validator (239 lines)
- `src/FalkForge.Compiler.Msi/Validation/IceValidationResult.cs` — result model (28 lines)
- `src/FalkForge.Compiler.Msi/Validation/IceMessage.cs` — message model (11 lines)
- `src/FalkForge.Compiler.Msi/Validation/IceMessageSeverity.cs` — severity enum (9 lines)
- `src/FalkForge.Compiler.Msi/MsiCompiler.cs` — step 9 at lines 142-160
- `src/FalkForge.Core/Models/PackageModel.cs` — last property at line 59
- `src/FalkForge.Core/Builders/PackageBuilder.cs` — Build() at line 400
- `src/FalkForge.Cli/Settings/BuildSettings.cs` — 75 lines, Format at line 49
- `src/FalkForge.Cli/Settings/ValidateSettings.cs` — 33 lines
- `src/FalkForge.Cli/Commands/ValidateCommand.cs` — 71 lines

---

### Task 1: Core — IceConfiguration model and builder

**Files:**
- Create: `src/FalkForge.Core/Models/IceConfiguration.cs`
- Create: `src/FalkForge.Core/Builders/IceConfigurationBuilder.cs`
- Create: `tests/FalkForge.Core.Tests/IceConfigurationBuilderTests.cs`

**IceConfiguration.cs:**
```csharp
namespace FalkForge.Models;

public sealed record IceConfiguration
{
    public bool Enabled { get; init; } = true;
    public string? CubFilePath { get; init; }
    public IReadOnlyList<string> SuppressedIces { get; init; } = [];
    public bool WarningsAsErrors { get; init; }
    public string? ReportPath { get; init; }
}
```

**IceConfigurationBuilder.cs:**
```csharp
using FalkForge.Models;

namespace FalkForge.Builders;

public sealed class IceConfigurationBuilder
{
    private bool _enabled = true;
    private string? _cubFilePath;
    private readonly List<string> _suppressedIces = [];
    private bool _warningsAsErrors;
    private string? _reportPath;

    public IceConfigurationBuilder Disable()
    {
        _enabled = false;
        return this;
    }

    public IceConfigurationBuilder CubFilePath(string path)
    {
        _cubFilePath = path;
        return this;
    }

    public IceConfigurationBuilder Suppress(params string[] iceNames)
    {
        _suppressedIces.AddRange(iceNames);
        return this;
    }

    public IceConfigurationBuilder WarningsAsErrors(bool value = true)
    {
        _warningsAsErrors = value;
        return this;
    }

    public IceConfigurationBuilder ReportPath(string path)
    {
        _reportPath = path;
        return this;
    }

    public IceConfiguration Build() => new()
    {
        Enabled = _enabled,
        CubFilePath = _cubFilePath,
        SuppressedIces = [.. _suppressedIces],
        WarningsAsErrors = _warningsAsErrors,
        ReportPath = _reportPath
    };
}
```

**Tests (2):**

```csharp
using FalkForge.Builders;

namespace FalkForge.Tests;

public sealed class IceConfigurationBuilderTests
{
    [Fact]
    public void Build_Defaults_ReturnsEnabledWithNoSuppressions()
    {
        var config = new IceConfigurationBuilder().Build();

        Assert.True(config.Enabled);
        Assert.Null(config.CubFilePath);
        Assert.Empty(config.SuppressedIces);
        Assert.False(config.WarningsAsErrors);
        Assert.Null(config.ReportPath);
    }

    [Fact]
    public void Build_FluentApi_SetsAllProperties()
    {
        var config = new IceConfigurationBuilder()
            .Disable()
            .CubFilePath(@"C:\custom\darice.cub")
            .Suppress("ICE03", "ICE82")
            .WarningsAsErrors()
            .ReportPath("ice-report.json")
            .Build();

        Assert.False(config.Enabled);
        Assert.Equal(@"C:\custom\darice.cub", config.CubFilePath);
        Assert.Equal(["ICE03", "ICE82"], config.SuppressedIces);
        Assert.True(config.WarningsAsErrors);
        Assert.Equal("ice-report.json", config.ReportPath);
    }
}
```

**Verify:** `dotnet test <worktree>/tests/FalkForge.Core.Tests/FalkForge.Core.Tests.csproj --filter "FullyQualifiedName~IceConfigurationBuilder"`

**Commit:** `feat(core): add IceConfiguration model and builder`

---

### Task 2: Core — PackageModel and PackageBuilder integration

**Files:**
- Modify: `src/FalkForge.Core/Models/PackageModel.cs` — add IceConfiguration property
- Modify: `src/FalkForge.Core/Builders/PackageBuilder.cs` — add Ice() method and wire Build()
- Create: `tests/FalkForge.Core.Tests/PackageBuilderIceTests.cs`

**PackageModel.cs — add after line 59 (SbomOptions), before closing brace:**
```csharp
    public IceConfiguration? IceConfiguration { get; init; }
```

**PackageBuilder.cs — add field near top (with other fields):**
```csharp
    private IceConfiguration? _iceConfiguration;
```

**PackageBuilder.cs — add method (before Build()):**
```csharp
    public PackageBuilder Ice(Action<IceConfigurationBuilder> configure)
    {
        var builder = new IceConfigurationBuilder();
        configure(builder);
        _iceConfiguration = builder.Build();
        return this;
    }
```

**PackageBuilder.cs — in Build() object initializer, add:**
```csharp
        IceConfiguration = _iceConfiguration,
```

**Test (1):**

```csharp
using FalkForge.Builders;

namespace FalkForge.Tests;

public sealed class PackageBuilderIceTests
{
    [Fact]
    public void Ice_ConfiguresIceOnModel()
    {
        var model = new PackageBuilder()
            .Name("TestProduct")
            .Manufacturer("Test")
            .Version("1.0.0")
            .UpgradeCode(Guid.NewGuid())
            .Ice(ice => ice
                .Suppress("ICE03")
                .WarningsAsErrors())
            .Build();

        Assert.NotNull(model.IceConfiguration);
        Assert.True(model.IceConfiguration.WarningsAsErrors);
        Assert.Single(model.IceConfiguration.SuppressedIces);
        Assert.Equal("ICE03", model.IceConfiguration.SuppressedIces[0]);
    }
}
```

**Verify:** `dotnet test <worktree>/tests/FalkForge.Core.Tests/FalkForge.Core.Tests.csproj --filter "FullyQualifiedName~PackageBuilderIce"`

**Commit:** `feat(core): add Ice() configuration to PackageBuilder`

---

### Task 3: Compiler.Msi — IceValidator configuration overload

**Files:**
- Modify: `src/FalkForge.Compiler.Msi/Validation/IceValidator.cs` — add config-aware overload
- Create: `tests/FalkForge.Compiler.Msi.Tests/IceValidatorConfigTests.cs`

**IceValidator.cs — add new public method after line 38 (before ValidateWithCub):**

```csharp
    public Result<IceValidationResult> Validate(string msiPath, IceConfiguration config)
    {
        if (!config.Enabled)
            return Result<IceValidationResult>.Success(IceValidationResult.Success());

        var cubPath = config.CubFilePath ?? FindDariceCub();
        if (cubPath is null)
            return Result<IceValidationResult>.Success(IceValidationResult.Success());

        if (!File.Exists(msiPath))
            return Result<IceValidationResult>.Failure(ErrorKind.FileNotFound, $"MSI file not found: {msiPath}");

        if (!File.Exists(cubPath))
            return Result<IceValidationResult>.Failure(ErrorKind.FileNotFound, $"CUB file not found: {cubPath}");

        var result = ValidateWithCub(msiPath, cubPath);
        if (result.IsFailure)
            return result;

        var messages = result.Value.Messages.ToList();

        // Filter suppressed ICEs
        if (config.SuppressedIces.Count > 0)
            messages.RemoveAll(m => config.SuppressedIces.Contains(m.IceName, StringComparer.OrdinalIgnoreCase));

        // Promote warnings to errors if configured
        if (config.WarningsAsErrors)
        {
            messages = messages.Select(m => m.Severity == IceMessageSeverity.Warning
                ? new IceMessage
                {
                    IceName = m.IceName,
                    Severity = IceMessageSeverity.Error,
                    Description = m.Description,
                    Table = m.Table,
                    Column = m.Column,
                    PrimaryKeys = m.PrimaryKeys
                }
                : m).ToList();
        }

        return Result<IceValidationResult>.Success(IceValidationResult.FromMessages(messages));
    }
```

**Add using at top of IceValidator.cs:**
```csharp
using FalkForge.Models;
```

**Tests (3):**

```csharp
using FalkForge.Models;
using System.Runtime.Versioning;

namespace FalkForge.Compiler.Msi.Tests;

[SupportedOSPlatform("windows")]
public sealed class IceValidatorConfigTests
{
    [Fact]
    public void Validate_DisabledConfig_ReturnsSuccessWithoutRunning()
    {
        var validator = new IceValidator();
        var config = new IceConfiguration { Enabled = false };

        var result = validator.Validate("nonexistent.msi", config);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsValid);
        Assert.Empty(result.Value.Messages);
    }

    [Fact]
    public void Validate_CustomCubPath_NonExistent_ReturnsFailure()
    {
        var validator = new IceValidator();
        var config = new IceConfiguration { CubFilePath = @"C:\nonexistent\darice.cub" };

        // Create a temp MSI to pass the first check
        var tempMsi = Path.GetTempFileName();
        try
        {
            var result = validator.Validate(tempMsi, config);
            Assert.True(result.IsFailure);
            Assert.Equal(ErrorKind.FileNotFound, result.Error.Kind);
            Assert.Contains("CUB file not found", result.Error.Message);
        }
        finally
        {
            File.Delete(tempMsi);
        }
    }

    [Fact]
    public void Validate_NonExistentMsi_WithConfig_ReturnsFailure()
    {
        var validator = new IceValidator();
        var config = new IceConfiguration { CubFilePath = @"C:\some\darice.cub" };

        var result = validator.Validate("nonexistent.msi", config);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.FileNotFound, result.Error.Kind);
    }
}
```

**Verify:** `dotnet test <worktree>/tests/FalkForge.Compiler.Msi.Tests/FalkForge.Compiler.Msi.Tests.csproj --filter "FullyQualifiedName~IceValidatorConfig"`

**Commit:** `feat(compiler): add IceValidator overload with IceConfiguration support`

---

### Task 4: Compiler.Msi — IceReportExporter

**Files:**
- Create: `src/FalkForge.Compiler.Msi/Validation/IceReportExporter.cs`
- Create: `src/FalkForge.Compiler.Msi/Validation/IceReportJsonContext.cs`
- Create: `tests/FalkForge.Compiler.Msi.Tests/IceReportExporterTests.cs`

**IceReportExporter.cs:**
```csharp
using System.Text.Json;

namespace FalkForge.Compiler.Msi.Validation;

public static class IceReportExporter
{
    public static void Export(IceValidationResult result, string outputPath)
    {
        var report = new IceReport
        {
            IsValid = result.IsValid,
            Messages = result.Messages.Select(m => new IceReportMessage
            {
                IceName = m.IceName,
                Severity = m.Severity.ToString(),
                Description = m.Description,
                Table = m.Table,
                Column = m.Column,
                PrimaryKeys = m.PrimaryKeys
            }).ToList(),
            Summary = new IceReportSummary
            {
                Errors = result.Errors.Count,
                Warnings = result.Warnings.Count,
                Failures = result.Failures.Count,
                Information = result.Messages.Count(m => m.Severity == IceMessageSeverity.Information)
            }
        };

        var json = JsonSerializer.Serialize(report, IceReportJsonContext.Default.IceReport);
        File.WriteAllText(outputPath, json);
    }
}

public sealed class IceReport
{
    public bool IsValid { get; init; }
    public required List<IceReportMessage> Messages { get; init; }
    public required IceReportSummary Summary { get; init; }
}

public sealed class IceReportMessage
{
    public required string IceName { get; init; }
    public required string Severity { get; init; }
    public required string Description { get; init; }
    public string? Table { get; init; }
    public string? Column { get; init; }
    public string? PrimaryKeys { get; init; }
}

public sealed class IceReportSummary
{
    public int Errors { get; init; }
    public int Warnings { get; init; }
    public int Failures { get; init; }
    public int Information { get; init; }
}
```

**IceReportJsonContext.cs:**
```csharp
using System.Text.Json.Serialization;

namespace FalkForge.Compiler.Msi.Validation;

[JsonSerializable(typeof(IceReport))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class IceReportJsonContext : JsonSerializerContext;
```

**Tests (2):**

```csharp
using System.Text.Json;

namespace FalkForge.Compiler.Msi.Tests;

public sealed class IceReportExporterTests
{
    [Fact]
    public void Export_WritesValidJson()
    {
        var messages = new List<IceMessage>
        {
            new() { IceName = "ICE03", Severity = IceMessageSeverity.Warning, Description = "Test warning", Table = "File" },
            new() { IceName = "ICE33", Severity = IceMessageSeverity.Error, Description = "Test error" }
        };
        var result = IceValidationResult.FromMessages(messages);
        var path = Path.GetTempFileName();

        try
        {
            IceReportExporter.Export(result, path);

            var json = File.ReadAllText(path);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.False(root.GetProperty("isValid").GetBoolean());
            Assert.Equal(2, root.GetProperty("messages").GetArrayLength());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Export_IncludesCorrectSummary()
    {
        var messages = new List<IceMessage>
        {
            new() { IceName = "ICE01", Severity = IceMessageSeverity.Error, Description = "Error" },
            new() { IceName = "ICE02", Severity = IceMessageSeverity.Warning, Description = "Warn" },
            new() { IceName = "ICE03", Severity = IceMessageSeverity.Information, Description = "Info" }
        };
        var result = IceValidationResult.FromMessages(messages);
        var path = Path.GetTempFileName();

        try
        {
            IceReportExporter.Export(result, path);

            var json = File.ReadAllText(path);
            var doc = JsonDocument.Parse(json);
            var summary = doc.RootElement.GetProperty("summary");

            Assert.Equal(1, summary.GetProperty("errors").GetInt32());
            Assert.Equal(1, summary.GetProperty("warnings").GetInt32());
            Assert.Equal(0, summary.GetProperty("failures").GetInt32());
            Assert.Equal(1, summary.GetProperty("information").GetInt32());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
```

**Verify:** `dotnet test <worktree>/tests/FalkForge.Compiler.Msi.Tests/FalkForge.Compiler.Msi.Tests.csproj --filter "FullyQualifiedName~IceReportExporter"`

**Commit:** `feat(compiler): add IceReportExporter for JSON ICE report output`

---

### Task 5: Compiler.Msi — MsiCompiler step 9 integration

**Files:**
- Modify: `src/FalkForge.Compiler.Msi/MsiCompiler.cs` — update step 9 to use IceConfiguration

**MsiCompiler.cs — replace step 9 (lines 142-160) with:**

```csharp
        // Step 9: ICE validation
        var iceConfig = package.IceConfiguration ?? new IceConfiguration();
        if (iceConfig.Enabled && package.ReproducibleOptions is null)
        {
            var iceValidator = new IceValidator();
            var iceResult = iceValidator.Validate(msiPath, iceConfig);
            if (iceResult.IsFailure)
            {
                // ICE validation infrastructure failure is non-fatal
            }
            else
            {
                if (iceConfig.ReportPath is not null)
                    IceReportExporter.Export(iceResult.Value, iceConfig.ReportPath);

                if (iceResult.Value.Errors.Count > 0 || iceResult.Value.Failures.Count > 0)
                {
                    var iceErrors = string.Join("; ", iceResult.Value.Messages
                        .Where(m => m.Severity is IceMessageSeverity.Error or IceMessageSeverity.Failure)
                        .Select(m => $"{m.IceName}: {m.Description}"));
                    return Result<string>.Failure(ErrorKind.Validation, $"ICE validation failed: {iceErrors}");
                }
            }
        }
```

**Add using at top of MsiCompiler.cs (if not already present):**
```csharp
using FalkForge.Models;
```

**No new tests** — existing `MsiCompilerTests` + `IceValidatorTests` cover this path. The change is minimal (passing config instead of calling parameterless overload).

**Verify:** `dotnet build <worktree>/src/FalkForge.Compiler.Msi/FalkForge.Compiler.Msi.csproj`

**Commit:** `feat(compiler): wire IceConfiguration into MsiCompiler step 9`

---

### Task 6: CLI — BuildSettings ICE flags

**Files:**
- Modify: `src/FalkForge.Cli/Settings/BuildSettings.cs` — add 5 ICE flags
- Create: `tests/FalkForge.Cli.Tests/BuildSettingsIceTests.cs`

**BuildSettings.cs — add after Format property (line 49), before Validate():**

```csharp
    [CommandOption("--ice")]
    [Description("Enable ICE validation (default: enabled)")]
    public bool? Ice { get; init; }

    [CommandOption("--no-ice")]
    [Description("Disable ICE validation")]
    public bool NoIce { get; init; }

    [CommandOption("--ice-cub-path <PATH>")]
    [Description("Path to custom darice.cub file")]
    public string? IceCubPath { get; init; }

    [CommandOption("--suppress-ice <NAMES>")]
    [Description("Comma-separated ICE names to suppress (e.g., ICE03,ICE82)")]
    public string? SuppressIce { get; init; }

    [CommandOption("--ice-warnings-as-errors")]
    [Description("Treat ICE warnings as build errors")]
    public bool IceWarningsAsErrors { get; init; }

    [CommandOption("--ice-report <PATH>")]
    [Description("Export ICE validation results to JSON file")]
    public string? IceReport { get; init; }
```

**BuildSettings.cs — add validation in Validate() after format validation:**

```csharp
        if (IceCubPath is not null && !File.Exists(IceCubPath))
            return CliValidationResult.Error($"ICE CUB file not found: {IceCubPath}");
```

**Add a helper method to BuildSettings.cs to construct IceConfiguration from flags:**

```csharp
    public IceConfiguration BuildIceConfiguration() => new()
    {
        Enabled = NoIce ? false : Ice ?? true,
        CubFilePath = IceCubPath,
        SuppressedIces = SuppressIce?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [],
        WarningsAsErrors = IceWarningsAsErrors,
        ReportPath = IceReport
    };
```

**Add using:**
```csharp
using FalkForge.Models;
```

**Tests (2):**

```csharp
using FalkForge.Cli.Settings;

namespace FalkForge.Cli.Tests;

public sealed class BuildSettingsIceTests
{
    [Fact]
    public void BuildIceConfiguration_Defaults_ReturnsEnabled()
    {
        var settings = new BuildSettings { ProjectPath = "test.csx" };
        var config = settings.BuildIceConfiguration();

        Assert.True(config.Enabled);
        Assert.Null(config.CubFilePath);
        Assert.Empty(config.SuppressedIces);
        Assert.False(config.WarningsAsErrors);
    }

    [Fact]
    public void BuildIceConfiguration_AllFlags_SetsAll()
    {
        var settings = new BuildSettings
        {
            ProjectPath = "test.csx",
            NoIce = true,
            IceCubPath = null,
            SuppressIce = "ICE03,ICE82",
            IceWarningsAsErrors = true,
            IceReport = "report.json"
        };
        var config = settings.BuildIceConfiguration();

        Assert.False(config.Enabled);
        Assert.Equal(["ICE03", "ICE82"], config.SuppressedIces);
        Assert.True(config.WarningsAsErrors);
        Assert.Equal("report.json", config.ReportPath);
    }
}
```

**Verify:** `dotnet test <worktree>/tests/FalkForge.Cli.Tests/FalkForge.Cli.Tests.csproj --filter "FullyQualifiedName~BuildSettingsIce"`

**Commit:** `feat(cli): add ICE validation flags to BuildSettings`

---

### Task 7: CLI — ValidateSettings and ValidateCommand ICE support

**Files:**
- Modify: `src/FalkForge.Cli/Settings/ValidateSettings.cs` — add ICE flags
- Modify: `src/FalkForge.Cli/Commands/ValidateCommand.cs` — add ICE validation for .msi files
- Modify: `src/FalkForge.Cli/FalkForge.Cli.csproj` — add Compiler.Msi reference (if not already present, check TFM compatibility)
- Create: `tests/FalkForge.Cli.Tests/ValidateCommandIceTests.cs`

**ValidateSettings.cs — add after Verbose property:**

```csharp
    [CommandOption("--ice")]
    [Description("Run ICE validation on .msi files")]
    public bool Ice { get; init; }

    [CommandOption("--ice-cub-path <PATH>")]
    [Description("Path to custom darice.cub file")]
    public string? IceCubPath { get; init; }

    [CommandOption("--suppress-ice <NAMES>")]
    [Description("Comma-separated ICE names to suppress")]
    public string? SuppressIce { get; init; }

    [CommandOption("--ice-warnings-as-errors")]
    [Description("Treat ICE warnings as errors")]
    public bool IceWarningsAsErrors { get; init; }

    [CommandOption("--ice-report <PATH>")]
    [Description("Export ICE results to JSON file")]
    public string? IceReport { get; init; }
```

**ValidateCommand.cs — in Execute(), add .msi ICE handling before the existing script/JSON loading logic. After the file existence check (line 31), add:**

```csharp
        var extension = Path.GetExtension(settings.ProjectPath);
        if (extension.Equals(".msi", StringComparison.OrdinalIgnoreCase))
        {
            if (!settings.Ice)
            {
                _console.MarkupLine("[yellow]Use --ice flag to run ICE validation on .msi files.[/]");
                return ExitCodes.Success;
            }

            if (!OperatingSystem.IsWindows())
            {
                _console.WriteError("ICE validation requires Windows.");
                return ExitCodes.RuntimeError;
            }

            return RunIceValidation(settings);
        }
```

**ValidateCommand.cs — add private method:**

```csharp
    [SupportedOSPlatform("windows")]
    private int RunIceValidation(ValidateSettings settings)
    {
        var config = new IceConfiguration
        {
            Enabled = true,
            CubFilePath = settings.IceCubPath,
            SuppressedIces = settings.SuppressIce?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [],
            WarningsAsErrors = settings.IceWarningsAsErrors,
            ReportPath = settings.IceReport
        };

        var validator = new IceValidator();
        var result = validator.Validate(settings.ProjectPath, config);

        if (result.IsFailure)
        {
            _console.WriteError($"ICE validation failed: {result.Error.Message}");
            return ExitCodes.RuntimeError;
        }

        var iceResult = result.Value;

        if (iceResult.Messages.Count == 0)
        {
            _console.MarkupLine("[green]ICE validation passed with no issues.[/]");
            return ExitCodes.Success;
        }

        // Display results as table
        var table = new Table();
        table.AddColumn("ICE");
        table.AddColumn("Severity");
        table.AddColumn("Description");
        table.AddColumn("Table");

        foreach (var msg in iceResult.Messages)
        {
            var severityColor = msg.Severity switch
            {
                IceMessageSeverity.Failure => "red",
                IceMessageSeverity.Error => "red",
                IceMessageSeverity.Warning => "yellow",
                _ => "grey"
            };

            table.AddRow(
                Markup.Escape(msg.IceName),
                $"[{severityColor}]{msg.Severity}[/]",
                Markup.Escape(msg.Description),
                Markup.Escape(msg.Table ?? ""));
        }

        _console.Write(table);

        var errorCount = iceResult.Errors.Count + iceResult.Failures.Count;
        var warnCount = iceResult.Warnings.Count;
        _console.MarkupLine(iceResult.IsValid
            ? $"[green]{iceResult.Messages.Count} issues ({warnCount} warnings). Validation PASSED.[/]"
            : $"[red]{iceResult.Messages.Count} issues ({errorCount} errors, {warnCount} warnings). Validation FAILED.[/]");

        return iceResult.IsValid ? ExitCodes.Success : ExitCodes.ValidationFailure;
    }
```

**Add usings to ValidateCommand.cs:**
```csharp
using FalkForge.Compiler.Msi.Validation;
using FalkForge.Models;
using System.Runtime.Versioning;
using Spectre.Console;
```

**NOTE:** The CLI project targets `net10.0` while `Compiler.Msi` targets `net10.0-windows`. Read the Cli.csproj to check if it already references Compiler.Msi (it should from the MSIX integration). If not, the ValidateCommand may need conditional compilation or the Cli.csproj may need to target `net10.0-windows`. READ the csproj first to determine the right approach.

**Tests (2):**

```csharp
namespace FalkForge.Cli.Tests;

public sealed class ValidateCommandIceTests
{
    [Fact]
    public void Execute_MsiWithoutIceFlag_ShowsHint()
    {
        var console = new TestConsoleOutput();
        var command = new ValidateCommand(console);

        // Create a temp .msi file (doesn't need to be valid)
        var tempMsi = Path.ChangeExtension(Path.GetTempFileName(), ".msi");
        File.WriteAllBytes(tempMsi, [0]);
        try
        {
            var settings = new ValidateSettings { ProjectPath = tempMsi, Ice = false };
            var result = command.Execute(null!, settings);

            Assert.Equal(ExitCodes.Success, result);
            Assert.Contains("--ice", console.Output);
        }
        finally
        {
            File.Delete(tempMsi);
        }
    }

    [Fact]
    public void Execute_CsxIgnoresIceFlag()
    {
        var console = new TestConsoleOutput();
        var command = new ValidateCommand(console);

        var tempCsx = Path.ChangeExtension(Path.GetTempFileName(), ".csx");
        File.WriteAllText(tempCsx, "// empty script");
        try
        {
            var settings = new ValidateSettings { ProjectPath = tempCsx, Ice = true };
            // Should proceed to normal validation, not ICE
            var result = command.Execute(null!, settings);
            // Any result is fine — just verify it didn't crash trying to ICE-validate a script
        }
        finally
        {
            File.Delete(tempCsx);
        }
    }
}
```

**NOTE:** You may need to adjust test construction based on how `TestConsoleOutput` works. Read existing `ValidateCommandTests.cs` for the pattern.

**Verify:** `dotnet test <worktree>/tests/FalkForge.Cli.Tests/FalkForge.Cli.Tests.csproj --filter "FullyQualifiedName~ValidateCommandIce"`

**Commit:** `feat(cli): add ICE validation support to forge validate command`

---

### Task 8: Full Verification

1. `dotnet build <worktree>/FalkForge.slnx` — 0 new errors (pre-existing Engine errors acceptable)
2. `dotnet test <worktree>/tests/FalkForge.Core.Tests/` — all pass
3. `dotnet test <worktree>/tests/FalkForge.Compiler.Msi.Tests/` — all pass
4. `dotnet test <worktree>/tests/FalkForge.Cli.Tests/` — all pass
5. Verify `forge validate MyApp.msi --ice` shows table output (if test MSI available)
6. Verify `forge build test.csx --no-ice` skips ICE

---

## Integration Map

```
Core:
  IceConfiguration (record)
  IceConfigurationBuilder (fluent)
  PackageModel.IceConfiguration (property)
  PackageBuilder.Ice() (method)

Compiler.Msi:
  IceValidator.Validate(path, config) (new overload)
  IceReportExporter.Export() (JSON)
  MsiCompiler step 9 reads IceConfiguration

CLI:
  forge build --ice/--no-ice/--suppress-ice/--ice-warnings-as-errors/--ice-report
  forge validate myapp.msi --ice [--ice-report report.json]
```
