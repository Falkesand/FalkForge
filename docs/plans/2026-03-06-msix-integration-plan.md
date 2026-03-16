# MSIX Integration Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task.

**Goal:** Wire the new `FalkForge.Compiler.Msix` project into Core API, CLI, SDK, Studio, and add a demo — making MSIX a first-class output format.

**Architecture:** Add `Installer.BuildMsix()` / `Installer.BuildMsixBundle()` entry points following existing patterns (BuildMergeModule/BuildPatch). Extend CLI with `--format msix` option. Add MSIX routing to Studio build service. Add SDK targets for Msix/MsixBundle output types.

**Tech Stack:** C# 13, .NET 10, Spectre.Console CLI, WPF (Studio), MSBuild SDK targets

---

## Reference Patterns

- **Core entry point:** `src/FalkForge.Core/Installer.cs` — `BuildMergeModule()` (lines 78-102) for builder+validate+compile pattern
- **CLI settings:** `src/FalkForge.Cli/Settings/BuildSettings.cs` — Spectre.Console CommandSettings
- **CLI build:** `src/FalkForge.Cli/Commands/BuildCommand.cs` — Execute() method
- **CLI decompile:** `src/FalkForge.Cli/Commands/DecompileCommand.cs` — extension routing
- **SDK targets:** `src/FalkForge.Sdk/Sdk/Sdk.targets` — `_ComputeFalkArtifactPath`
- **Studio build:** `src/FalkForge.Studio/Project/StudioBuildService.cs` — `Compile()` at line 229
- **Studio tree:** `src/FalkForge.Studio/Shell/StudioViewModel.cs` — `BuildDefaultTree()` at line 63

---

### Task 1: Core API — `Installer.BuildMsix()` and `Installer.BuildMsixBundle()`

**Files:**
- Modify: `src/FalkForge.Core/Installer.cs` — add two new methods
- Modify: `src/FalkForge.Core/FalkForge.Core.csproj` — add ProjectReference to Compiler.Msix
- Create: `tests/FalkForge.Core.Tests/InstallerMsixTests.cs`

**Core.csproj change — add project reference:**
```xml
<ProjectReference Include="..\FalkForge.Compiler.Msix\FalkForge.Compiler.Msix.csproj" />
```

**Installer.cs — add after BuildTransform() (line 178), before GetOutputPath():**

```csharp
/// <summary>
///     Builds an MSIX package.
///     Configures the MSIX model via a fluent builder, validates it,
///     and passes the model and output path to the compile function.
/// </summary>
/// <param name="args">Command-line arguments (supports -o/--output for output path).</param>
/// <param name="configure">Action to configure the MSIX builder.</param>
/// <param name="compile">
///     A function that receives the MSIX model and output path,
///     and returns the created .msix file path on success.
/// </param>
/// <returns>Exit code: 0 for success, 1 for failure.</returns>
public static int BuildMsix(string[] args, Action<MsixBuilder> configure,
    Func<MsixModel, string, Result<string>> compile)
{
    var builder = new MsixBuilder();
    configure(builder);
    var model = builder.Build();

    var validation = MsixValidator.Validate(model);
    if (validation.IsFailure)
    {
        Console.Error.WriteLine($"MSIX validation failed: {validation.Error}");
        return 1;
    }

    var outputPath = GetOutputPath(args);
    var result = compile(model, outputPath);
    if (result.IsFailure)
    {
        Console.Error.WriteLine($"MSIX compilation failed: {result.Error}");
        return 1;
    }

    Console.WriteLine($"MSIX package created: {result.Value}");
    return 0;
}

/// <summary>
///     Builds an MSIX bundle (.msixbundle) containing multiple architecture-specific packages.
/// </summary>
/// <param name="args">Command-line arguments (supports -o/--output for output path).</param>
/// <param name="configure">Action to configure the MSIX bundle builder.</param>
/// <param name="compile">
///     A function that receives the bundle model and output path,
///     and returns the created .msixbundle file path on success.
/// </param>
/// <returns>Exit code: 0 for success, 1 for failure.</returns>
public static int BuildMsixBundle(string[] args, Action<MsixBundleBuilder> configure,
    Func<MsixBundleModel, string, Result<string>> compile)
{
    var builder = new MsixBundleBuilder();
    configure(builder);
    var model = builder.Build();

    var outputPath = GetOutputPath(args);
    var result = compile(model, outputPath);
    if (result.IsFailure)
    {
        Console.Error.WriteLine($"MSIX bundle compilation failed: {result.Error}");
        return 1;
    }

    Console.WriteLine($"MSIX bundle created: {result.Value}");
    return 0;
}
```

**Required usings at top of Installer.cs:**
```csharp
using FalkForge.Compiler.Msix;
using FalkForge.Compiler.Msix.Builders;
```

**Tests (4):**
1. `BuildMsix_ValidModel_ReturnsZero` — configure valid model, mock compile returns success → exit 0
2. `BuildMsix_ValidationFails_ReturnsOne` — empty name → exit 1
3. `BuildMsix_CompileFails_ReturnsOne` — compile returns failure → exit 1
4. `BuildMsixBundle_ValidModel_ReturnsZero` — configure valid bundle, mock compile → exit 0

For tests, use a mock compile function `(model, path) => Result<string>.Success("test.msix")`.

**Verify:** `dotnet build D:/Git/FalkInstaller/src/FalkForge.Core/FalkForge.Core.csproj` — 0 errors. `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Core.Tests/` — all pass.

---

### Task 2: CLI — Add `--format` option to BuildSettings

**Files:**
- Modify: `src/FalkForge.Cli/Settings/BuildSettings.cs` — add Format property
- Create: `tests/FalkForge.Cli.Tests/BuildSettingsFormatTests.cs`

**BuildSettings.cs — add after WinGetInstallerUrl (line 45):**
```csharp
[CommandOption("--format")]
[Description("Output format: msi (default), msix, bundle, msm, msp, mst")]
public string? Format { get; init; }
```

**BuildSettings.cs — update Validate() to accept format values (after line 60):**
```csharp
if (Format is not null)
{
    var validFormats = new[] { "msi", "msix", "bundle", "msm", "msp", "mst" };
    if (!validFormats.Contains(Format, StringComparer.OrdinalIgnoreCase))
        return CliValidationResult.Error($"Invalid format '{Format}'. Valid formats: {string.Join(", ", validFormats)}");
}
```

**Tests (3):**
1. `Validate_FormatMsix_Succeeds` — Format = "msix" passes validation
2. `Validate_FormatInvalid_Fails` — Format = "xyz" fails validation
3. `Validate_FormatNull_Succeeds` — null format (default) passes

**Verify:** `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Cli.Tests/` — all pass.

---

### Task 3: CLI — Route MSIX format in BuildCommand

**Files:**
- Modify: `src/FalkForge.Cli/Commands/BuildCommand.cs` — add MSIX routing
- Modify: `src/FalkForge.Cli/FalkForge.Cli.csproj` — add Compiler.Msix reference
- Create: `tests/FalkForge.Cli.Tests/BuildCommandMsixTests.cs`

**Cli.csproj — add project reference:**
```xml
<ProjectReference Include="..\FalkForge.Compiler.Msix\FalkForge.Compiler.Msix.csproj" />
```

**BuildCommand.cs — add using:**
```csharp
using FalkForge.Compiler.Msix;
```

**BuildCommand.cs — in Execute(), after `_console.MarkupLine($"[green]Loaded JSON config:` (line 65-67), replace the JSON "not yet supported" block with format-aware routing:**

Replace lines 65-67:
```csharp
_console.MarkupLine($"[green]Loaded JSON config:[/] {Markup.Escape(package.Name)} v{package.Version}");
_console.MarkupLine("[yellow]MSI compilation from JSON is not yet supported.[/]");
return ExitCodes.Success;
```

With:
```csharp
_console.MarkupLine($"[green]Loaded JSON config:[/] {Markup.Escape(package.Name)} v{package.Version}");

if (string.Equals(settings.Format, "msix", StringComparison.OrdinalIgnoreCase))
{
    _console.MarkupLine("[yellow]MSIX compilation from JSON is not yet supported.[/]");
    return ExitCodes.Success;
}

_console.MarkupLine("[yellow]MSI compilation from JSON is not yet supported.[/]");
return ExitCodes.Success;
```

**BuildCommand.cs — in the .cs script path (after line 82), add format check before the existing script loading:**

After the existing `loadResult` success check (line 84), add MSIX format handling. Insert before line 70:
```csharp
if (string.Equals(settings.Format, "msix", StringComparison.OrdinalIgnoreCase))
{
    if (!OperatingSystem.IsWindows())
    {
        _console.WriteError("MSIX compilation requires Windows.");
        return ExitCodes.RuntimeError;
    }

    _console.MarkupLine("[yellow]MSIX compilation from .cs scripts requires calling Installer.BuildMsix() in the script.[/]");
    _console.MarkupLine("[grey]Use --format msi (default) for MSI output.[/]");
}
```

**Tests (2):**
1. `Execute_FormatMsix_NonWindows_ReturnsError` — mock non-Windows → RuntimeError (may need to skip on Windows)
2. `Execute_FormatMsix_ShowsMessage` — verify informational message is output

**Note:** Full MSIX-from-script compilation requires the script to call `Installer.BuildMsix()` directly. The CLI just needs to not block it. The `--format msix` flag serves as documentation and future routing point.

**Verify:** `dotnet build D:/Git/FalkInstaller/src/FalkForge.Cli/FalkForge.Cli.csproj` — 0 errors. `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Cli.Tests/` — all pass.

---

### Task 4: CLI — Add `.msix` and `.msixbundle` to DecompileCommand

**Files:**
- Modify: `src/FalkForge.Cli/Commands/DecompileCommand.cs` — add extension routing

**DecompileCommand.cs — add after line 42 (the .exe check), before line 44:**
```csharp
if (extension.Equals(".msix", StringComparison.OrdinalIgnoreCase) ||
    extension.Equals(".msixbundle", StringComparison.OrdinalIgnoreCase))
{
    _console.WriteError("MSIX decompilation is not yet supported.");
    return ExitCodes.RuntimeError;
}
```

**DecompileCommand.cs — update the error message on line 44:**
```csharp
_console.WriteError($"Unsupported file extension '{extension}'. Expected .msi, .exe, .msix, or .msixbundle.");
```

**Tests (1):**
1. `Execute_MsixFile_ReturnsNotSupported` — .msix file → informative error, not "unsupported format"

**Verify:** `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Cli.Tests/` — all pass.

---

### Task 5: SDK Targets — Add Msix and MsixBundle output types

**Files:**
- Modify: `src/FalkForge.Sdk/Sdk/Sdk.targets` — add two lines

**Sdk.targets — add after line 17 (CustomAction), before `</PropertyGroup>`:**
```xml
      <FalkArtifactPath Condition="'$(FalkOutputType)' == 'Msix'">$(TargetDir)$(AssemblyName).msix</FalkArtifactPath>
      <FalkArtifactPath Condition="'$(FalkOutputType)' == 'MsixBundle'">$(TargetDir)$(AssemblyName).msixbundle</FalkArtifactPath>
```

**No tests needed** — MSBuild targets, verified by build.

**Verify:** `dotnet build D:/Git/FalkInstaller/src/FalkForge.Sdk/FalkForge.Sdk.csproj` — 0 errors.

---

### Task 6: Studio — Add MSIX project type routing

**Files:**
- Modify: `src/FalkForge.Studio/Project/StudioBuildService.cs` — add MSIX compile routing
- Modify: `src/FalkForge.Studio/Shell/StudioViewModel.cs` — add MSIX tree nodes
- Modify: `src/FalkForge.Studio/FalkForge.Studio.csproj` — add Compiler.Msix reference
- Create: `tests/FalkForge.Studio.Tests/StudioBuildServiceMsixTests.cs`

**Studio.csproj — add project reference:**
```xml
<ProjectReference Include="..\FalkForge.Compiler.Msix\FalkForge.Compiler.Msix.csproj" />
```

**StudioBuildService.cs — update Compile() method (line 229-241). Replace with routing:**
```csharp
public static Result<string> Compile(StudioProject project, string baseDirectory)
{
    var outputPath = Path.IsPathRooted(project.Build.OutputPath)
        ? project.Build.OutputPath
        : Path.Combine(baseDirectory, project.Build.OutputPath);

    return project.ProjectType?.ToLowerInvariant() switch
    {
        "msix" => CompileMsix(project, baseDirectory, outputPath),
        "bundle" => CompileBundle(project, baseDirectory, outputPath),
        _ => CompileMsi(project, baseDirectory, outputPath)
    };
}

private static Result<string> CompileMsi(StudioProject project, string baseDirectory, string outputPath)
{
    var modelResult = BuildModel(project, baseDirectory);
    if (modelResult.IsFailure)
        return Result<string>.Failure(modelResult.Error);

    var compiler = new MsiCompiler();
    return compiler.Compile(modelResult.Value, outputPath);
}

private static Result<string> CompileBundle(StudioProject project, string baseDirectory, string outputPath)
{
    var modelResult = BuildBundleModel(project, baseDirectory);
    if (modelResult.IsFailure)
        return Result<string>.Failure(modelResult.Error);

    var compiler = new BundleCompiler();
    return compiler.Compile(modelResult.Value, outputPath);
}

private static Result<string> CompileMsix(StudioProject project, string baseDirectory, string outputPath)
{
    // Minimal MSIX compilation stub — validates project type is recognized
    // Full MSIX Studio editing will come in a future Studio update
    return Result<string>.Failure(ErrorKind.NotSupported,
        "MSIX compilation from Studio is not yet supported. Use the C# API or CLI instead.");
}
```

**Add using:**
```csharp
using FalkForge.Compiler.Msix;
```

**StudioViewModel.cs — update BuildDefaultTree() (line 84-88). Replace:**
```csharp
if (_project.ProjectType == "bundle")
{
    TreeNodes.Add(new TreeNodeViewModel("Bundle Settings", "bundleSettings"));
    TreeNodes.Add(new TreeNodeViewModel("Bundle Packages", "bundlePackages"));
}
```

With:
```csharp
switch (_project.ProjectType)
{
    case "bundle":
        TreeNodes.Add(new TreeNodeViewModel("Bundle Settings", "bundleSettings"));
        TreeNodes.Add(new TreeNodeViewModel("Bundle Packages", "bundlePackages"));
        break;
    case "msix":
        TreeNodes.Add(new TreeNodeViewModel("MSIX Applications", "msixApplications"));
        TreeNodes.Add(new TreeNodeViewModel("Capabilities", "msixCapabilities"));
        break;
}
```

**Tests (2):**
1. `Compile_MsixProjectType_ReturnsNotSupported` — project type "msix" → NotSupported result
2. `Compile_MsiProjectType_StillWorks` — default project type → delegates to MSI (regression)

**Verify:** `dotnet build D:/Git/FalkInstaller/src/FalkForge.Studio/FalkForge.Studio.csproj` — 0 errors. `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Studio.Tests/` — all pass.

---

### Task 7: Demo — Minimal MSIX project

**Files:**
- Create: `demo/15-msix-basic/msix-basic.csx`
- Create: `demo/15-msix-basic/README.md`
- Create: `demo/15-msix-basic/payload/hello.exe` (placeholder — 0-byte file)
- Create: `demo/15-msix-basic/assets/Square44x44Logo.png` (placeholder — 0-byte file)
- Create: `demo/15-msix-basic/assets/Square150x150Logo.png` (placeholder — 0-byte file)

**msix-basic.csx:**
```csharp
#r "nuget: FalkForge.Core"
#r "nuget: FalkForge.Compiler.Msix"

using FalkForge;
using FalkForge.Compiler.Msix;
using FalkForge.Compiler.Msix.Builders;

Installer.BuildMsix(Args.ToArray(), msix =>
{
    msix
        .Name("FalkForge.Demo.MsixBasic")
        .Publisher("CN=FalkForge Demo")
        .DisplayName("MSIX Basic Demo")
        .PublisherDisplayName("FalkForge")
        .Version(new Version(1, 0, 0, 0))
        .Architecture(ProcessorArchitecture.X64)
        .Application("App", "hello.exe", app => app
            .DisplayName("Hello World")
            .Square44x44Logo("assets/Square44x44Logo.png")
            .Square150x150Logo("assets/Square150x150Logo.png"))
        .Capability("internetClient")
        .MinWindowsVersion("10.0.17763.0")
        .Signing(s => s.CertificatePath("demo-cert.pfx"));
}, (model, outputPath) =>
{
    var compiler = new MsixCompiler();
    return compiler.Compile(model, outputPath);
});
```

**README.md:**
```markdown
# Demo 15: MSIX Basic

Minimal MSIX package using the FalkForge MSIX compiler.

## Prerequisites

- Windows 10 1809+ (MSIX packaging APIs)
- Code signing certificate (PFX)

## Build

```bash
forge build msix-basic.csx --format msix -o ./output
```

## What This Shows

- `MsixBuilder` fluent API
- Single application entry point
- Capability declaration
- Signing configuration
```

**No tests** — demo project, verified by inspection.

**Verify:** Files exist and are syntactically valid.

---

### Task 8: Full Verification

1. `dotnet build D:/Git/FalkInstaller/FalkForge.slnx` — 0 errors (excluding pre-existing Engine errors)
2. `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Core.Tests/` — all pass
3. `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Cli.Tests/` — all pass
4. `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Studio.Tests/` — all pass
5. `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Compiler.Msix.Tests/` — 79 pass
6. Verify demo files exist
7. Verify `Installer.BuildMsix` is callable from a script

---

## Final Integration Map

```
Core: Installer.BuildMsix() / BuildMsixBundle()
 └→ FalkForge.Compiler.Msix (project reference)

CLI: forge build --format msix
 └→ BuildCommand routes to MSIX
 └→ DecompileCommand recognizes .msix/.msixbundle

SDK: FalkOutputType=Msix / MsixBundle
 └→ Sdk.targets computes artifact path

Studio: ProjectType="msix"
 └→ StudioBuildService.CompileMsix() (stub)
 └→ StudioViewModel shows MSIX tree nodes

Demo: demo/15-msix-basic/
 └→ Working example of builder API
```
