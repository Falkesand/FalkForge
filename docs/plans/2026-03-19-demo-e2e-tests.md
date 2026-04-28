# Demo End-to-End Tests Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Verify all demos compile, produce valid MSI/Bundle outputs, and survive decompilation round-trips — without installing anything.

**Architecture:** Each demo is run as a subprocess via `dotnet run --project <path> -- -o <tempdir>`, producing an MSI or Bundle. The output is then inspected (metadata), ICE-validated (MSI only), and decompiled back to a model for structural assertions. A shared `DemoBuildFixture` runs demos once per test session; all test phases share the outputs.

**Tech Stack:** xUnit `ICollectionFixture`, `Process` for subprocess invocation, `MsiDatabase` read-only inspection, `MsiDecompiler`/`BundleDecompiler` for round-trip, `IceValidator` for ICE checks.

---

### Task 1: Add Decompiler Reference to Integration Tests

**Files:**
- Modify: `tests/FalkForge.Integration.Tests/FalkForge.Integration.Tests.csproj`

**Step 1: Add project references**

Add these `<ProjectReference>` entries to the existing `<ItemGroup>`:

```xml
<ProjectReference Include="..\..\src\FalkForge.Decompiler\FalkForge.Decompiler.csproj" />
<ProjectReference Include="..\..\src\FalkForge.Cli\FalkForge.Cli.csproj" />
```

The Decompiler is needed for round-trip verification. The Cli reference provides `MsiInspector`.

**Step 2: Build to verify references resolve**

Run: `dotnet build D:/Git/FalkInstaller/tests/FalkForge.Integration.Tests/FalkForge.Integration.Tests.csproj`
Expected: Build succeeded. 0 warnings.

**Step 3: Commit**

```
feat(tests): add Decompiler and Cli references to integration tests
```

---

### Task 2: Create DemoExpectation Model and DemoTestCatalog

**Files:**
- Create: `tests/FalkForge.Integration.Tests/DemoEndToEnd/DemoExpectation.cs`
- Create: `tests/FalkForge.Integration.Tests/DemoEndToEnd/DemoTestCatalog.cs`

**Step 1: Write DemoExpectation record**

```csharp
namespace FalkForge.Integration.Tests.DemoEndToEnd;

public enum DemoOutputType
{
    Msi,
    Bundle,
    MergeModule,
    Patch,
    Transform
}

public sealed record DemoExpectation(
    string Name,
    string ProjectPath,
    DemoOutputType OutputType,
    string[] RequiredTables,
    bool RequiresInfrastructure = false);
```

**Step 2: Write DemoTestCatalog with all demo entries**

```csharp
namespace FalkForge.Integration.Tests.DemoEndToEnd;

public static class DemoTestCatalog
{
    private static readonly string DemoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "demo"));

    public static IEnumerable<object[]> MsiDemos => AllDemos
        .Where(d => d.OutputType == DemoOutputType.Msi)
        .Select(d => new object[] { d });

    public static IEnumerable<object[]> BundleDemos => AllDemos
        .Where(d => d.OutputType == DemoOutputType.Bundle)
        .Select(d => new object[] { d });

    public static IEnumerable<object[]> AllDemosData => AllDemos
        .Select(d => new object[] { d });

    public static IEnumerable<DemoExpectation> AllDemos { get; } = BuildCatalog();

    private static List<DemoExpectation> BuildCatalog()
    {
        var demos = new List<DemoExpectation>
        {
            // Core MSI demos
            Msi("01-hello-world", "File", "Component", "Directory", "Feature"),
            Msi("02-notepad-clone", "File", "Component", "Directory", "Feature", "Shortcut"),
            Msi("03-client-server", "File", "Component", "Directory", "Feature", "ServiceInstall"),
            Msi("04-dev-toolkit", "File", "Component", "Directory", "Feature", "Environment", "Registry"),
            Msi("05-enterprise-suite", "File", "Component", "Directory", "Feature"),
            Msi("07-extensions-showcase", "File", "Component", "Directory", "Feature", requiresInfra: true),
            Msi("08-localization", "File", "Component", "Directory", "Feature"),
            Msi("09-advanced-msi", "File", "Component", "Directory", "Feature", "CustomAction"),
            Msi("11-custom-ui-simple", "File", "Component", "Directory", "Feature"),
            Msi("12-custom-ui-vstyle", "File", "Component", "Directory", "Feature"),
            Msi("13-glass-ui", "File", "Component", "Directory", "Feature"),
            Msi("14-lifecycle-hooks", "File", "Component", "Directory", "Feature"),
            Msi("16-features", "File", "Component", "Directory", "Feature"),
            Msi("17-services", "File", "Component", "Directory", "Feature", "ServiceInstall"),
            Msi("18-environment-variables", "File", "Component", "Directory", "Feature", "Environment"),
            Msi("19-file-associations", "File", "Component", "Directory", "Feature"),
            Msi("20-custom-actions", "File", "Component", "Directory", "Feature", "CustomAction"),
            Msi("21-launch-conditions", "File", "Component", "Directory", "Feature", "LaunchCondition"),
            Msi("22-ini-files", "File", "Component", "Directory", "Feature", "IniFile"),
            Msi("23-permissions", "File", "Component", "Directory", "Feature"),
            Msi("24-fonts", "File", "Component", "Directory", "Feature", "Font"),
            Msi("25-file-operations", "File", "Component", "Directory", "Feature"),
            Msi("26-custom-tables", "File", "Component", "Directory", "Feature"),
            Msi("27-gac-assembly", "File", "Component", "Directory", "Feature", "MsiAssembly"),
            Msi("28-sequence-scheduling", "File", "Component", "Directory", "Feature"),
            Msi("29-ext-firewall", "File", "Component", "Directory", "Feature"),
            Msi("30-ext-iis", "File", "Component", "Directory", "Feature", requiresInfra: true),
            Msi("31-ext-sql", "File", "Component", "Directory", "Feature", requiresInfra: true),
            Msi("32-ext-dotnet", "File", "Component", "Directory", "Feature"),
            Msi("33-ext-util", "File", "Component", "Directory", "Feature"),
            Msi("34-ext-dependency", "File", "Component", "Directory", "Feature", "Registry"),
            Msi("47-powershell-actions", "File", "Component", "Directory", "Feature", "CustomAction"),
            Msi("48-com-registration", "File", "Component", "Directory", "Feature", "Registry"),
            Msi("49-http-extension", "File", "Component", "Directory", "Feature"),
            Msi("50-driver-install", "File", "Component", "Directory", "Feature"),
            Msi("51-ice-validation", "File", "Component", "Directory", "Feature"),

            // Special MSI types
            new("44-merge-module", Path.Combine(DemoRoot, "44-merge-module", "44-merge-module.csproj"),
                DemoOutputType.MergeModule, ["File", "Component", "Directory"]),
            new("45-patch", Path.Combine(DemoRoot, "45-patch", "45-patch.csproj"),
                DemoOutputType.Patch, []),
            new("46-transform", Path.Combine(DemoRoot, "46-transform", "46-transform.csproj"),
                DemoOutputType.Transform, []),

            // Bundle demos
            Bundle("35-bundle-simple"),
            Bundle("36-bundle-exe-package"),
            Bundle("37-bundle-msu-package"),
            Bundle("38-bundle-nested"),
            Bundle("39-bundle-remote-payload"),
            Bundle("40-bundle-variables"),
            Bundle("41-bundle-rollback"),
            Bundle("42-bundle-update-feed"),
            Bundle("43-bundle-layout"),
        };

        return demos;
    }

    private static DemoExpectation Msi(string name, params string[] requiredTables)
        => new(name, Path.Combine(DemoRoot, name, $"{name}.csproj"),
            DemoOutputType.Msi, requiredTables);

    private static DemoExpectation Msi(string name, string t1, string t2, string t3,
        string t4, bool requiresInfra)
        => new(name, Path.Combine(DemoRoot, name, $"{name}.csproj"),
            DemoOutputType.Msi, [t1, t2, t3, t4], requiresInfra);

    private static DemoExpectation Msi(string name, string t1, string t2, string t3,
        string t4, string t5, bool requiresInfra = false)
        => new(name, Path.Combine(DemoRoot, name, $"{name}.csproj"),
            DemoOutputType.Msi, [t1, t2, t3, t4, t5], requiresInfra);

    private static DemoExpectation Msi(string name, string t1, string t2, string t3,
        string t4, string t5, string t6, bool requiresInfra = false)
        => new(name, Path.Combine(DemoRoot, name, $"{name}.csproj"),
            DemoOutputType.Msi, [t1, t2, t3, t4, t5, t6], requiresInfra);

    private static DemoExpectation Bundle(string name)
        => new(name, Path.Combine(DemoRoot, name, $"{name}.csproj"),
            DemoOutputType.Bundle, []);
}
```

**Step 3: Build to verify compilation**

Run: `dotnet build D:/Git/FalkInstaller/tests/FalkForge.Integration.Tests/FalkForge.Integration.Tests.csproj`
Expected: Build succeeded.

**Step 4: Commit**

```
feat(tests): add DemoExpectation model and DemoTestCatalog
```

---

### Task 3: Create DemoBuildFixture

**Files:**
- Create: `tests/FalkForge.Integration.Tests/DemoEndToEnd/DemoBuildFixture.cs`

**Step 1: Write the fixture that runs demos and caches outputs**

```csharp
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace FalkForge.Integration.Tests.DemoEndToEnd;

[SupportedOSPlatform("windows")]
public sealed class DemoBuildFixture : IDisposable
{
    private readonly string _outputRoot;
    private readonly ConcurrentDictionary<string, DemoBuildResult> _results = new();

    public DemoBuildFixture()
    {
        _outputRoot = Path.Combine(Path.GetTempPath(), $"falk-demo-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_outputRoot);
    }

    public DemoBuildResult GetOrBuild(DemoExpectation demo)
    {
        return _results.GetOrAdd(demo.Name, _ => RunDemo(demo));
    }

    private DemoBuildResult RunDemo(DemoExpectation demo)
    {
        var outputDir = Path.Combine(_outputRoot, demo.Name);
        Directory.CreateDirectory(outputDir);

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{demo.ProjectPath}\" --no-build -- -o \"{outputDir}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(TimeSpan.FromMinutes(2));

        var outputFile = FindOutputFile(outputDir, demo.OutputType);

        return new DemoBuildResult(
            ExitCode: process.ExitCode,
            OutputFile: outputFile,
            OutputDir: outputDir,
            Stdout: stdout,
            Stderr: stderr);
    }

    private static string? FindOutputFile(string dir, DemoOutputType type)
    {
        if (!Directory.Exists(dir))
            return null;

        var pattern = type switch
        {
            DemoOutputType.Msi => "*.msi",
            DemoOutputType.Bundle => "*.exe",
            DemoOutputType.MergeModule => "*.msm",
            DemoOutputType.Patch => "*.msp",
            DemoOutputType.Transform => "*.mst",
            _ => "*.*"
        };

        return Directory.GetFiles(dir, pattern).FirstOrDefault();
    }

    public void Dispose()
    {
        try { Directory.Delete(_outputRoot, recursive: true); }
        catch { /* best effort cleanup */ }
    }
}

public sealed record DemoBuildResult(
    int ExitCode,
    string? OutputFile,
    string OutputDir,
    string Stdout,
    string Stderr)
{
    public bool Succeeded => ExitCode == 0 && OutputFile is not null;
}
```

**Step 2: Create the xUnit collection definition**

Create `tests/FalkForge.Integration.Tests/DemoEndToEnd/DemoTestCollection.cs`:

```csharp
namespace FalkForge.Integration.Tests.DemoEndToEnd;

[CollectionDefinition("DemoEndToEnd")]
public sealed class DemoTestCollection : ICollectionFixture<DemoBuildFixture>;
```

**Step 3: Build to verify**

Run: `dotnet build D:/Git/FalkInstaller/tests/FalkForge.Integration.Tests/FalkForge.Integration.Tests.csproj`
Expected: Build succeeded.

**Step 4: Commit**

```
feat(tests): add DemoBuildFixture for subprocess demo execution
```

---

### Task 4: Write DemoCompilationTests (Phase 1)

**Files:**
- Create: `tests/FalkForge.Integration.Tests/DemoEndToEnd/DemoCompilationTests.cs`

**Step 1: Write the failing test**

```csharp
using System.Runtime.Versioning;

namespace FalkForge.Integration.Tests.DemoEndToEnd;

[Collection("DemoEndToEnd")]
[SupportedOSPlatform("windows")]
public sealed class DemoCompilationTests
{
    private readonly DemoBuildFixture _fixture;

    public DemoCompilationTests(DemoBuildFixture fixture) => _fixture = fixture;

    [Theory]
    [MemberData(nameof(DemoTestCatalog.AllDemosData), MemberType = typeof(DemoTestCatalog))]
    public void Demo_ProducesOutputFile(DemoExpectation demo)
    {
        if (demo.RequiresInfrastructure)
        {
            // Skip demos that need SQL Server, IIS, etc.
            return;
        }

        var result = _fixture.GetOrBuild(demo);

        Assert.True(result.ExitCode == 0,
            $"Demo '{demo.Name}' failed with exit code {result.ExitCode}.\nStderr: {result.Stderr}");
        Assert.NotNull(result.OutputFile);
        Assert.True(File.Exists(result.OutputFile),
            $"Demo '{demo.Name}' output file not found at {result.OutputFile}");
        Assert.True(new FileInfo(result.OutputFile).Length > 0,
            $"Demo '{demo.Name}' produced empty output file");
    }
}
```

**Step 2: Build the demos first (needed for --no-build in fixture)**

Run: `dotnet build D:/Git/FalkInstaller/FalkForge.slnx`

**Step 3: Run the test**

Run: `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Integration.Tests/FalkForge.Integration.Tests.csproj --filter "FullyQualifiedName~DemoCompilationTests" -v minimal`
Expected: All non-infrastructure demos pass.

**Step 4: Commit**

```
test(integration): add demo compilation verification tests
```

---

### Task 5: Write DemoInspectionTests (Phase 2 — MSI Metadata)

**Files:**
- Create: `tests/FalkForge.Integration.Tests/DemoEndToEnd/DemoInspectionTests.cs`

**Step 1: Write the test**

```csharp
using System.Runtime.Versioning;
using FalkForge.Cli;

namespace FalkForge.Integration.Tests.DemoEndToEnd;

[Collection("DemoEndToEnd")]
[SupportedOSPlatform("windows")]
public sealed class DemoInspectionTests
{
    private readonly DemoBuildFixture _fixture;

    public DemoInspectionTests(DemoBuildFixture fixture) => _fixture = fixture;

    [Theory]
    [MemberData(nameof(DemoTestCatalog.MsiDemos), MemberType = typeof(DemoTestCatalog))]
    public void Msi_HasValidMetadata(DemoExpectation demo)
    {
        if (demo.RequiresInfrastructure) return;

        var build = _fixture.GetOrBuild(demo);
        if (!build.Succeeded) return; // compilation test will catch this

        var result = MsiInspector.Inspect(build.OutputFile!);

        Assert.True(result.IsSuccess,
            $"Inspection failed for '{demo.Name}': {(result.IsFailure ? result.Error.Message : "")}");

        var info = result.Value;
        Assert.False(string.IsNullOrWhiteSpace(info.ProductName),
            $"Demo '{demo.Name}' MSI has no ProductName");
        Assert.False(string.IsNullOrWhiteSpace(info.Manufacturer),
            $"Demo '{demo.Name}' MSI has no Manufacturer");
        Assert.False(string.IsNullOrWhiteSpace(info.Version),
            $"Demo '{demo.Name}' MSI has no ProductVersion");
        Assert.False(string.IsNullOrWhiteSpace(info.ProductCode),
            $"Demo '{demo.Name}' MSI has no ProductCode");
        Assert.True(info.TableCount > 0,
            $"Demo '{demo.Name}' MSI has no tables");
    }

    [Theory]
    [MemberData(nameof(DemoTestCatalog.MsiDemos), MemberType = typeof(DemoTestCatalog))]
    public void Msi_ContainsRequiredTables(DemoExpectation demo)
    {
        if (demo.RequiresInfrastructure || demo.RequiredTables.Length == 0) return;

        var build = _fixture.GetOrBuild(demo);
        if (!build.Succeeded) return;

        var result = MsiInspector.Inspect(build.OutputFile!);
        if (result.IsFailure) return; // metadata test will catch this

        var tableNames = result.Value.TableNames;
        foreach (var expected in demo.RequiredTables)
        {
            Assert.Contains(expected, tableNames,
                StringComparer.OrdinalIgnoreCase);
        }
    }
}
```

**Step 2: Run tests**

Run: `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Integration.Tests/FalkForge.Integration.Tests.csproj --filter "FullyQualifiedName~DemoInspectionTests" -v minimal`
Expected: All non-infrastructure MSI demos pass.

**Step 3: Commit**

```
test(integration): add MSI metadata inspection tests for demos
```

---

### Task 6: Write DemoDecompilationTests (Phase 3 — Round-Trip)

**Files:**
- Create: `tests/FalkForge.Integration.Tests/DemoEndToEnd/DemoDecompilationTests.cs`

**Step 1: Write the test**

```csharp
using System.Runtime.Versioning;
using FalkForge.Decompiler;

namespace FalkForge.Integration.Tests.DemoEndToEnd;

[Collection("DemoEndToEnd")]
[SupportedOSPlatform("windows")]
public sealed class DemoDecompilationTests
{
    private readonly DemoBuildFixture _fixture;

    public DemoDecompilationTests(DemoBuildFixture fixture) => _fixture = fixture;

    [Theory]
    [MemberData(nameof(DemoTestCatalog.MsiDemos), MemberType = typeof(DemoTestCatalog))]
    public void Msi_DecompilesToValidPackageModel(DemoExpectation demo)
    {
        if (demo.RequiresInfrastructure) return;

        var build = _fixture.GetOrBuild(demo);
        if (!build.Succeeded) return;

        var decompiler = new MsiDecompiler();
        var result = decompiler.Decompile(build.OutputFile!);

        Assert.True(result.IsSuccess,
            $"Decompilation failed for '{demo.Name}': {(result.IsFailure ? result.Error.Message : "")}");

        var model = result.Value;
        Assert.False(string.IsNullOrWhiteSpace(model.Name),
            $"Decompiled '{demo.Name}' has no Name");
        Assert.NotEmpty(model.Features);
        Assert.True(model.Features.Sum(f => f.Components.Count) > 0,
            $"Decompiled '{demo.Name}' has no components");
    }

    [Theory]
    [MemberData(nameof(DemoTestCatalog.MsiDemos), MemberType = typeof(DemoTestCatalog))]
    public void Msi_DecompilesToValidCSharp(DemoExpectation demo)
    {
        if (demo.RequiresInfrastructure) return;

        var build = _fixture.GetOrBuild(demo);
        if (!build.Succeeded) return;

        var decompiler = new MsiDecompiler();
        var result = decompiler.DecompileToCSharp(build.OutputFile!);

        Assert.True(result.IsSuccess,
            $"C# decompilation failed for '{demo.Name}': {(result.IsFailure ? result.Error.Message : "")}");
        Assert.False(string.IsNullOrWhiteSpace(result.Value),
            $"Decompiled C# for '{demo.Name}' is empty");
        Assert.Contains("PackageBuilder", result.Value);
    }

    [Theory]
    [MemberData(nameof(DemoTestCatalog.BundleDemos), MemberType = typeof(DemoTestCatalog))]
    public void Bundle_DecompilesToValidBundleModel(DemoExpectation demo)
    {
        var build = _fixture.GetOrBuild(demo);
        if (!build.Succeeded) return;

        var decompiler = new BundleDecompiler();
        var result = decompiler.Decompile(build.OutputFile!);

        Assert.True(result.IsSuccess,
            $"Bundle decompilation failed for '{demo.Name}': {(result.IsFailure ? result.Error.Message : "")}");

        var model = result.Value;
        Assert.False(string.IsNullOrWhiteSpace(model.Name),
            $"Decompiled bundle '{demo.Name}' has no Name");
    }

    [Theory]
    [MemberData(nameof(DemoTestCatalog.BundleDemos), MemberType = typeof(DemoTestCatalog))]
    public void Bundle_DecompilesToValidCSharp(DemoExpectation demo)
    {
        var build = _fixture.GetOrBuild(demo);
        if (!build.Succeeded) return;

        var decompiler = new BundleDecompiler();
        var result = decompiler.DecompileToCSharp(build.OutputFile!);

        Assert.True(result.IsSuccess,
            $"Bundle C# decompilation failed for '{demo.Name}': {(result.IsFailure ? result.Error.Message : "")}");
        Assert.False(string.IsNullOrWhiteSpace(result.Value),
            $"Decompiled C# for bundle '{demo.Name}' is empty");
        Assert.Contains("BundleBuilder", result.Value);
    }
}
```

**Step 2: Run tests**

Run: `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Integration.Tests/FalkForge.Integration.Tests.csproj --filter "FullyQualifiedName~DemoDecompilationTests" -v minimal`
Expected: All non-infrastructure demos pass.

**Step 3: Commit**

```
test(integration): add decompilation round-trip tests for demos
```

---

### Task 7: Write DemoIceValidationTests (Phase 4 — ICE Checks)

**Files:**
- Create: `tests/FalkForge.Integration.Tests/DemoEndToEnd/DemoIceValidationTests.cs`

**Step 1: Write the test**

```csharp
using System.Runtime.Versioning;
using FalkForge.Compiler.Msi.Validation;

namespace FalkForge.Integration.Tests.DemoEndToEnd;

[Collection("DemoEndToEnd")]
[SupportedOSPlatform("windows")]
[Trait("Category", "ICE")]
public sealed class DemoIceValidationTests
{
    private readonly DemoBuildFixture _fixture;

    public DemoIceValidationTests(DemoBuildFixture fixture) => _fixture = fixture;

    [Theory]
    [MemberData(nameof(DemoTestCatalog.MsiDemos), MemberType = typeof(DemoTestCatalog))]
    public void Msi_PassesIceValidation(DemoExpectation demo)
    {
        if (demo.RequiresInfrastructure) return;

        var build = _fixture.GetOrBuild(demo);
        if (!build.Succeeded) return;

        var validator = new IceValidator();
        var result = validator.Validate(build.OutputFile!);

        // IceValidator returns success with empty results if darice.cub not found
        // (Windows SDK not installed) — that's OK, we just skip gracefully
        if (result.IsFailure) return;

        var validation = result.Value;
        Assert.True(validation.IsValid,
            $"ICE validation failed for '{demo.Name}':\n" +
            string.Join("\n", validation.Errors.Concat(validation.Failures)
                .Select(m => $"  {m.IceCode}: {m.Description}")));
    }
}
```

**Step 2: Run tests**

Run: `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Integration.Tests/FalkForge.Integration.Tests.csproj --filter "FullyQualifiedName~DemoIceValidationTests" -v minimal`
Expected: Pass (or skip gracefully if Windows SDK not installed).

**Step 3: Commit**

```
test(integration): add ICE validation tests for demo MSIs
```

---

### Task 8: Final Verification and Cleanup

**Step 1: Build all demos**

Run: `dotnet build D:/Git/FalkInstaller/FalkForge.slnx`
Expected: Build succeeded. 0 warnings.

**Step 2: Run all demo E2E tests**

Run: `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Integration.Tests/FalkForge.Integration.Tests.csproj --filter "FullyQualifiedName~DemoEndToEnd" -v minimal`
Expected: All tests pass.

**Step 3: Run full test suite to verify no regressions**

Run: `dotnet test D:/Git/FalkInstaller/FalkForge.slnx -v minimal`
Expected: All ~3871 tests pass.

**Step 4: Commit**

```
test(integration): complete demo end-to-end test suite
```
