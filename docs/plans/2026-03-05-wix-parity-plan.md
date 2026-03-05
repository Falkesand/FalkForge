# WiX Parity — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Close remaining gaps between FalkInstaller and WiX Toolset across Bundle/Burn engine features and MSI extensions.

**Architecture:** 9 features organized into 4 parallel work streams. Engine features extend existing detection, cache, and execution pipelines. Extensions follow the established IMsiTableContributor pattern. All features use Result<T>, TDD, and plug into existing fluent API builders.

**Tech Stack:** C#, .NET, TDD, Result<T> pattern, IProcessRunner, IMsiTableContributor

---

## Work Streams (Parallelizable)

| Stream | Features | Independence |
|--------|----------|-------------|
| A | Advanced Detection, EULA/License Flow | Detection pipeline |
| B | Payload Signing, Network Throttling | Download/cache pipeline |
| C | Bundle Chaining, Patch Slipstreaming | Execution pipeline |
| D | Scheduled Tasks, Performance Counters, ODBC Extensions | Extension system |

Streams A-D can run in parallel git worktrees since they touch different subsystems.

---

## Stream A: Detection & License

### Task A1: SearchCondition Model Types

**Files:**
- Create: `src/FalkForge.Engine.Protocol/Manifest/SearchCondition.cs`
- Create: `src/FalkForge.Engine.Protocol/Manifest/SearchConditionType.cs`
- Create: `src/FalkForge.Engine.Protocol/Manifest/DetectionMode.cs`
- Modify: `src/FalkForge.Engine.Protocol/Manifest/PackageInfo.cs`

**Design:**
```csharp
// SearchConditionType.cs
namespace FalkForge.Engine.Protocol.Manifest;

public enum SearchConditionType
{
    FileExists,
    FileVersion,
    DirectoryExists,
    RegistryValue,
    ProductSearch
}

// DetectionMode.cs
namespace FalkForge.Engine.Protocol.Manifest;

public enum DetectionMode
{
    Default,       // Use built-in ProductCode detection
    SearchOnly,    // Use only SearchConditions
    Combined       // Both must agree
}

// SearchCondition.cs
namespace FalkForge.Engine.Protocol.Manifest;

public sealed class SearchCondition
{
    public required SearchConditionType Type { get; init; }
    public required string Path { get; init; }
    public string? Value { get; init; }
    public string? Comparison { get; init; } // "=", "<", ">", "<=", ">="
}
```

**PackageInfo additions:**
```csharp
public DetectionMode DetectionMode { get; init; } = DetectionMode.Default;
public IReadOnlyList<SearchCondition> SearchConditions { get; init; } = [];
```

**Step 1:** Write tests for SearchCondition model validation (type, path required)
**Step 2:** Create the model types
**Step 3:** Add properties to PackageInfo
**Step 4:** Verify build, commit

---

### Task A2: Search Condition Evaluators — Tests

**Files:**
- Create: `tests/FalkForge.Engine.Tests/Detection/SearchConditionEvaluatorTests.cs`

**Tests to write (10 tests):**
1. `FileExists_ExistingFile_ReturnsTrue`
2. `FileExists_MissingFile_ReturnsFalse`
3. `FileVersion_MatchingVersion_ReturnsTrue`
4. `FileVersion_OlderVersion_ReturnsFalse`
5. `DirectoryExists_ExistingDir_ReturnsTrue`
6. `DirectoryExists_MissingDir_ReturnsFalse`
7. `RegistryValue_MatchingValue_ReturnsTrue`
8. `RegistryValue_MissingKey_ReturnsFalse`
9. `ProductSearch_InstalledProduct_ReturnsTrue`
10. `ProductSearch_MissingProduct_ReturnsFalse`

Use filesystem abstraction (IFileSystem interface or similar) for testability. Registry tests use the existing mock registry from `tests/FalkForge.Engine.Tests/Mocks/`.

**Step 1:** Write all failing tests
**Step 2:** Commit

---

### Task A3: Search Condition Evaluators — Implementation

**Files:**
- Create: `src/FalkForge.Engine/Detection/SearchConditionEvaluator.cs`
- Create: `src/FalkForge.Engine/Detection/IFileSystemProvider.cs`

**Design:**
```csharp
namespace FalkForge.Engine.Detection;

public interface IFileSystemProvider
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    Version? GetFileVersion(string path);
}

public sealed class SearchConditionEvaluator(IFileSystemProvider fileSystem, IRegistry registry)
{
    public Result<bool> Evaluate(SearchCondition condition)
    {
        return condition.Type switch
        {
            SearchConditionType.FileExists => fileSystem.FileExists(condition.Path),
            SearchConditionType.FileVersion => EvaluateFileVersion(condition),
            SearchConditionType.DirectoryExists => fileSystem.DirectoryExists(condition.Path),
            SearchConditionType.RegistryValue => EvaluateRegistry(condition),
            SearchConditionType.ProductSearch => EvaluateProductSearch(condition),
            _ => Result<bool>.Failure(ErrorKind.DetectionError, $"Unknown search type: {condition.Type}")
        };
    }
}
```

**Step 1:** Implement evaluator, all tests pass
**Step 2:** Commit

---

### Task A4: Wire Search Conditions into PackageDetector

**Files:**
- Modify: `src/FalkForge.Engine/Detection/PackageDetector.cs`
- Modify: `tests/FalkForge.Engine.Tests/Detection/PackageDetectorTests.cs` (add tests)

**Changes:**
- PackageDetector gains `SearchConditionEvaluator` dependency
- In `Detect()`, after existing ProductCode detection, evaluate SearchConditions based on DetectionMode
- `DetectionMode.Default`: existing behavior only
- `DetectionMode.SearchOnly`: only SearchConditions
- `DetectionMode.Combined`: both must agree

**Tests (4 tests):**
1. `Detect_DefaultMode_IgnoresSearchConditions`
2. `Detect_SearchOnlyMode_UsesOnlySearchConditions`
3. `Detect_CombinedMode_BothMustAgree`
4. `Detect_SearchConditionFails_ReturnsNotInstalled`

**Step 1:** Write failing tests
**Step 2:** Implement, verify pass
**Step 3:** Commit

---

### Task A5: Fluent API for Search Conditions

**Files:**
- Modify: `src/FalkForge.Compiler.Bundle/Builders/BundlePackageBuilder.cs`
- Create: `tests/FalkForge.Compiler.Bundle.Tests/Builders/SearchConditionBuilderTests.cs`

**API Design:**
```csharp
chain.MsiPackage("setup.msi", p => p
    .DetectionMode(DetectionMode.Combined)
    .SearchCondition(s => s
        .FileExists(@"C:\Program Files\MyApp\app.exe"))
    .SearchCondition(s => s
        .FileVersion(@"C:\Program Files\MyApp\app.exe", ">=", "2.0.0"))
);
```

**Tests (4 tests):**
1. `Build_WithFileExistsSearch_SetsSearchCondition`
2. `Build_WithFileVersionSearch_SetsVersionComparison`
3. `Build_WithDetectionMode_SetsMode`
4. `Build_MultipleSearchConditions_PreservesAll`

**Step 1:** Write failing tests
**Step 2:** Implement builder methods
**Step 3:** Commit

---

### Task A6: EULA/License Flow — Protocol Messages

**Files:**
- Create: `src/FalkForge.Engine.Protocol/Messages/LicenseMessage.cs`
- Modify: `src/FalkForge.Engine.Protocol/MessageType.cs`
- Modify: `src/FalkForge.Engine.Protocol/Serialization/MessageSerializer.cs`
- Modify: `src/FalkForge.Engine.Protocol/Serialization/MessageDeserializer.cs`

**Design:**
```csharp
// LicenseMessage.cs
namespace FalkForge.Engine.Protocol.Messages;

public sealed class LicenseMessage
{
    public required LicenseAction Action { get; init; }
    public string? LicenseContent { get; init; }
}

public enum LicenseAction
{
    Required,   // Engine → UI: license acceptance needed
    Accepted,   // UI → Engine: user accepted
    Declined    // UI → Engine: user declined
}
```

Add `License` to `MessageType` enum.

**Step 1:** Write serialization round-trip tests
**Step 2:** Implement message type and serialization
**Step 3:** Commit

---

### Task A7: EULA/License Flow — Engine Gate

**Files:**
- Modify: `src/FalkForge.Engine/EngineHost.cs` or relevant phase handler
- Create: `tests/FalkForge.Engine.Tests/Phases/LicenseGateTests.cs`

**Design:**
- After Detecting phase, before Planning:
  - If `manifest.LicenseFile` is set → send `LicenseMessage(Action=Required)`
  - Wait for `LicenseMessage(Action=Accepted)` or `Declined`
  - If Declined → abort with user-cancelled error
  - Silent mode → auto-accept

**Tests (4 tests):**
1. `LicenseRequired_UserAccepts_ProceedsToPlan`
2. `LicenseRequired_UserDeclines_Aborts`
3. `LicenseRequired_SilentMode_AutoAccepts`
4. `NoLicense_SkipsGate`

**Step 1:** Write failing tests
**Step 2:** Implement gate logic
**Step 3:** Commit

---

## Stream B: Download & Signing

### Task B1: IAuthenticodeValidator Interface + Tests

**Files:**
- Create: `src/FalkForge.Platform.Windows/IAuthenticodeValidator.cs`
- Create: `src/FalkForge.Platform.Windows/AuthenticodeValidator.cs`
- Create: `tests/FalkForge.Engine.Tests/Cache/AuthenticodeValidatorTests.cs`

**Design:**
```csharp
namespace FalkForge.Platform.Windows;

public interface IAuthenticodeValidator
{
    Result<Unit> ValidateSignature(string filePath, string? expectedThumbprint);
}

public sealed class AuthenticodeValidator : IAuthenticodeValidator
{
    // P/Invoke WinVerifyTrust for signature validation
    // If expectedThumbprint is set, also verify certificate thumbprint matches
}
```

**Tests (5 tests):**
1. `ValidFile_WithValidSignature_ReturnsSuccess` (mock-based)
2. `UnsignedFile_ReturnsFailure`
3. `WrongThumbprint_ReturnsFailure`
4. `NullThumbprint_SkipsThumbprintCheck`
5. `MissingFile_ReturnsFailure`

**Step 1:** Write interface and mock-based failing tests
**Step 2:** Implement (with P/Invoke stubs)
**Step 3:** Commit

---

### Task B2: Wire Authenticode into PackageCache

**Files:**
- Modify: `src/FalkForge.Engine/Cache/PackageCache.cs`
- Modify: `src/FalkForge.Engine.Protocol/Manifest/PackageInfo.cs` (add `AuthenticodeThumbprint`)
- Create: `tests/FalkForge.Engine.Tests/Cache/PackageCacheSignatureTests.cs`

**Changes:**
- `PackageInfo` gains: `public string? AuthenticodeThumbprint { get; init; }`
- `PackageCache.CachePackage()`: after SHA-256 check, if `AuthenticodeThumbprint` is set, call `IAuthenticodeValidator.ValidateSignature()`
- Cache constructor gains optional `IAuthenticodeValidator` parameter

**Tests (3 tests):**
1. `Cache_WithThumbprint_ValidatesSignature`
2. `Cache_WithoutThumbprint_SkipsValidation`
3. `Cache_SignatureInvalid_ReturnsFailure`

**Step 1:** Write failing tests
**Step 2:** Implement, verify pass
**Step 3:** Commit

---

### Task B3: Fluent API for Authenticode

**Files:**
- Modify: `src/FalkForge.Compiler.Bundle/Builders/BundlePackageBuilder.cs`
- Create: `tests/FalkForge.Compiler.Bundle.Tests/Builders/AuthenticodeBuilderTests.cs`

**API:**
```csharp
chain.ExePackage("setup.exe", p => p
    .AuthenticodeThumbprint("A1B2C3...")
);
```

**Step 1:** Write test, implement, commit

---

### Task B4: Network Throttling — TokenBucket

**Files:**
- Create: `src/FalkForge.Engine/Download/TokenBucket.cs`
- Create: `tests/FalkForge.Engine.Tests/Download/TokenBucketTests.cs`

**Design:**
```csharp
namespace FalkForge.Engine.Download;

internal sealed class TokenBucket(long bytesPerSecond)
{
    public ValueTask WaitForTokensAsync(int bytes, CancellationToken ct);
}
```

**Tests (4 tests):**
1. `Unlimited_ReturnsImmediately` (bytesPerSecond = 0)
2. `WithinBudget_ReturnsImmediately`
3. `ExceedsBudget_Waits`
4. `Cancellation_ThrowsOCE`

**Step 1:** Write failing tests
**Step 2:** Implement
**Step 3:** Commit

---

### Task B5: Wire Throttling into PayloadDownloader

**Files:**
- Modify: `src/FalkForge.Engine/Download/PayloadDownloader.cs`
- Modify: `src/FalkForge.Engine.Protocol/Manifest/InstallerManifest.cs` (add `MaxBytesPerSecond`)

**Changes:**
- `InstallerManifest` gains: `public long MaxBytesPerSecond { get; init; }`
- `PayloadDownloader` constructor gains optional `TokenBucket`
- In download loop, call `tokenBucket.WaitForTokensAsync(bytesRead, ct)` after each chunk

**Step 1:** Write integration test
**Step 2:** Wire throttle
**Step 3:** Commit

---

### Task B6: Fluent API for Throttling

**Files:**
- Modify: `src/FalkForge.Compiler.Bundle/Builders/BundleBuilder.cs`

**API:**
```csharp
new BundleBuilder()
    .DownloadThrottle(bytesPerSecond: 1_048_576) // 1 MB/s
```

**Step 1:** Test + implement, commit

---

## Stream C: Execution Pipeline

### Task C1: Prerequisite Package Model

**Files:**
- Modify: `src/FalkForge.Engine.Protocol/Manifest/PackageInfo.cs`
- Modify: `src/FalkForge.Compiler.Bundle/Builders/BundlePackageBuilder.cs`

**Changes:**
- `PackageInfo` gains: `public bool IsPrerequisite { get; init; }`
- `BundlePackageBuilder` gains: `.Prerequisite(bool)`

**Step 1:** Write builder test
**Step 2:** Add property and builder method
**Step 3:** Commit

---

### Task C2: Prerequisite Detection & Planning

**Files:**
- Modify: `src/FalkForge.Engine/Planning/Planner.cs`
- Create: `tests/FalkForge.Engine.Tests/Planning/PrerequisitePlannerTests.cs`

**Changes:**
- Planner separates packages into prereqs and main chain
- Prereqs are planned first in execution order
- All prereqs are implicitly Vital

**Tests (4 tests):**
1. `Plan_WithPrereqs_PrereqsFirst`
2. `Plan_PrereqAlreadyInstalled_Skipped`
3. `Plan_PrereqsImplicitlyVital`
4. `Plan_NoPrereqs_UnchangedBehavior`

**Step 1:** Write failing tests
**Step 2:** Implement
**Step 3:** Commit

---

### Task C3: Prerequisite Execution in ApplyingHandler

**Files:**
- Modify: `src/FalkForge.Engine/Phases/ApplyingHandler.cs`
- Create: `tests/FalkForge.Engine.Tests/Phases/PrerequisiteExecutionTests.cs`

**Changes:**
- ApplyingHandler executes prereq packages before main chain
- Prereq failure aborts entire install (no rollback of prereqs — they're independent)
- Progress reporting distinguishes prereq vs main phase

**Tests (3 tests):**
1. `Apply_PrereqsExecuteBeforeMainChain`
2. `Apply_PrereqFailure_AbortsEntireInstall`
3. `Apply_PrereqSuccess_ContinuesToMainChain`

**Step 1:** Write failing tests
**Step 2:** Implement
**Step 3:** Commit

---

### Task C4: Patch Slipstreaming Model

**Files:**
- Modify: `src/FalkForge.Engine.Protocol/Manifest/PackageInfo.cs`
- Modify: `src/FalkForge.Compiler.Bundle/Builders/MspPackageBuilder.cs`

**Changes:**
- `PackageInfo` gains: `public string? SlipstreamTargetId { get; init; }`
- `MspPackageBuilder` gains: `.SlipstreamTarget(string msiPackageId)`

**Step 1:** Write builder test
**Step 2:** Add property and method
**Step 3:** Commit

---

### Task C5: Patch Slipstreaming in MsiExecutor — Tests

**Files:**
- Create: `tests/FalkForge.Engine.Tests/Execution/SlipstreamTests.cs`

**Tests (4 tests):**
1. `MsiInstall_WithSlipstream_AddsPatchProperty` — PATCH="path1" in msiexec args
2. `MsiInstall_MultipleSlipstreams_SemicolonSeparated` — PATCH="path1;path2"
3. `MsiInstall_NoSlipstream_NoPatchProperty`
4. `MsiUninstall_IgnoresSlipstream`

**Step 1:** Write failing tests
**Step 2:** Commit

---

### Task C6: Patch Slipstreaming in MsiExecutor — Implementation

**Files:**
- Modify: `src/FalkForge.Engine/Execution/MsiExecutor.cs`
- Modify: `src/FalkForge.Engine/Execution/PackageExecutor.cs` (pass slipstream info)

**Changes:**
- `MsiExecutor.ExecuteAsync` gains access to slipstream MSP paths
- When building install arguments, if slipstream patches exist, append `PATCH="path1;path2"` to msiexec command line
- PackageExecutor resolves SlipstreamTargetId to actual cached MSP paths before calling MsiExecutor

**Step 1:** Implement, verify all tests pass
**Step 2:** Commit

---

## Stream D: Extensions

### Task D1: Scheduled Tasks Extension — Model + Table

**Files:**
- Create: `src/FalkForge.Extensions.Util/ScheduledTask/ScheduledTaskModel.cs`
- Create: `src/FalkForge.Extensions.Util/ScheduledTask/ScheduledTaskTableContributor.cs`
- Create: `tests/FalkForge.Extensions.Util.Tests/ScheduledTask/ScheduledTaskTableContributorTests.cs`

**Design:**
```csharp
public sealed class ScheduledTaskModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Command { get; init; }
    public string? Arguments { get; init; }
    public string? WorkingDirectory { get; init; }
    public required ScheduledTaskTriggerType TriggerType { get; init; }
    public string? Schedule { get; init; } // cron-like or interval
    public string? RunAsUser { get; init; }
    public bool RunElevated { get; init; }
}

public enum ScheduledTaskTriggerType
{
    OnInstall,
    OnLogin,
    OnSchedule,
    OnBoot
}
```

Table: `FalkForgeScheduledTask` with custom action at install time.

**Tests (3 tests):**
1. `GetRows_SingleTask_ReturnsCorrectRow`
2. `GetRows_WithSchedule_IncludesScheduleColumn`
3. `GetRows_Empty_ReturnsNoRows`

**Step 1:** Write model + failing tests
**Step 2:** Implement table contributor
**Step 3:** Commit

---

### Task D2: Scheduled Tasks Extension — Builder + Registration

**Files:**
- Modify: `src/FalkForge.Extensions.Util/UtilExtension.cs`
- Create: `src/FalkForge.Extensions.Util/ScheduledTask/ScheduledTaskBuilder.cs`
- Create: `tests/FalkForge.Extensions.Util.Tests/ScheduledTask/ScheduledTaskBuilderTests.cs`

**Fluent API:**
```csharp
component.ScheduledTask("MyTask", t => t
    .Command(@"[INSTALLFOLDER]app.exe")
    .Arguments("--daemon")
    .TriggerOnSchedule("0 */6 * * *")
    .RunElevated()
);
```

**Step 1:** Write builder tests
**Step 2:** Implement builder and register in UtilExtension
**Step 3:** Commit

---

### Task D3: Performance Counters Extension — Model + Table

**Files:**
- Create: `src/FalkForge.Extensions.Util/PerfCounter/PerfCounterModel.cs`
- Create: `src/FalkForge.Extensions.Util/PerfCounter/PerfCounterTableContributor.cs`
- Create: `tests/FalkForge.Extensions.Util.Tests/PerfCounter/PerfCounterTableContributorTests.cs`

**Design:**
```csharp
public sealed class PerfCounterModel
{
    public required string Id { get; init; }
    public required string CategoryName { get; init; }
    public required string CounterName { get; init; }
    public required PerfCounterType CounterType { get; init; }
    public string? CategoryHelp { get; init; }
    public string? CounterHelp { get; init; }
}

public enum PerfCounterType
{
    NumberOfItems32,
    NumberOfItems64,
    RateOfCountsPerSecond32,
    RateOfCountsPerSecond64,
    AverageTimer32,
    AverageCount64
}
```

Table: `FalkForgePerfCounter` with custom action.

**Tests (2 tests):**
1. `GetRows_SingleCounter_ReturnsCorrectRow`
2. `GetRows_MultipleCounters_ReturnsAllRows`

**Step 1:** Write model + failing tests
**Step 2:** Implement
**Step 3:** Commit

---

### Task D4: Performance Counters — Builder

**Files:**
- Create: `src/FalkForge.Extensions.Util/PerfCounter/PerfCounterBuilder.cs`
- Create: `tests/FalkForge.Extensions.Util.Tests/PerfCounter/PerfCounterBuilderTests.cs`

**Fluent API:**
```csharp
component.PerformanceCounter("RequestCount", c => c
    .CategoryName("MyApp")
    .CounterType(PerfCounterType.RateOfCountsPerSecond32)
    .CategoryHelp("MyApp performance counters")
    .CounterHelp("Number of requests per second")
);
```

**Step 1:** Write tests, implement, commit

---

### Task D5: ODBC Extension — Model + Table

**Files:**
- Create: `src/FalkForge.Extensions.Util/Odbc/OdbcDriverModel.cs`
- Create: `src/FalkForge.Extensions.Util/Odbc/OdbcDataSourceModel.cs`
- Create: `src/FalkForge.Extensions.Util/Odbc/OdbcTableContributor.cs`
- Create: `tests/FalkForge.Extensions.Util.Tests/Odbc/OdbcTableContributorTests.cs`

**Design:**
```csharp
public sealed class OdbcDriverModel
{
    public required string Id { get; init; }
    public required string DriverName { get; init; }
    public required string FileName { get; init; } // Component file ref
    public string? SetupFileName { get; init; }
}

public sealed class OdbcDataSourceModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string DriverName { get; init; }
    public required OdbcRegistration Registration { get; init; }
    public Dictionary<string, string> Properties { get; init; } = new();
}

public enum OdbcRegistration
{
    PerMachine,
    PerUser
}
```

Tables: `ODBCDriver`, `ODBCDataSource` (standard MSI tables).

**Tests (3 tests):**
1. `GetRows_Driver_ReturnsODBCDriverRow`
2. `GetRows_DataSource_ReturnsODBCDataSourceRow`
3. `GetRows_DriverAndDataSource_ReturnsBothTables`

**Step 1:** Write models + failing tests
**Step 2:** Implement table contributor
**Step 3:** Commit

---

### Task D6: ODBC Extension — Builder + Registration

**Files:**
- Create: `src/FalkForge.Extensions.Util/Odbc/OdbcDriverBuilder.cs`
- Create: `src/FalkForge.Extensions.Util/Odbc/OdbcDataSourceBuilder.cs`
- Create: `tests/FalkForge.Extensions.Util.Tests/Odbc/OdbcBuilderTests.cs`

**Fluent API:**
```csharp
component.OdbcDriver("MyDriver", d => d
    .DriverName("My ODBC Driver")
    .FileName("[#driverFile]")
);

component.OdbcDataSource("MyDSN", ds => ds
    .Name("MyAppDB")
    .DriverName("My ODBC Driver")
    .Registration(OdbcRegistration.PerMachine)
    .Property("Server", "localhost")
    .Property("Database", "mydb")
);
```

**Step 1:** Write builder tests
**Step 2:** Implement builders and register
**Step 3:** Commit

---

## Critical Files Reference

| File | Role |
|------|------|
| `src/FalkForge.Engine/Detection/PackageDetector.cs` | Main detection orchestrator |
| `src/FalkForge.Engine/Detection/MsiDetector.cs` | Registry-based MSI detection |
| `src/FalkForge.Engine/Cache/PackageCache.cs` | Cache with SHA-256 verification |
| `src/FalkForge.Engine/Download/PayloadDownloader.cs` | HTTPS download with resume |
| `src/FalkForge.Engine/Execution/MsiExecutor.cs` | MSI install via msiexec |
| `src/FalkForge.Engine/Execution/PackageExecutor.cs` | Routes to type-specific executors |
| `src/FalkForge.Engine/Planning/Planner.cs` | Creates execution plan from manifest |
| `src/FalkForge.Engine/Phases/ApplyingHandler.cs` | Executes planned actions |
| `src/FalkForge.Engine.Protocol/Manifest/PackageInfo.cs` | Package metadata model |
| `src/FalkForge.Engine.Protocol/Manifest/InstallerManifest.cs` | Bundle manifest model |
| `src/FalkForge.Compiler.Bundle/Builders/BundlePackageBuilder.cs` | Fluent package API |
| `src/FalkForge.Compiler.Bundle/Builders/BundleBuilder.cs` | Fluent bundle API |
| `src/FalkForge.Extensions.Util/UtilExtension.cs` | Util extension registration |
| `src/FalkForge.Platform.Windows/IMsiApi.cs` | Windows MSI P/Invoke |

---

## Dependency Graph

```
Stream A: A1 → A2 → A3 → A4 → A5 (detection)
          A6 → A7 (EULA, independent of A1-A5)

Stream B: B1 → B2 → B3 (signing)
          B4 → B5 → B6 (throttling, independent of B1-B3)

Stream C: C1 → C2 → C3 (chaining)
          C4 → C5 → C6 (slipstreaming, independent of C1-C3)

Stream D: D1 → D2 (scheduled tasks)
          D3 → D4 (perf counters)
          D5 → D6 (ODBC)
          All independent of each other
```
