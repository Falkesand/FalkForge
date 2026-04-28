# Extract Command Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add `forge extract` CLI command and bundle `--extract` self-extraction flag for extracting MSI cabinet files and bundle payloads without installing.

**Architecture:** New `ExtractCommand` in CLI follows existing command patterns (Spectre.Console.Cli). MSI extraction orchestrated by new `MsiExtractor` class combining `MsiDatabase` + `DirectoryResolver` + `CabinetExtractor`. Bundle extraction uses existing `BundleReader`. Engine gets early-exit `--extract`/`--extract-list` handling in Program.cs following the `--sbom` pattern.

**Tech Stack:** C#, Spectre.Console.Cli, MsiDatabase P/Invoke, CabinetExtractor (FDI P/Invoke), BundleReader, xUnit.

---

### Task 1: Create ExtractSettings

**Files:**
- Create: `src/FalkForge.Cli/Settings/ExtractSettings.cs`

**Step 1: Write the settings class**

Follow the pattern from `DecompileSettings.cs`. The settings class defines CLI arguments and options.

```csharp
using System.ComponentModel;
using Spectre.Console.Cli;
using CliValidationResult = Spectre.Console.ValidationResult;

namespace FalkForge.Cli.Settings;

public sealed class ExtractSettings : CommandSettings
{
    [Description("Path to the file to extract (.msi, .msm, or .exe bundle)")]
    [CommandArgument(0, "<file>")]
    public string FilePath { get; init; } = string.Empty;

    [Description("Output directory for extracted files")]
    [CommandOption("-o|--output")]
    public string? OutputPath { get; init; }

    [Description("List packages without extracting (bundles only)")]
    [CommandOption("--list")]
    public bool ListOnly { get; init; }

    [Description("Extract specific package(s) by PackageId (repeatable, bundles only)")]
    [CommandOption("--package")]
    public string[]? Packages { get; init; }

    public override CliValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(FilePath))
            return CliValidationResult.Error("File path is required.");

        if (FilePath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return CliValidationResult.Error("File path contains invalid characters.");

        var ext = Path.GetExtension(FilePath);
        if (!ext.Equals(".msi", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".msm", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".exe", StringComparison.OrdinalIgnoreCase))
            return CliValidationResult.Error("File must be an .msi, .msm, or .exe file.");

        if (!ListOnly && string.IsNullOrWhiteSpace(OutputPath))
            return CliValidationResult.Error("Output directory (-o) is required unless using --list.");

        if (OutputPath is not null && OutputPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return CliValidationResult.Error("Output path contains invalid characters.");

        if (Packages is { Length: > 0 } && !ext.Equals(".exe", StringComparison.OrdinalIgnoreCase))
            return CliValidationResult.Error("--package is only valid for .exe bundles.");

        return CliValidationResult.Success();
    }
}
```

**Step 2: Build**

Run: `dotnet build D:/Git/FalkInstaller/src/FalkForge.Cli/FalkForge.Cli.csproj`
Expected: 0 errors, 0 warnings.

**Step 3: Commit**

```
feat(cli): add ExtractSettings for forge extract command
```

---

### Task 2: Create MsiExtractor

**Files:**
- Create: `src/FalkForge.Cli/MsiExtractor.cs`

**Step 1: Write tests**

Create `tests/FalkForge.Cli.Tests/MsiExtractorTests.cs`. The test should build a demo MSI, then extract it and verify the files are present with correct directory structure.

Use the demo E2E pattern — run `demo/01-hello-world` to produce an MSI, then extract it.

```csharp
using System.Runtime.Versioning;

namespace FalkForge.Cli.Tests;

[SupportedOSPlatform("windows")]
public sealed class MsiExtractorTests : IDisposable
{
    private readonly string _tempDir;

    public MsiExtractorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"msi-extract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void Extract_ProducesFiles()
    {
        // Build demo 01 to get an MSI
        var demoDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "demo", "01-hello-world"));
        var outputDir = Path.Combine(_tempDir, "build");
        Directory.CreateDirectory(outputDir);

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{demoDir}\" -- -o \"{outputDir}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = demoDir
        };

        using var process = System.Diagnostics.Process.Start(psi)!;
        process.WaitForExit(TimeSpan.FromMinutes(2));
        Assert.Equal(0, process.ExitCode);

        var msiFile = Directory.GetFiles(outputDir, "*.msi").FirstOrDefault();
        Assert.NotNull(msiFile);

        // Extract
        var extractDir = Path.Combine(_tempDir, "extracted");
        var result = MsiExtractor.Extract(msiFile, extractDir);

        Assert.True(result.IsSuccess, $"Extraction failed: {(result.IsFailure ? result.Error.Message : "")}");

        // Verify at least one file was extracted
        var files = Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
```

**Step 2: Write MsiExtractor**

```csharp
using System.Runtime.Versioning;
using FalkForge.Compiler.Msi;
using FalkForge.Decompiler;
using FalkForge.Decompiler.TableReaders;

namespace FalkForge.Cli;

[SupportedOSPlatform("windows")]
public static class MsiExtractor
{
    public static Result<int> Extract(string msiPath, string outputDir)
    {
        // Open MSI database
        var dbResult = MsiDatabase.Open(msiPath, readOnly: true);
        if (dbResult.IsFailure)
            return Result<int>.Failure(dbResult.Error);

        using var db = dbResult.Value;

        // Read directory table for path resolution
        var dirResult = db.QueryRows(
            "SELECT `Directory`, `Directory_Parent`, `DefaultDir` FROM `Directory`", 3);
        if (dirResult.IsFailure)
            return Result<int>.Failure(dirResult.Error);

        var dirEntries = dirResult.Value.Select(r => new DirectoryTableReader.DirectoryEntry
        {
            DirectoryId = r[0] ?? "",
            ParentId = r[1] ?? "",
            DefaultDir = r[2] ?? ""
        }).ToList();

        var resolver = new DirectoryResolver(dirEntries);

        // Read File + Component tables to map files to directories
        var fileResult = db.QueryRows(
            "SELECT `File`.`FileName`, `Component`.`Directory_` FROM `File` " +
            "INNER JOIN `Component` ON `File`.`Component_` = `Component`.`Component`", 2);
        if (fileResult.IsFailure)
            return Result<int>.Failure(fileResult.Error);

        var fileMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in fileResult.Value)
        {
            var fileName = row[0] ?? "";
            var directoryId = row[1] ?? "";

            // MSI FileName format: "ShortName|LongName" — use long name if present
            var longName = fileName.Contains('|') ? fileName.Split('|')[1] : fileName;
            var dirPath = resolver.Resolve(directoryId);
            fileMap[longName] = dirPath;
        }

        // Read cabinets from _Streams or Media table
        // For embedded cabinets, the cabinet name starts with '#'
        var mediaResult = db.QueryRows("SELECT `Cabinet` FROM `Media`", 1);
        if (mediaResult.IsFailure)
            return Result<int>.Failure(mediaResult.Error);

        var extractedCount = 0;

        foreach (var mediaRow in mediaResult.Value)
        {
            var cabinetName = mediaRow[0];
            if (string.IsNullOrEmpty(cabinetName)) continue;

            // Embedded cabinets start with '#'
            var isEmbedded = cabinetName.StartsWith('#');
            if (!isEmbedded) continue;

            var streamName = cabinetName[1..]; // Remove '#' prefix

            var streamResult = db.ExtractStream(streamName);
            if (streamResult.IsFailure) continue;

            using var cabStream = new MemoryStream(streamResult.Value);
            var cabResult = CabinetExtractor.Extract(cabStream);
            if (cabResult.IsFailure) continue;

            foreach (var (cabFileName, fileBytes) in cabResult.Value)
            {
                // Resolve the install directory for this file
                var dirPath = fileMap.GetValueOrDefault(cabFileName, "INSTALLFOLDER");
                var targetDir = Path.Combine(outputDir, dirPath);
                Directory.CreateDirectory(targetDir);

                var targetPath = Path.Combine(targetDir, cabFileName);
                File.WriteAllBytes(targetPath, fileBytes);
                extractedCount++;
            }
        }

        return Result<int>.Success(extractedCount);
    }
}
```

**Note:** The subagent needs to check if `MsiDatabase.ExtractStream()` exists. If not, it will need to add a method to read binary streams from the `_Streams` table, OR use an alternative approach (e.g., `MsiGetProperty` or a specialized query). Read the full `MsiDatabase.cs` to check available methods and adjust the implementation accordingly. The key pattern is: open MSI → read Media table for cabinet names → extract embedded stream → decompress via CabinetExtractor.

**Step 3: Build and run test**

Run: `dotnet build D:/Git/FalkInstaller/FalkForge.slnx`
Run: `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Cli.Tests/FalkForge.Cli.Tests.csproj --filter "FullyQualifiedName~MsiExtractorTests" --no-build -v minimal`
Expected: Test passes.

**Step 4: Commit**

```
feat(cli): add MsiExtractor for cabinet file extraction with directory resolution
```

---

### Task 3: Create ExtractCommand

**Files:**
- Create: `src/FalkForge.Cli/Commands/ExtractCommand.cs`
- Modify: `src/FalkForge.Cli/Program.cs`

**Step 1: Write the command**

Follow the `DecompileCommand` pattern exactly.

```csharp
using System.Diagnostics.CodeAnalysis;
using FalkForge.Cli.Settings;
using FalkForge.Engine.Protocol.Bundle;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FalkForge.Cli.Commands;

public sealed class ExtractCommand : Command<ExtractSettings>
{
    private readonly IConsoleOutput _console;

    public ExtractCommand() : this(new SpectreConsoleOutput()) { }

    public ExtractCommand(IConsoleOutput console)
    {
        _console = console;
    }

    public override int Execute([NotNull] CommandContext context, [NotNull] ExtractSettings settings)
    {
        var filePath = Path.GetFullPath(settings.FilePath);

        if (!File.Exists(filePath))
        {
            _console.WriteError($"File not found: {filePath}");
            return ExitCodes.RuntimeError;
        }

        var extension = Path.GetExtension(filePath);

        if (extension.Equals(".msi", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".msm", StringComparison.OrdinalIgnoreCase))
            return ExtractMsi(filePath, settings);

        if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
            return ExtractBundle(filePath, settings);

        _console.WriteError($"Unsupported file type '{extension}'.");
        return ExitCodes.RuntimeError;
    }

    private int ExtractMsi(string msiPath, ExtractSettings settings)
    {
        if (!OperatingSystem.IsWindows())
        {
            _console.WriteError("MSI extraction requires Windows.");
            return ExitCodes.RuntimeError;
        }

        if (settings.ListOnly)
        {
            _console.WriteError("--list is only supported for bundle (.exe) files.");
            return ExitCodes.ValidationFailure;
        }

        _console.MarkupLine($"[grey]Extracting: {Markup.Escape(msiPath)}[/]");

        var result = MsiExtractor.Extract(msiPath, settings.OutputPath!);
        if (result.IsFailure)
        {
            _console.WriteError($"Extraction failed: {result.Error.Message}");
            return ExitCodes.FromResult(result);
        }

        _console.MarkupLine($"[green]Extracted {result.Value} files to {Markup.Escape(settings.OutputPath!)}[/]");
        return ExitCodes.Success;
    }

    private int ExtractBundle(string bundlePath, ExtractSettings settings)
    {
        var contentResult = BundleReader.Extract(bundlePath);
        if (contentResult.IsFailure)
        {
            _console.WriteError($"Not a valid FalkForge bundle: {contentResult.Error.Message}");
            return ExitCodes.RuntimeError;
        }

        var content = contentResult.Value;
        var entries = content.TocEntries;

        if (settings.ListOnly)
        {
            _console.MarkupLine($"[grey]Packages in {Markup.Escape(Path.GetFileName(bundlePath))}:[/]");
            foreach (var entry in entries)
            {
                var sizeStr = FormatSize(entry.OriginalSize);
                _console.WriteLine($"  {entry.PackageId,-25} {sizeStr,10}");
            }
            return ExitCodes.Success;
        }

        // Filter by --package if specified
        var toExtract = entries.AsEnumerable();
        if (settings.Packages is { Length: > 0 })
        {
            var requested = new HashSet<string>(settings.Packages, StringComparer.OrdinalIgnoreCase);
            var available = entries.Select(e => e.PackageId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = requested.Except(available).ToList();
            if (missing.Count > 0)
            {
                _console.WriteError($"Package(s) not found: {string.Join(", ", missing)}");
                _console.MarkupLine("[grey]Available packages:[/]");
                foreach (var e in entries)
                    _console.WriteLine($"  {e.PackageId}");
                return ExitCodes.ValidationFailure;
            }
            toExtract = entries.Where(e => requested.Contains(e.PackageId));
        }

        _console.MarkupLine($"[grey]Extracting: {Markup.Escape(Path.GetFileName(bundlePath))}[/]");

        foreach (var entry in toExtract)
        {
            var payloadResult = BundleReader.ExtractPayload(bundlePath, entry);
            if (payloadResult.IsFailure)
            {
                _console.WriteError($"  Failed to extract {entry.PackageId}: {payloadResult.Error.Message}");
                return ExitCodes.RuntimeError;
            }

            var packageDir = Path.Combine(settings.OutputPath!, entry.PackageId);
            Directory.CreateDirectory(packageDir);

            // Determine output filename from manifest or use PackageId
            var outputFile = Path.Combine(packageDir, $"{entry.PackageId}.dat");
            File.WriteAllBytes(outputFile, payloadResult.Value);

            var sizeStr = FormatSize(entry.OriginalSize);
            _console.MarkupLine($"  [blue]{Markup.Escape(entry.PackageId)}[/] ({sizeStr}) → {Markup.Escape(packageDir)}");
        }

        _console.MarkupLine($"[green]Extracted to {Markup.Escape(settings.OutputPath!)}[/]");
        return ExitCodes.Success;
    }

    private static string FormatSize(int bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F1} MB"
    };
}
```

**Step 2: Register the command in Program.cs**

Add after the `decompile` command registration:

```csharp
config.AddCommand<ExtractCommand>("extract")
    .WithDescription("Extract files from an MSI or payloads from an EXE bundle")
    .WithExample("extract", "package.msi", "-o", "./output")
    .WithExample("extract", "bundle.exe", "--list")
    .WithExample("extract", "bundle.exe", "-o", "./output")
    .WithExample("extract", "bundle.exe", "-o", "./output", "--package", "ServerMsi");
```

**Step 3: Build**

Run: `dotnet build D:/Git/FalkInstaller/src/FalkForge.Cli/FalkForge.Cli.csproj`
Expected: 0 errors, 0 warnings.

**Step 4: Commit**

```
feat(cli): add forge extract command for MSI and bundle extraction
```

---

### Task 4: Add --extract and --extract-list to Engine

**Files:**
- Modify: `src/FalkForge.Engine/Program.cs`

**Step 1: Read the current Program.cs in full**

The subagent must read `D:/Git/FalkInstaller/src/FalkForge.Engine/Program.cs` in full to understand the argument parsing loop and the existing early-exit patterns (`--sbom`, `--plan-only`).

**Step 2: Add --extract and --extract-list argument parsing**

In the argument parsing loop (around lines 27-57), add:

```csharp
string? extractDir = null;
var extractList = false;
var extractPackages = new List<string>();

// In the argument parsing loop, add cases:
case "--extract":
    extractDir = args[++i];
    break;
case "--extract-list":
    extractList = true;
    break;
case "--package":
    extractPackages.Add(args[++i]);
    break;
```

**Step 3: Add early-exit extraction handler**

After the existing `--sbom` early-exit block (around line 88-89), add the extraction handler. This runs extraction using the bundle's own executable as the source, then exits immediately without starting the engine:

```csharp
if (extractList || extractDir is not null)
{
    var selfPath = Environment.ProcessPath
        ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;

    if (selfPath is null)
    {
        Console.Error.WriteLine("Error: Could not determine bundle path.");
        return ExitCodes.RuntimeError;
    }

    var contentResult = BundleReader.Extract(selfPath);
    if (contentResult.IsFailure)
    {
        Console.Error.WriteLine($"Error: {contentResult.Error.Message}");
        return ExitCodes.RuntimeError;
    }

    var content = contentResult.Value;

    if (extractList)
    {
        Console.WriteLine($"Packages in {Path.GetFileName(selfPath)}:");
        foreach (var entry in content.TocEntries)
        {
            var size = entry.OriginalSize < 1024 * 1024
                ? $"{entry.OriginalSize / 1024.0:F1} KB"
                : $"{entry.OriginalSize / (1024.0 * 1024.0):F1} MB";
            Console.WriteLine($"  {entry.PackageId,-25} {size,10}");
        }
        return 0;
    }

    // Extract payloads
    Directory.CreateDirectory(extractDir!);
    var toExtract = content.TocEntries.AsEnumerable();
    if (extractPackages.Count > 0)
    {
        var requested = new HashSet<string>(extractPackages, StringComparer.OrdinalIgnoreCase);
        var available = content.TocEntries.Select(e => e.PackageId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = requested.Except(available).ToList();
        if (missing.Count > 0)
        {
            Console.Error.WriteLine($"Package(s) not found: {string.Join(", ", missing)}");
            Console.Error.WriteLine("Available packages:");
            foreach (var e in content.TocEntries)
                Console.Error.WriteLine($"  {e.PackageId}");
            return 1;
        }
        toExtract = content.TocEntries.Where(e => requested.Contains(e.PackageId));
    }

    Console.WriteLine($"Extracting {Path.GetFileName(selfPath)}...");
    foreach (var entry in toExtract)
    {
        var payloadResult = BundleReader.ExtractPayload(selfPath, entry);
        if (payloadResult.IsFailure)
        {
            Console.Error.WriteLine($"  Failed: {entry.PackageId} — {payloadResult.Error.Message}");
            return 2;
        }

        var packageDir = Path.Combine(extractDir!, entry.PackageId);
        Directory.CreateDirectory(packageDir);
        File.WriteAllBytes(Path.Combine(packageDir, $"{entry.PackageId}.dat"), payloadResult.Value);

        var sizeStr = entry.OriginalSize < 1024 * 1024
            ? $"{entry.OriginalSize / 1024.0:F1} KB"
            : $"{entry.OriginalSize / (1024.0 * 1024.0):F1} MB";
        Console.WriteLine($"  {entry.PackageId} ({sizeStr}) → {packageDir}");
    }

    Console.WriteLine($"Extracted to {extractDir}");
    return 0;
}
```

**Important:** This must run BEFORE the existing bundle bootstrapper self-extraction logic and before the engine host is created. It bypasses all UI/detection/planning/elevation — just extracts and exits.

**Step 4: Build**

Run: `dotnet build D:/Git/FalkInstaller/src/FalkForge.Engine/FalkForge.Engine.csproj`
Expected: 0 errors, 0 warnings.

**Step 5: Commit**

```
feat(engine): add --extract and --extract-list self-extraction to bundle engine
```

---

### Task 5: Write CLI integration tests

**Files:**
- Create: `tests/FalkForge.Cli.Tests/ExtractCommandTests.cs`

**Step 1: Write integration tests**

Test the full `ExtractCommand` via its `Execute` method with mock console output.

```csharp
using System.Runtime.Versioning;
using FalkForge.Cli.Commands;
using FalkForge.Cli.Settings;
using Spectre.Console.Cli;

namespace FalkForge.Cli.Tests;

[SupportedOSPlatform("windows")]
public sealed class ExtractCommandTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TestConsoleOutput _console;

    public ExtractCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"extract-cmd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _console = new TestConsoleOutput();
    }

    [Fact]
    public void Execute_FileNotFound_ReturnsError()
    {
        var command = new ExtractCommand(_console);
        var settings = new ExtractSettings
        {
            FilePath = "nonexistent.msi",
            OutputPath = _tempDir
        };

        var result = command.Execute(
            new CommandContext([], "extract", null),
            settings);

        Assert.Equal(ExitCodes.RuntimeError, result);
        Assert.Contains("not found", _console.ErrorOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_NoOutput_NoList_ReturnsError()
    {
        var settings = new ExtractSettings
        {
            FilePath = "test.msi"
        };

        var result = settings.Validate();
        Assert.False(result.Successful);
    }

    [Fact]
    public void Validate_ListOnly_NoOutputRequired()
    {
        var settings = new ExtractSettings
        {
            FilePath = "test.exe",
            ListOnly = true
        };

        var result = settings.Validate();
        Assert.True(result.Successful);
    }

    [Fact]
    public void Validate_PackageOnMsi_ReturnsError()
    {
        var settings = new ExtractSettings
        {
            FilePath = "test.msi",
            OutputPath = _tempDir,
            Packages = ["ServerMsi"]
        };

        var result = settings.Validate();
        Assert.False(result.Successful);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private sealed class TestConsoleOutput : IConsoleOutput
    {
        public string ErrorOutput { get; private set; } = "";
        public string Output { get; private set; } = "";

        public void MarkupLine(string markup) => Output += markup + "\n";
        public void WriteLine(string text) => Output += text + "\n";
        public void WriteError(string text) => ErrorOutput += text + "\n";
    }
}
```

**Step 2: Build and run tests**

Run: `dotnet build D:/Git/FalkInstaller/FalkForge.slnx`
Run: `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Cli.Tests/FalkForge.Cli.Tests.csproj --filter "FullyQualifiedName~ExtractCommand" --no-build -v minimal`
Expected: All tests pass.

**Step 3: Commit**

```
test(cli): add ExtractCommand and ExtractSettings tests
```

---

### Task 6: Full test suite verification

**Step 1: Build full solution**

Run: `dotnet build D:/Git/FalkInstaller/FalkForge.slnx`
Expected: 0 errors, 0 warnings.

**Step 2: Run full test suite**

Run: `dotnet test D:/Git/FalkInstaller/FalkForge.slnx -v minimal`
Expected: All ~3,918+ tests pass.

**Step 3: Commit if any fixes needed**

```
feat(cli): complete forge extract command implementation
```
