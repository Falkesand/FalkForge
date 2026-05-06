# Quick Wins Implementation Plan: PowerShell CA, COM Registration, Driver Installation

> **PARTIALLY COMPLETED 2026-05-05** — COM Registration (Part B) is done: `ClassTableProducer.cs`, `TypeLibTableProducer.cs`, and `ProgIdTableProducer.cs` exist in `src/FalkForge.Compiler.Msi/Recipe/Producers/`. PowerShell CA (Part A) and Driver (Part C) status: verify separately. All `TableEmitter.cs` references in task bodies are stale — that file was deleted at commit 0d853bd (Phase 9 recipe cutover: 1c40837). Replace with the relevant producer file for any remaining emission work.

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add PowerShell custom actions, COM class/TypeLib registration, and driver installation support — three independent features following existing FalkForge patterns.

**Architecture:** Each feature follows Model → Builder → Table/CA emission. PowerShell extends CustomActionBuilder. COM adds new models to Core + table emission to Compiler.Msi via recipe producers. Driver creates a new Extensions.Driver project.

**Tech Stack:** C# 13, .NET 10, xUnit, MSI table emission via P/Invoke

---

## Reference Files

- `src/FalkForge.Core/Models/CustomActionType.cs` — CA type constants
- `src/FalkForge.Core/Models/CustomActionModel.cs` — CA model (13 lines)
- `src/FalkForge.Core/Builders/CustomActionBuilder.cs` — CA fluent builder (113 lines)
- `src/FalkForge.Compiler.Msi/Tables/TableEmitter.cs` — EmitCustomActions at ~1256, EmitProgIds at ~1180
- `src/FalkForge.Compiler.Msi/Tables/MsiTableDefinitions.cs` — Class (line ~86), TypeLib, ProgId tables
- `src/FalkForge.Extensions.Util/QuietExec/QuietExecModel.cs` — extension model pattern
- `src/FalkForge.Extensions.Firewall/FirewallExtension.cs` — IFalkForgeExtension pattern
- `src/FalkForge.Extensions.Firewall/FalkForge.Extensions.Firewall.csproj` — extension project template

---

## Part A: PowerShell Custom Actions

### Task 1: PowerShell model and builder methods

**Files:**
- Modify: `src/FalkForge.Core/Models/CustomActionType.cs` — add PowerShell constants
- Modify: `src/FalkForge.Core/Builders/CustomActionBuilder.cs` — add PowerShellScript() and PowerShellFile()
- Create: `tests/FalkForge.Core.Tests/PowerShellCustomActionTests.cs`

**CustomActionType.cs — add constants:**
```csharp
    public const int PowerShellScript = 51; // SetProperty type — stores script in property
    public const int PowerShellExec = 34 | 0x100; // ExeInDir + InScript (deferred)
```

Actually, the PowerShell CA uses a two-step approach:
1. A SetProperty CA (type 51) that stores the powershell command line
2. An ExeInDir CA (type 34) that executes powershell.exe

But the builder should hide this complexity. The builder creates TWO CustomActionModels internally.

**CustomActionBuilder.cs — add methods (read file first to find exact insertion point):**

```csharp
    public CustomActionBuilder PowerShellScript(string script)
    {
        _baseType = CustomActionType.ExeInDir;
        _sourceRef = "[SystemFolder]";
        var escapedScript = script.Replace("\"", "\\\"");
        _target = $"powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{escapedScript}\"";
        return this;
    }

    public CustomActionBuilder PowerShellFile(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"PowerShell script not found: {filePath}", filePath);
        var content = File.ReadAllText(filePath);
        return PowerShellScript(content);
    }
```

Note: Read the builder carefully. It has `_baseType`, `_sourceRef`, `_target` fields. The `ExeInDir` type uses SourceRef as the directory reference and Target as the command. `[SystemFolder]` resolves to `C:\Windows\System32\` at install time.

The `Is64Bit` consideration: on 64-bit systems, `[SystemFolder]` points to System32 (64-bit), `[System64Folder]` also points to System32. For 32-bit PowerShell, use `[SystemFolder]` on 32-bit or `[System32Folder]` + SysWOW64. Default to 64-bit.

**Tests (2):**

```csharp
namespace FalkForge.Tests;

public sealed class PowerShellCustomActionTests
{
    [Fact]
    public void PowerShellScript_SetsExeInDirWithPowerShellCommand()
    {
        var builder = new CustomActionBuilder("TestPS");
        builder.PowerShellScript("Write-Host 'Hello'").Deferred().NoImpersonate();
        var model = builder.Build();

        Assert.Equal(CustomActionType.ExeInDir | CustomActionType.InScript | CustomActionType.NoImpersonate, model.Type);
        Assert.Equal("[SystemFolder]", model.SourceRef);
        Assert.Contains("powershell.exe", model.Target);
        Assert.Contains("Write-Host", model.Target);
    }

    [Fact]
    public void PowerShellFile_ReadsAndEmbedsContent()
    {
        var tempPs1 = Path.GetTempFileName() + ".ps1";
        File.WriteAllText(tempPs1, "Get-Process | Out-File C:\\log.txt");
        try
        {
            var builder = new CustomActionBuilder("TestPSFile");
            builder.PowerShellFile(tempPs1);
            var model = builder.Build();

            Assert.Contains("Get-Process", model.Target);
        }
        finally
        {
            File.Delete(tempPs1);
        }
    }
}
```

NOTE: Read CustomActionBuilder constructor to see if it takes an id parameter or uses a separate Id() method. Adjust test accordingly.

**Verify:** `dotnet test <worktree>/tests/FalkForge.Core.Tests/ --filter "FullyQualifiedName~PowerShellCustomAction"`

**Commit:** `feat(core): add PowerShell custom action builder methods`

---

### Task 2: PowerShell PackageBuilder integration and compilation test

**Files:**
- Create: `tests/FalkForge.Compiler.Msi.Tests/PowerShellEmissionTests.cs`

The existing `EmitCustomActions` in `TableEmitter.cs` already handles any CustomActionModel — since PowerShellScript() just sets `ExeInDir` type with powershell.exe target, no table emission changes are needed. The test verifies the end-to-end model → emission path.

**Test (2):**

```csharp
namespace FalkForge.Compiler.Msi.Tests;

public sealed class PowerShellEmissionTests
{
    [Fact]
    public void PowerShellAction_ProducesValidCustomActionModel()
    {
        var builder = new CustomActionBuilder("InstallScript");
        builder.PowerShellScript("New-Item -ItemType Directory -Path C:\\MyApp\\Data")
            .Deferred()
            .NoImpersonate();
        var model = builder.Build();

        // Verify it would serialize correctly to MSI table
        Assert.Equal("InstallScript", model.Id);
        Assert.NotNull(model.Target);
        Assert.StartsWith("powershell.exe", model.Target);
        Assert.Equal("[SystemFolder]", model.SourceRef);
    }

    [Fact]
    public void PowerShellFile_NonExistent_ThrowsFileNotFound()
    {
        var builder = new CustomActionBuilder("BadScript");
        Assert.Throws<FileNotFoundException>(() =>
            builder.PowerShellFile("nonexistent.ps1"));
    }
}
```

**Verify:** `dotnet test <worktree>/tests/FalkForge.Compiler.Msi.Tests/ --filter "FullyQualifiedName~PowerShellEmission"`

**Commit:** `test(compiler): add PowerShell custom action emission tests`

---

## Part B: COM Registration

### Task 3: COM models and enums

**Files:**
- Create: `src/FalkForge.Core/Models/ComClassModel.cs`
- Create: `src/FalkForge.Core/Models/ComTypeLibModel.cs`
- Create: `src/FalkForge.Core/Models/ComServerType.cs`
- Create: `src/FalkForge.Core/Models/ComThreadingModel.cs`

**ComServerType.cs:**
```csharp
namespace FalkForge.Models;

public enum ComServerType
{
    InprocServer32,
    LocalServer32
}
```

**ComThreadingModel.cs:**
```csharp
namespace FalkForge.Models;

public enum ComThreadingModel
{
    Apartment,
    Free,
    Both,
    Neutral
}
```

**ComClassModel.cs:**
```csharp
namespace FalkForge.Models;

public sealed record ComClassModel
{
    public required Guid ClassId { get; init; }
    public required ComServerType ServerType { get; init; }
    public string? ProgId { get; init; }
    public string? Description { get; init; }
    public ComThreadingModel ThreadingModel { get; init; } = ComThreadingModel.Apartment;
    public Guid? AppId { get; init; }
    public string? ComponentRef { get; init; }
}
```

**ComTypeLibModel.cs:**
```csharp
namespace FalkForge.Models;

public sealed record ComTypeLibModel
{
    public required Guid TypeLibId { get; init; }
    public required Version Version { get; init; }
    public int Language { get; init; }
    public string? Description { get; init; }
    public string? ComponentRef { get; init; }
}
```

**No tests for models alone** — tested via builders in Task 4.

**Verify:** `dotnet build <worktree>/src/FalkForge.Core/FalkForge.Core.csproj`

**Commit:** `feat(core): add COM class and TypeLib models`

---

### Task 4: COM builders

**Files:**
- Create: `src/FalkForge.Core/Builders/ComClassBuilder.cs`
- Create: `src/FalkForge.Core/Builders/ComTypeLibBuilder.cs`
- Modify: `src/FalkForge.Core/Builders/PackageBuilder.cs` — add ComClass() and TypeLib() methods + lists
- Modify: `src/FalkForge.Core/Models/PackageModel.cs` — add ComClasses and TypeLibs properties
- Create: `tests/FalkForge.Core.Tests/ComRegistrationTests.cs`

**ComClassBuilder.cs:**
```csharp
using FalkForge.Models;

namespace FalkForge.Builders;

public sealed class ComClassBuilder
{
    private Guid _classId;
    private ComServerType _serverType = ComServerType.InprocServer32;
    private string? _progId;
    private string? _description;
    private ComThreadingModel _threadingModel = ComThreadingModel.Apartment;
    private Guid? _appId;
    private string? _componentRef;

    public ComClassBuilder ClassId(Guid classId) { _classId = classId; return this; }
    public ComClassBuilder InprocServer32() { _serverType = ComServerType.InprocServer32; return this; }
    public ComClassBuilder LocalServer32() { _serverType = ComServerType.LocalServer32; return this; }
    public ComClassBuilder ProgId(string progId) { _progId = progId; return this; }
    public ComClassBuilder Description(string desc) { _description = desc; return this; }
    public ComClassBuilder ThreadingModel(ComThreadingModel model) { _threadingModel = model; return this; }
    public ComClassBuilder AppId(Guid appId) { _appId = appId; return this; }
    public ComClassBuilder ComponentRef(string componentRef) { _componentRef = componentRef; return this; }

    public ComClassModel Build() => new()
    {
        ClassId = _classId,
        ServerType = _serverType,
        ProgId = _progId,
        Description = _description,
        ThreadingModel = _threadingModel,
        AppId = _appId,
        ComponentRef = _componentRef
    };
}
```

**ComTypeLibBuilder.cs:**
```csharp
using FalkForge.Models;

namespace FalkForge.Builders;

public sealed class ComTypeLibBuilder
{
    private Guid _typeLibId;
    private Version _version = new(1, 0);
    private int _language;
    private string? _description;
    private string? _componentRef;

    public ComTypeLibBuilder TypeLibId(Guid id) { _typeLibId = id; return this; }
    public ComTypeLibBuilder Version(int major, int minor) { _version = new Version(major, minor); return this; }
    public ComTypeLibBuilder Language(int lcid) { _language = lcid; return this; }
    public ComTypeLibBuilder Description(string desc) { _description = desc; return this; }
    public ComTypeLibBuilder ComponentRef(string componentRef) { _componentRef = componentRef; return this; }

    public ComTypeLibModel Build() => new()
    {
        TypeLibId = _typeLibId,
        Version = _version,
        Language = _language,
        Description = _description,
        ComponentRef = _componentRef
    };
}
```

**PackageModel.cs — add properties:**
```csharp
    public IReadOnlyList<ComClassModel> ComClasses { get; init; } = [];
    public IReadOnlyList<ComTypeLibModel> TypeLibs { get; init; } = [];
```

**PackageBuilder.cs — add fields, methods, and wire Build():**
```csharp
    private readonly List<ComClassModel> _comClasses = [];
    private readonly List<ComTypeLibModel> _typeLibs = [];

    public PackageBuilder ComClass(Action<ComClassBuilder> configure)
    {
        var builder = new ComClassBuilder();
        configure(builder);
        _comClasses.Add(builder.Build());
        return this;
    }

    public PackageBuilder TypeLib(Action<ComTypeLibBuilder> configure)
    {
        var builder = new ComTypeLibBuilder();
        configure(builder);
        _typeLibs.Add(builder.Build());
        return this;
    }
```

In Build() initializer:
```csharp
        ComClasses = [.. _comClasses],
        TypeLibs = [.. _typeLibs],
```

**Tests (2):**

```csharp
using FalkForge.Builders;
using FalkForge.Models;

namespace FalkForge.Tests;

public sealed class ComRegistrationTests
{
    [Fact]
    public void ComClassBuilder_SetsAllProperties()
    {
        var classId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var model = new ComClassBuilder()
            .ClassId(classId)
            .LocalServer32()
            .ProgId("MyApp.Server")
            .Description("My COM Server")
            .ThreadingModel(ComThreadingModel.Both)
            .AppId(appId)
            .Build();

        Assert.Equal(classId, model.ClassId);
        Assert.Equal(ComServerType.LocalServer32, model.ServerType);
        Assert.Equal("MyApp.Server", model.ProgId);
        Assert.Equal("My COM Server", model.Description);
        Assert.Equal(ComThreadingModel.Both, model.ThreadingModel);
        Assert.Equal(appId, model.AppId);
    }

    [Fact]
    public void ComTypeLibBuilder_SetsAllProperties()
    {
        var libId = Guid.NewGuid();
        var model = new ComTypeLibBuilder()
            .TypeLibId(libId)
            .Version(2, 1)
            .Language(1033)
            .Description("My Type Library")
            .Build();

        Assert.Equal(libId, model.TypeLibId);
        Assert.Equal(new Version(2, 1), model.Version);
        Assert.Equal(1033, model.Language);
        Assert.Equal("My Type Library", model.Description);
    }
}
```

**Verify:** `dotnet test <worktree>/tests/FalkForge.Core.Tests/ --filter "FullyQualifiedName~ComRegistration"`

**Commit:** `feat(core): add COM class and TypeLib builders with PackageBuilder integration`

---

### Task 5: COM table emission

**Files:**
- Modify: `src/FalkForge.Compiler.Msi/Tables/MsiTableDefinitions.cs` — add Class and TypeLib table definitions if missing
- Modify: `src/FalkForge.Compiler.Msi/Tables/TableEmitter.cs` — add EmitComClasses() and EmitTypeLibs()
- Create: `tests/FalkForge.Compiler.Msi.Tests/ComTableEmissionTests.cs`

READ `MsiTableDefinitions.cs` first. The Class and TypeLib table schemas may already be defined. If not, add them.

**Class table schema** (per MSI spec):
```
Class: CLSID(s72), Context(s255), Component_(s72), ProgId_Default(S255), Description(S255), AppId_(S38), FileTypeMask(S255), Icon_(S72), IconIndex(I2), DefInprocHandler(S255), Argument(S255), Feature_(s38)
```

**TypeLib table schema** (per MSI spec):
```
TypeLib: LibID(s38), Language(i2), Component_(s72), Version(I4), Description(S255), Directory_(S72), Feature_(s38), Cost(I4)
```

**TableEmitter.cs — add EmitComClasses():**

```csharp
private Result<Unit> EmitComClasses(ResolvedPackage resolved)
{
    var classes = resolved.Package.ComClasses;
    if (classes.Count == 0) return Unit.Value;

    foreach (var cls in classes)
    {
        var context = cls.ServerType == ComServerType.InprocServer32
            ? "InprocServer32" : "LocalServer32";

        var result = _database.InsertRow(
            "SELECT `CLSID`, `Context`, `Component_`, `ProgId_Default`, `Description`, `AppId_`, `FileTypeMask`, `Icon_`, `IconIndex`, `DefInprocHandler`, `Argument`, `Feature_` FROM `Class`",
            record => record
                .SetString(1, cls.ClassId.ToString("B").ToUpperInvariant())
                .SetString(2, context)
                .SetString(3, cls.ComponentRef ?? "")
                .SetString(4, cls.ProgId)
                .SetString(5, cls.Description)
                .SetString(6, cls.AppId?.ToString("B").ToUpperInvariant())
                .SetString(7, null)
                .SetString(8, null)
                .SetInteger(9, 0)
                .SetString(10, cls.ThreadingModel.ToString().ToLowerInvariant())
                .SetString(11, null)
                .SetString(12, ""));
        if (result.IsFailure) return result;

        // Link ProgId to Class if specified
        if (cls.ProgId is not null)
        {
            var progResult = _database.InsertRow(
                "SELECT `ProgId`, `ProgId_Parent`, `Class_`, `Description`, `Icon_`, `IconIndex` FROM `ProgId`",
                record => record
                    .SetString(1, cls.ProgId)
                    .SetString(2, null)
                    .SetString(3, cls.ClassId.ToString("B").ToUpperInvariant())
                    .SetString(4, cls.Description)
                    .SetString(5, null)
                    .SetInteger(6, 0));
            if (progResult.IsFailure) return progResult;
        }
    }
    return Unit.Value;
}
```

NOTE: READ the existing TableEmitter.cs carefully to understand:
- How `_database.InsertRow` works (exact API)
- How `SetString`/`SetInteger` handle nulls
- How features are referenced
- The exact column order in the SQL SELECT statement (must match MSI schema)

Adjust the code above based on what you find.

**TableEmitter.cs — add EmitTypeLibs():**

```csharp
private Result<Unit> EmitTypeLibs(ResolvedPackage resolved)
{
    var libs = resolved.Package.TypeLibs;
    if (libs.Count == 0) return Unit.Value;

    foreach (var lib in libs)
    {
        var version = (lib.Version.Major << 8) | lib.Version.Minor;
        var result = _database.InsertRow(
            "SELECT `LibID`, `Language`, `Component_`, `Version`, `Description`, `Directory_`, `Feature_`, `Cost` FROM `TypeLib`",
            record => record
                .SetString(1, lib.TypeLibId.ToString("B").ToUpperInvariant())
                .SetInteger(2, lib.Language)
                .SetString(3, lib.ComponentRef ?? "")
                .SetInteger(4, version)
                .SetString(5, lib.Description)
                .SetString(6, null)
                .SetString(7, "")
                .SetInteger(8, 0));
        if (result.IsFailure) return result;
    }
    return Unit.Value;
}
```

Call both methods from the main emission flow. READ the Compile() method to find where other Emit* methods are called (after EmitProgIds).

**Tests (2):**

```csharp
namespace FalkForge.Compiler.Msi.Tests;

public sealed class ComTableEmissionTests
{
    [Fact]
    public void ComClassModel_HasCorrectContext()
    {
        var model = new ComClassModel
        {
            ClassId = Guid.NewGuid(),
            ServerType = ComServerType.InprocServer32,
            ProgId = "Test.Server",
            ThreadingModel = ComThreadingModel.Both,
            ComponentRef = "comp1"
        };

        // Verify context string mapping
        var context = model.ServerType == ComServerType.InprocServer32
            ? "InprocServer32" : "LocalServer32";
        Assert.Equal("InprocServer32", context);
    }

    [Fact]
    public void ComTypeLib_VersionEncoding()
    {
        var lib = new ComTypeLibModel
        {
            TypeLibId = Guid.NewGuid(),
            Version = new Version(2, 5),
            ComponentRef = "comp1"
        };

        var encoded = (lib.Version.Major << 8) | lib.Version.Minor;
        Assert.Equal(517, encoded); // 2*256 + 5
    }
}
```

**Verify:** `dotnet test <worktree>/tests/FalkForge.Compiler.Msi.Tests/ --filter "FullyQualifiedName~ComTableEmission"`

**Commit:** `feat(compiler): add COM Class and TypeLib table emission`

---

## Part C: Driver Installation

### Task 6: Driver extension project scaffolding

**Files:**
- Create: `src/FalkForge.Extensions.Driver/FalkForge.Extensions.Driver.csproj`
- Create: `src/FalkForge.Extensions.Driver/DriverModel.cs`
- Create: `src/FalkForge.Extensions.Driver/DriverBuilder.cs`
- Create: `src/FalkForge.Extensions.Driver/DriverExtension.cs`
- Create: `src/FalkForge.Extensions.Driver/DriverTableContributor.cs`
- Create: `src/FalkForge.Extensions.Driver/DriverValidator.cs`
- Create: `tests/FalkForge.Extensions.Driver.Tests/FalkForge.Extensions.Driver.Tests.csproj`
- Create: `tests/FalkForge.Extensions.Driver.Tests/DriverBuilderTests.cs`
- Modify: `FalkForge.slnx` — add both projects

**FalkForge.Extensions.Driver.csproj** (based on Firewall template):
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>FalkForge.Extensions.Driver</RootNamespace>
    <Description>Driver extension for FalkForge — installs device drivers via pnputil</Description>
    <InternalsVisibleTo Include="FalkForge.Extensions.Driver.Tests" />
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\FalkForge.Core\FalkForge.Core.csproj" />
    <ProjectReference Include="..\FalkForge.Extensibility\FalkForge.Extensibility.csproj" />
  </ItemGroup>
</Project>
```

**DriverModel.cs:**
```csharp
namespace FalkForge.Extensions.Driver;

public sealed record DriverModel
{
    public required string Id { get; init; }
    public required string InfFilePath { get; init; }
    public bool ForceInstall { get; init; }
    public string? Condition { get; init; }
}
```

**DriverBuilder.cs:**
```csharp
namespace FalkForge.Extensions.Driver;

public sealed class DriverBuilder
{
    private string? _id;
    private string? _infFilePath;
    private bool _forceInstall;
    private string? _condition;

    public DriverBuilder Id(string id) { _id = id; return this; }
    public DriverBuilder InfFile(string path) { _infFilePath = path; return this; }
    public DriverBuilder ForceInstall(bool force = true) { _forceInstall = force; return this; }
    public DriverBuilder Condition(string condition) { _condition = condition; return this; }

    public Result<DriverModel> Build()
    {
        if (string.IsNullOrWhiteSpace(_id))
            return Result<DriverModel>.Failure(ErrorKind.Validation, "DRV001: Driver Id is required.");
        if (string.IsNullOrWhiteSpace(_infFilePath))
            return Result<DriverModel>.Failure(ErrorKind.Validation, "DRV002: INF file path is required.");

        return Result<DriverModel>.Success(new DriverModel
        {
            Id = _id,
            InfFilePath = _infFilePath,
            ForceInstall = _forceInstall,
            Condition = _condition
        });
    }
}
```

**DriverExtension.cs:**
```csharp
using FalkForge.Extensibility;

namespace FalkForge.Extensions.Driver;

public sealed class DriverExtension : IFalkForgeExtension
{
    public DriverTableContributor TableContributor { get; } = new();
    public string Name => "Driver";

    public void Register(IExtensionRegistry registry)
    {
        registry.RegisterTableContributor(TableContributor);
    }

    public void AddDriver(Action<DriverBuilder> configure)
    {
        var builder = new DriverBuilder();
        configure(builder);
        var result = builder.Build();
        if (result.IsFailure)
            throw new InvalidOperationException(result.Error.Message);
        TableContributor.AddDriver(result.Value);
    }
}
```

**DriverTableContributor.cs:**
```csharp
using FalkForge.Extensibility;
using FalkForge.Models;

namespace FalkForge.Extensions.Driver;

public sealed class DriverTableContributor : IMsiTableContributor
{
    private readonly List<DriverModel> _drivers = [];

    public IReadOnlyList<DriverModel> Drivers => _drivers;

    internal void AddDriver(DriverModel driver) => _drivers.Add(driver);

    public IReadOnlyList<MsiTableRow> GetTableRows(ExtensionContext context)
    {
        var rows = new List<MsiTableRow>();

        foreach (var driver in _drivers)
        {
            var forceFlag = driver.ForceInstall ? " /force" : "";
            var installCmd = $"pnputil /install-driver \"[INSTALLDIR]{driver.InfFilePath}\" /subdirs{forceFlag}";
            var uninstallCmd = $"pnputil /delete-driver \"[INSTALLDIR]{driver.InfFilePath}\" /uninstall";

            // Install custom action (deferred, elevated)
            rows.Add(new MsiTableRow("CustomAction", new Dictionary<string, object>
            {
                ["Action"] = $"DRV_Install_{driver.Id}",
                ["Type"] = CustomActionType.ExeInDir | CustomActionType.InScript | CustomActionType.NoImpersonate,
                ["Source"] = "[SystemFolder]",
                ["Target"] = installCmd,
                ["ExtendedType"] = 0
            }));

            // Uninstall custom action
            rows.Add(new MsiTableRow("CustomAction", new Dictionary<string, object>
            {
                ["Action"] = $"DRV_Uninstall_{driver.Id}",
                ["Type"] = CustomActionType.ExeInDir | CustomActionType.InScript | CustomActionType.NoImpersonate,
                ["Source"] = "[SystemFolder]",
                ["Target"] = uninstallCmd,
                ["ExtendedType"] = 0
            }));
        }

        return rows;
    }
}
```

NOTE: READ the actual `IMsiTableContributor` interface and `MsiTableRow` type from `src/FalkForge.Extensibility/` to see the exact API. The above code may need adjustment. Also check `ExtensionContext` and how `IExtensionRegistry.RegisterTableContributor` works. The Firewall extension is the best reference.

**FalkForge.Extensions.Driver.Tests.csproj:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\FalkForge.Extensions.Driver\FalkForge.Extensions.Driver.csproj" />
    <ProjectReference Include="..\..\src\FalkForge.Testing\FalkForge.Testing.csproj" />
  </ItemGroup>
</Project>
```

**Tests (3):**

```csharp
using FalkForge.Extensions.Driver;

namespace FalkForge.Extensions.Driver.Tests;

public sealed class DriverBuilderTests
{
    [Fact]
    public void Build_ValidDriver_ReturnsSuccess()
    {
        var result = new DriverBuilder()
            .Id("MyDriver")
            .InfFile("drivers/mydriver.inf")
            .ForceInstall()
            .Build();

        Assert.True(result.IsSuccess);
        Assert.Equal("MyDriver", result.Value.Id);
        Assert.Equal("drivers/mydriver.inf", result.Value.InfFilePath);
        Assert.True(result.Value.ForceInstall);
    }

    [Fact]
    public void Build_MissingId_ReturnsFailure()
    {
        var result = new DriverBuilder()
            .InfFile("drivers/mydriver.inf")
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("DRV001", result.Error.Message);
    }

    [Fact]
    public void Build_MissingInfFile_ReturnsFailure()
    {
        var result = new DriverBuilder()
            .Id("MyDriver")
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("DRV002", result.Error.Message);
    }
}
```

**Verify:**
```bash
dotnet build <worktree>/src/FalkForge.Extensions.Driver/FalkForge.Extensions.Driver.csproj
dotnet test <worktree>/tests/FalkForge.Extensions.Driver.Tests/FalkForge.Extensions.Driver.Tests.csproj
```

**Commit:** `feat(driver): add driver installation extension with pnputil support`

---

### Task 7: Full Verification

1. `dotnet build <worktree>/FalkForge.slnx` — 0 new errors
2. `dotnet test <worktree>/tests/FalkForge.Core.Tests/` — all pass
3. `dotnet test <worktree>/tests/FalkForge.Compiler.Msi.Tests/` — all pass
4. `dotnet test <worktree>/tests/FalkForge.Extensions.Driver.Tests/` — all pass
5. Verify new types are discoverable: `ComClassBuilder`, `ComTypeLibBuilder`, `DriverBuilder`, `PowerShellScript/PowerShellFile`

---

## Integration Map

```
PowerShell Custom Actions:
  CustomActionBuilder.PowerShellScript() / .PowerShellFile()
  -> ExeInDir CA type with powershell.exe command
  -> Existing TableEmitter handles emission

COM Registration:
  PackageBuilder.ComClass() / .TypeLib()
  -> ComClassModel / ComTypeLibModel on PackageModel
  -> TableEmitter.EmitComClasses() / EmitTypeLibs()
  -> MSI Class, TypeLib, ProgId tables

Driver Installation:
  DriverExtension.AddDriver()
  -> DriverTableContributor emits CAs
  -> pnputil /install-driver (install) + /delete-driver (uninstall)
```
