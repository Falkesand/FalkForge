# Per-MSI Internal Progress Reporting — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add real-time per-MSI progress reporting so the install progress bar moves smoothly during each package's installation instead of jumping in equal steps.

**Architecture:** Register `MsiSetExternalUIW` callback during MSI execution to receive progress ticks. Thread an `IProgress<int>` through the executor chain. Throttle progress updates to max 1 per 100ms. Elevated path deferred to follow-up.

**Tech Stack:** C#/.NET 10, Windows Installer API (`msi.dll`), P/Invoke with `LibraryImport`

**Design doc:** `docs/plans/2026-03-05-per-msi-progress-design.md`

---

### Task 1: Add PackagePercent to InstallProgress

**Files:**
- Modify: `src/FalkForge.Engine.Protocol/InstallProgress.cs`
- Modify: `src/FalkForge.Engine/Phases/ApplyingHandler.cs` (update all `new InstallProgress(...)` call sites)
- Modify: `src/FalkForge.Engine.Protocol/Messages/ProgressMessage.cs` (no change needed — wraps InstallProgress)

**Step 1: Update InstallProgress record struct**

Change:
```csharp
public readonly record struct InstallProgress(int Current, int Total, string CurrentPackage);
```
To:
```csharp
public readonly record struct InstallProgress(int Current, int Total, string CurrentPackage, int PackagePercent = 0);
```

The default value `0` ensures all existing call sites continue to compile without changes.

**Step 2: Verify build**

Run: `dotnet build D:/Git/FalkInstaller/FalkForge.slnx`
Expected: 0 errors

**Step 3: Run all tests**

Run: `dotnet test D:/Git/FalkInstaller/FalkForge.slnx`
Expected: All pass (no regressions)

**Step 4: Commit**

```
feat(protocol): add PackagePercent field to InstallProgress
```

---

### Task 2: Add MsiSetExternalUIW P/Invoke

**Files:**
- Modify: `src/FalkForge.Platform.Windows/NativeMethods.Msi.cs`

**Step 1: Add P/Invoke, delegate, and constants**

Add to `NativeMethods`:

```csharp
// MSI external UI handler delegate — called by Windows Installer during execution
[UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
internal delegate int MsiInstallUIHandler(
    nint context,
    uint messageType,
    string message);

[LibraryImport("msi.dll", StringMarshalling = StringMarshalling.Utf16)]
internal static partial nint MsiSetExternalUIW(
    MsiInstallUIHandler? handler,
    uint messageFilter,
    nint context);

// Message type flags for MsiSetExternalUIW filter
internal const uint INSTALLLOGMODE_PROGRESS = 0x00000400;   // 1 << 10
internal const uint INSTALLLOGMODE_ACTIONSTART = 0x00008000; // 1 << 15
```

**Step 2: Verify build**

Run: `dotnet build D:/Git/FalkInstaller/src/FalkForge.Platform.Windows/FalkForge.Platform.Windows.csproj`
Expected: 0 errors

**Step 3: Commit**

```
feat(platform): add MsiSetExternalUIW P/Invoke for progress callbacks
```

---

### Task 3: Add SetExternalUI to IMsiApi interface

**Files:**
- Modify: `src/FalkForge.Platform.Windows/IMsiApi.cs`
- Modify: `src/FalkForge.Platform.Windows/WindowsMsiApi.cs`
- Test: existing tests that mock `IMsiApi` — must add `SetExternalUI` to mocks

**Step 1: Write failing test**

Find existing test files that mock `IMsiApi`. Add a test that verifies `SetExternalUI` can be called:

```csharp
[Fact]
public void SetExternalUI_WithNullHandler_ReturnsZero()
{
    var api = new FakeIMsiApi(); // existing mock — will fail to compile until interface updated
    var result = api.SetExternalUI(null, 0, IntPtr.Zero);
    Assert.Equal(IntPtr.Zero, result);
}
```

Run tests — expected: compile error (method doesn't exist on interface).

**Step 2: Add interface method**

In `IMsiApi.cs`, add:
```csharp
/// <summary>
/// Registers an external UI handler for progress callbacks during MSI operations.
/// Wraps MsiSetExternalUI.
/// </summary>
/// <param name="handler">Callback function, or null to unregister.</param>
/// <param name="messageFilter">Bitmask of message types to receive.</param>
/// <param name="context">User-defined context pointer.</param>
/// <returns>Pointer to the previously registered handler.</returns>
nint SetExternalUI(Delegate? handler, uint messageFilter, nint context);
```

In `WindowsMsiApi.cs`, add:
```csharp
public nint SetExternalUI(Delegate? handler, uint messageFilter, nint context)
    => NativeMethods.MsiSetExternalUIW(
        handler as NativeMethods.MsiInstallUIHandler,
        messageFilter,
        context);
```

Update all test mocks/fakes of `IMsiApi` to implement the new method (return `IntPtr.Zero`).

**Step 3: Run tests**

Run: `dotnet test D:/Git/FalkInstaller/FalkForge.slnx`
Expected: All pass

**Step 4: Commit**

```
feat(platform): add SetExternalUI to IMsiApi for progress callbacks
```

---

### Task 4: Add IProgress<int> to MsiExecutor

**Files:**
- Modify: `src/FalkForge.Engine/Execution/MsiExecutor.cs`
- Test: `tests/FalkForge.Engine.Tests/Execution/MsiExecutorTests.cs` (find actual test file)

**Step 1: Write failing test**

Add test that verifies MsiExecutor reports progress via `IProgress<int>`:

```csharp
[Fact]
public async Task ExecuteAsync_Install_ReportsProgressViaCallback()
{
    // Arrange: create MsiExecutor with a fake IMsiApi that triggers progress callback
    // Act: call ExecuteAsync with IProgress<int>
    // Assert: IProgress<int> received values
}
```

This will fail to compile because `ExecuteAsync` doesn't accept `IProgress<int>` yet.

**Step 2: Update ExecuteAsync signature**

Change `MsiExecutor.ExecuteAsync`:
```csharp
public async Task<Result<int>> ExecuteAsync(PlanAction action, CancellationToken ct, IProgress<int> packageProgress)
```

Update `ExecuteDirect` to accept and use `IProgress<int>`:
```csharp
private Result<int> ExecuteDirect(PlanAction action, string additionalArgs, IProgress<int> packageProgress)
```

In `ExecuteDirect`, register the MSI external UI handler before `InstallProduct`/`ConfigureProduct`:

```csharp
private Result<int> ExecuteDirect(PlanAction action, string additionalArgs, IProgress<int> packageProgress)
{
    var msiApi = _msiApiAccessor();
    if (msiApi is null)
        return Result<int>.Failure(ErrorKind.ExecutionError, "MSI API not available");

    // Track progress state for MSI progress message parsing
    var progressState = new MsiProgressState();

    NativeMethods.MsiInstallUIHandler? handler = (context, messageType, message) =>
    {
        var percent = progressState.ProcessMessage(messageType, message);
        if (percent >= 0)
            packageProgress.Report(percent);
        return 0; // IDOK — let installer continue
    };

    // Pin the delegate so GC doesn't collect it during native call
    var gcHandle = GCHandle.Alloc(handler);
    try
    {
        msiApi.SetInternalUI(2, IntPtr.Zero); // INSTALLUILEVEL_NONE
        msiApi.SetExternalUI(handler, NativeMethods.INSTALLLOGMODE_PROGRESS, IntPtr.Zero);

        uint exitCode = /* existing switch expression */;

        return (int)exitCode;
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        return Result<int>.Failure(ErrorKind.ExecutionError, $"MSI execution failed: {ex.Message}");
    }
    finally
    {
        // Unregister handler
        msiApi.SetExternalUI(null, 0, IntPtr.Zero);
        gcHandle.Free();
    }
}
```

Also update `ExecuteElevatedAsync` to accept the parameter (but not use it — elevated progress deferred):
```csharp
private static async Task<Result<int>> ExecuteElevatedAsync(
    PlanAction action,
    string additionalArgs,
    IElevationClient elevationClient,
    CancellationToken ct)
    // No IProgress<int> — elevated progress deferred
```

**Step 3: Run tests**

Run: `dotnet test D:/Git/FalkInstaller/FalkForge.slnx`
Expected: All pass

**Step 4: Commit**

```
feat(engine): add IProgress<int> to MsiExecutor for per-MSI progress
```

---

### Task 5: Create MsiProgressState parser

**Files:**
- Create: `src/FalkForge.Engine/Execution/MsiProgressState.cs`
- Create: `tests/FalkForge.Engine.Tests/Execution/MsiProgressStateTests.cs`

The MSI progress message protocol uses field-based messages. The key message types:
- Type 0 (master reset): sets total ticks and direction
- Type 1 (action info): not needed
- Type 2 (progress tick): reports ticks completed
- Type 3 (progress report): reports progress

Each message is a colon-delimited string like `"1: 2 2: 100 3: 0 4: 1"`.

**Step 1: Write failing tests**

```csharp
public class MsiProgressStateTests
{
    [Fact]
    public void ProcessMessage_MasterReset_SetsTotal()
    {
        var state = new MsiProgressState();
        // Field1=0 (master reset), Field2=100 (total), Field3=0 (forward), Field4=1 (in-script)
        var percent = state.ProcessMessage(0x0400, "1: 0 2: 100 3: 0 4: 1");
        Assert.Equal(0, percent);
    }

    [Fact]
    public void ProcessMessage_ProgressTick_ReportsPercent()
    {
        var state = new MsiProgressState();
        state.ProcessMessage(0x0400, "1: 0 2: 100 3: 0 4: 1"); // reset with total=100
        var percent = state.ProcessMessage(0x0400, "1: 2 2: 50"); // tick 50 of 100
        Assert.Equal(50, percent);
    }

    [Fact]
    public void ProcessMessage_NonProgressMessage_ReturnsNegative()
    {
        var state = new MsiProgressState();
        var percent = state.ProcessMessage(0x0001, "some other message");
        Assert.Equal(-1, percent);
    }

    [Fact]
    public void ProcessMessage_ProgressExceedsTotal_ClampedTo100()
    {
        var state = new MsiProgressState();
        state.ProcessMessage(0x0400, "1: 0 2: 100 3: 0 4: 1");
        var percent = state.ProcessMessage(0x0400, "1: 2 2: 150");
        Assert.Equal(100, percent);
    }
}
```

Run tests — expected: compile error (MsiProgressState doesn't exist).

**Step 2: Implement MsiProgressState**

```csharp
namespace FalkForge.Engine.Execution;

/// <summary>
/// Parses MSI progress messages from MsiSetExternalUIW callback
/// and converts them into 0-100 percent values.
/// </summary>
internal sealed class MsiProgressState
{
    private int _total;
    private int _completed;
    private bool _forward = true;

    private const uint ProgressMessageFlag = 0x0400; // INSTALLLOGMODE_PROGRESS

    /// <summary>
    /// Processes an MSI UI message and returns a percent (0-100),
    /// or -1 if the message is not a progress update.
    /// </summary>
    public int ProcessMessage(uint messageType, string message)
    {
        if ((messageType & ProgressMessageFlag) == 0)
            return -1;

        var fields = ParseFields(message);
        if (fields.Count == 0)
            return -1;

        if (!fields.TryGetValue(1, out var field1))
            return -1;

        return field1 switch
        {
            0 => HandleMasterReset(fields),
            2 => HandleProgressTick(fields),
            _ => -1
        };
    }

    private int HandleMasterReset(Dictionary<int, int> fields)
    {
        if (fields.TryGetValue(2, out var total))
            _total = total;

        _completed = 0;
        _forward = !fields.TryGetValue(3, out var direction) || direction == 0;

        return _total > 0 ? 0 : -1;
    }

    private int HandleProgressTick(Dictionary<int, int> fields)
    {
        if (_total <= 0)
            return -1;

        if (fields.TryGetValue(2, out var increment))
        {
            if (_forward)
                _completed += increment;
            else
                _completed -= increment;
        }

        var percent = (int)((long)_completed * 100 / _total);
        return Math.Clamp(percent, 0, 100);
    }

    private static Dictionary<int, int> ParseFields(string message)
    {
        var result = new Dictionary<int, int>();
        var span = message.AsSpan();

        while (span.Length > 0)
        {
            // Skip whitespace
            span = span.TrimStart();
            if (span.Length == 0) break;

            // Find field number before ':'
            var colonIndex = span.IndexOf(':');
            if (colonIndex < 0) break;

            if (!int.TryParse(span[..colonIndex].Trim(), out var fieldNum))
                break;

            span = span[(colonIndex + 1)..].TrimStart();

            // Find value (up to next space or end)
            var spaceIndex = span.IndexOf(' ');
            var valueSpan = spaceIndex >= 0 ? span[..spaceIndex] : span;

            if (int.TryParse(valueSpan.Trim(), out var value))
                result[fieldNum] = value;

            span = spaceIndex >= 0 ? span[(spaceIndex + 1)..] : [];
        }

        return result;
    }
}
```

**Step 3: Run tests**

Run: `dotnet test D:/Git/FalkInstaller/FalkForge.slnx`
Expected: All pass

**Step 4: Commit**

```
feat(engine): add MsiProgressState parser for MSI progress messages
```

---

### Task 6: Add IProgress<int> to other executors and PackageExecutor

**Files:**
- Modify: `src/FalkForge.Engine/Execution/MsuExecutor.cs`
- Modify: `src/FalkForge.Engine/Execution/MspExecutor.cs`
- Modify: `src/FalkForge.Engine/Execution/BundleExecutor.cs`
- Modify: `src/FalkForge.Engine/Execution/PackageExecutor.cs`

**Step 1: Update all executor signatures**

Add `IProgress<int> packageProgress` parameter to each `ExecuteAsync`:

`MsuExecutor`:
```csharp
public async Task<Result<int>> ExecuteAsync(PlanAction action, CancellationToken ct, IProgress<int> packageProgress)
```
(parameter unused — MSU doesn't support progress callbacks)

`MspExecutor`:
```csharp
public async Task<Result<int>> ExecuteAsync(PlanAction action, CancellationToken ct, IProgress<int> packageProgress)
```
(parameter unused — MSP uses msiexec.exe process, no in-process callback)

`BundleExecutor`:
```csharp
public async Task<Result<int>> ExecuteAsync(PlanAction action, CancellationToken ct, IProgress<int> packageProgress)
```
(parameter unused — child bundle handles its own progress)

`PackageExecutor.ExecuteAsync`:
```csharp
public async Task<Result<ExecutionOutcome>> ExecuteAsync(PlanAction action, CancellationToken ct, IProgress<int> packageProgress)
{
    var innerResult = action.Package.Type switch
    {
        PackageType.MsiPackage => await _msiExecutor.ExecuteAsync(action, ct, packageProgress),
        PackageType.MsuPackage => await _msuExecutor.ExecuteAsync(action, ct, packageProgress),
        PackageType.MspPackage => await _mspExecutor.ExecuteAsync(action, ct, packageProgress),
        PackageType.BundlePackage => await _bundleExecutor.ExecuteAsync(action, ct, packageProgress),
        // ...existing error cases...
    };
    // ...rest unchanged...
}
```

**Step 2: Fix all call sites**

Update `ApplyingHandler` calls from `_executor.ExecuteAsync(action, ct)` to `_executor.ExecuteAsync(action, ct, progress)` — use a no-op progress for now:

```csharp
var result = await _executor.ExecuteAsync(action, ct, new Progress<int>(_ => { }));
```

**Step 3: Update existing tests**

All existing tests calling `ExecuteAsync(action, ct)` need the third parameter. Add `new Progress<int>(_ => { })` or a capturing progress instance.

**Step 4: Verify build and tests**

Run: `dotnet build D:/Git/FalkInstaller/FalkForge.slnx`
Run: `dotnet test D:/Git/FalkInstaller/FalkForge.slnx`
Expected: 0 errors, all pass

**Step 5: Commit**

```
feat(engine): thread IProgress<int> through executor chain
```

---

### Task 7: Wire throttled progress in ApplyingHandler

**Files:**
- Modify: `src/FalkForge.Engine/Phases/ApplyingHandler.cs`

**Step 1: Create throttled progress wrapper**

Replace the no-op progress with a real one that sends `ProgressMessage` with `PackagePercent`:

In both `ExecuteWithSegmentsAsync` and `ExecuteFlatAsync`, before the executor call:

```csharp
var lastProgressSent = Stopwatch.GetTimestamp();
var progress = new Progress<int>(percent =>
{
    var now = Stopwatch.GetTimestamp();
    var elapsed = Stopwatch.GetElapsedTime(lastProgressSent, now);
    if (elapsed.TotalMilliseconds < 100 && percent < 100)
        return; // throttle

    lastProgressSent = now;

    if (context.UiPipe is not null && context.UiPipe.IsConnected)
    {
        // Fire-and-forget send — we're on the MSI callback thread
        _ = context.UiPipe.SendAsync(new ProgressMessage
        {
            Progress = new InstallProgress(
                actionIndex + 1,
                totalPackages,
                action.PackageId,
                percent)
        }, ct);
    }
});

var result = await _executor.ExecuteAsync(action, ct, progress);
```

**Step 2: Verify build and tests**

Run: `dotnet build D:/Git/FalkInstaller/FalkForge.slnx`
Run: `dotnet test D:/Git/FalkInstaller/FalkForge.slnx`
Expected: 0 errors, all pass

**Step 3: Commit**

```
feat(engine): wire throttled per-MSI progress in ApplyingHandler
```

---

### Task 8: Update InstallProgressPage percent calculation

**Files:**
- Modify: `demo/MAS/Pages/InstallProgressPage.cs`

**Step 1: Update OnProgress handler**

Change:
```csharp
private void OnProgress(InstallProgress progress)
{
    var percent = progress.Total > 0
        ? (int)(progress.Current * 100.0 / progress.Total)
        : 0;

    ProgressPercent = percent;
    ...
}
```

To:
```csharp
private void OnProgress(InstallProgress progress)
{
    var overall = progress.Total > 0
        ? ((progress.Current - 1) * 100 + progress.PackagePercent) / progress.Total
        : 0;

    ProgressPercent = Math.Clamp(overall, 0, 100);

    var locKey = $"InstallProgress.Package.{progress.CurrentPackage}";
    var localized = Localize(locKey);
    ProgressDetail = localized != locKey ? localized : progress.CurrentPackage;
}
```

**Step 2: Verify build**

Run: `dotnet build D:/Git/FalkInstaller/demo/FalkForge.Demos.slnx`
Expected: 0 errors

**Step 3: Commit**

```
feat(demo): update MAS progress page for per-MSI percent calculation
```

---

### Task 9: Integration verification

**Step 1: Full solution build**

Run: `dotnet build D:/Git/FalkInstaller/FalkForge.slnx`
Expected: 0 errors, 0 warnings

**Step 2: Demo solution build**

Run: `dotnet build D:/Git/FalkInstaller/demo/FalkForge.Demos.slnx`
Expected: 0 errors

**Step 3: All tests**

Run: `dotnet test D:/Git/FalkInstaller/FalkForge.slnx`
Expected: All pass

**Step 4: Roslyn diagnostics**

Run Roslyn `get_diagnostics` with `severity=warning` on modified projects.
Expected: 0 diagnostics

---

## Notes

- **Elevated path**: Per-MSI progress for elevated MSI execution (`MsiInstallCommand`) is deferred. The current `IElevatedCommand.Execute()` is synchronous with no streaming — adding progress requires elevation protocol changes. For now, elevated installs report 0% then 100% per package.
- **GCHandle**: The MSI callback delegate must be pinned with `GCHandle.Alloc` to prevent GC collection during the native `MsiInstallProductW` call. Always free in `finally`.
- **Thread safety**: `MsiSetExternalUIW` callback fires on the same thread as `MsiInstallProductW` (it's synchronous blocking). The `Progress<T>` class marshals to the sync context, but since we're in a console/engine process without a sync context, reports will be inline.
- **Message parsing**: MSI progress messages use the format `"1: <field1> 2: <field2> ..."`. Field1=0 means master reset (Field2=total ticks), Field1=2 means progress tick (Field2=increment).
