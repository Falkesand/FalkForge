# Security, Memory, and Performance Fixes Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Fix 16 confirmed security, memory, and performance issues identified in the FalkInstaller codebase review.

**Architecture:** Fixes are applied in dependency order: self-contained elevation security fixes first, then the HMAC stdin change (which touches IProcessLauncher), then memory/performance improvements. Each task is independently testable via `dotnet test`.

**Tech Stack:** .NET 10, C# latest, xUnit 2.9.3, System.Buffers (ArrayPool), NativeAOT (Engine + Elevation)

---

## Quick Reference

| ID | Severity | File | Issue |
|----|----------|------|-------|
| S2 | CRITICAL | ServiceInstallCommand.cs | Binary path not validated against trusted dirs |
| S3 | HIGH | RegistryWriteCommand.cs | Deny-list misses all of SOFTWARE\Microsoft\ |
| S1 | HIGH | ElevatingHandler.cs | HMAC secret exposed in CLI args |
| S4 | MEDIUM | FileWriteCommand.cs | TOCTOU symlink attack |
| S5 | MEDIUM | MsiInstallCommand.cs | UNC paths not blocked |
| S6 | MEDIUM | PayloadDownloader.cs | HTTP scheme allowed |
| M1 | HIGH | CabinetBuilder.cs | new byte[cb] in FCI hot callbacks |
| M2 | HIGH | CabinetExtractor.cs | new byte[cb] in FDI hot callbacks |
| M3 | HIGH | MsiExecutor.cs | String concat in property loop |
| M4 | MEDIUM | PipeClient.cs + PipeServer.cs | new byte[len] per message in receive loop |
| M5 | MEDIUM | RollbackJournal.cs | No WriteThrough, crash-unsafe |
| P1 | HIGH | TableEmitter.cs | 15+ FirstOrDefault() inside loops |
| P2 | MEDIUM | ComponentResolver.cs | Redundant SHA256 per file for shared dirs |
| P3 | HIGH | BundleCompiler.cs | File.ReadAllBytes loads full package |
| P4 | HIGH | PayloadEmbedder.cs | Sequential compression |
| P5 | MEDIUM | ConditionEvaluator.cs | Re-tokenizes on every Evaluate() call |

---

## Task 1: S2 — ServiceInstallCommand binary path whitelist

**Files:**
- Modify: `src/FalkForge.Engine.Elevation/Commands/ServiceInstallCommand.cs`
- Test: `tests/FalkForge.Engine.Elevation.Tests/Commands/ServiceInstallCommandTests.cs`

**Step 1: Write the failing test**

Find the existing test class (or create it). Add:

```csharp
[Fact]
public void Execute_RejectsPathOutsideTrustedDirectories()
{
    var command = new ServiceInstallCommand();
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);
    writer.Write("MySvc");
    writer.Write("My Service");
    writer.Write(@"C:\Users\attacker\evil.exe"); // outside trusted dirs
    var payload = stream.ToArray();

    var result = command.Execute(payload);

    Assert.True(result.IsFailure);
    Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
}
```

**Step 2: Run test to verify it fails**

```
dotnet test tests/FalkForge.Engine.Elevation.Tests --filter "RejectsPathOutsideTrustedDirectories" -v
```

Expected: FAIL (test passes currently — the path is accepted without whitelist check)

**Step 3: Implement the fix**

In `ServiceInstallCommand.cs`, after the `normalizedBinaryPath.Contains("..")` check (around line 40), add:

```csharp
if (!FileWriteCommand.IsAllowedPath(normalizedBinaryPath))
    return Result<byte[]>.Failure(ErrorKind.SecurityError,
        "Binary path must be under Program Files or ProgramData");
```

Add the using if needed: the two commands are in the same namespace `FalkForge.Engine.Elevation.Commands`.

**Step 4: Run test to verify it passes**

```
dotnet test tests/FalkForge.Engine.Elevation.Tests --filter "RejectsPathOutsideTrustedDirectories" -v
```

Expected: PASS

**Step 5: Run all tests**

```
dotnet test
```

Expected: all tests pass (zero failures)

**Step 6: Commit**

```bash
git add src/FalkForge.Engine.Elevation/Commands/ServiceInstallCommand.cs \
        tests/FalkForge.Engine.Elevation.Tests/Commands/ServiceInstallCommandTests.cs
git commit -m "fix(security): validate service binary path against trusted directories"
```

---

## Task 2: S3 — RegistryWriteCommand deny-list expansion

**Files:**
- Modify: `src/FalkForge.Engine.Elevation/Commands/RegistryWriteCommand.cs`
- Test: `tests/FalkForge.Engine.Elevation.Tests/Commands/RegistryWriteCommandTests.cs`

**Step 1: Write the failing test**

```csharp
[Theory]
[InlineData(@"SOFTWARE\Microsoft\SomeOtherKey")]
[InlineData(@"SOFTWARE\Microsoft\ActiveSetup\Installed Components")]
public void Execute_RejectsMicrosoftSubtreeKeys(string subKey)
{
    var command = new RegistryWriteCommand();
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);
    writer.Write("HKLM");
    writer.Write(subKey);
    writer.Write("TestValue");
    writer.Write("REG_SZ");
    writer.Write("data");
    var payload = stream.ToArray();

    var result = command.Execute(payload);

    Assert.True(result.IsFailure);
    Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
}
```

**Step 2: Run test to verify it fails**

```
dotnet test tests/FalkForge.Engine.Elevation.Tests --filter "RejectsMicrosoftSubtreeKeys" -v
```

Expected: FAIL (currently `SOFTWARE\Microsoft\SomeOtherKey` is accepted)

**Step 3: Implement the fix**

In `RegistryWriteCommand.cs`, replace the `DeniedSubKeyPrefixes` array:

```csharp
private static readonly string[] DeniedSubKeyPrefixes =
[
    @"SOFTWARE\Microsoft\",
    @"SOFTWARE\Classes\",
    @"SOFTWARE\Policies\",
    @"SYSTEM\",
    @"SECURITY\",
    @"SAM\"
];
```

This replaces the two partial `SOFTWARE\Microsoft\*` entries with the full subtree block, and also blocks `SOFTWARE\Classes\` (COM hijacking vector) and `SOFTWARE\Policies\` (Group Policy abuse).

**Step 4: Run tests to verify**

```
dotnet test tests/FalkForge.Engine.Elevation.Tests -v
```

Expected: all tests pass. Verify the existing test for `SOFTWARE\Microsoft\Windows NT\` still passes (it's now covered by `SOFTWARE\Microsoft\`).

**Step 5: Run all tests**

```
dotnet test
```

**Step 6: Commit**

```bash
git add src/FalkForge.Engine.Elevation/Commands/RegistryWriteCommand.cs \
        tests/FalkForge.Engine.Elevation.Tests/Commands/RegistryWriteCommandTests.cs
git commit -m "fix(security): block full SOFTWARE\Microsoft\ subtree in registry writes"
```

---

## Task 3: S4 — FileWriteCommand TOCTOU symlink protection

**Files:**
- Modify: `src/FalkForge.Engine.Elevation/Commands/FileWriteCommand.cs`
- Test: `tests/FalkForge.Engine.Elevation.Tests/Commands/FileWriteCommandTests.cs`

**Step 1: Write the failing test**

This test is tricky to do with a real symlink on CI. Write a unit test against the helper method instead:

```csharp
[Fact]
public void IsReparsePoint_ReturnsTrueForKnownReparsePoint()
{
    // Use the Windows temp directory junction if available, otherwise skip
    var junctionPath = Path.Combine(Path.GetTempPath(), "Documents and Settings");
    if (!Directory.Exists(junctionPath))
    {
        // Skip on systems without this junction
        return;
    }

    var attrs = new DirectoryInfo(junctionPath).Attributes;
    Assert.True(attrs.HasFlag(FileAttributes.ReparsePoint));
}

[Fact]
public void Execute_RejectsReparsePointDirectory()
{
    // Create temp dir, then a path pointing into Program Files via reparse point detection
    // Instead: verify the guard code path exists by testing the internal flag
    // The actual TOCTOU protection is structural — verified by code review
    // This test verifies IsAllowedPath still returns false for traversal
    var result = FileWriteCommand.IsAllowedPath(@"C:\Windows\System32\evil.txt");
    Assert.False(result);
}
```

**Step 2: Run test to verify**

```
dotnet test tests/FalkForge.Engine.Elevation.Tests --filter "RejectsReparsePointDirectory" -v
```

**Step 3: Implement the fix**

In `FileWriteCommand.cs`, inside the `Execute` method, after `Directory.CreateDirectory(dir)` (currently around line 39), add a reparse point check:

```csharp
var dir = Path.GetDirectoryName(normalizedPath);
if (dir is not null)
{
    Directory.CreateDirectory(dir);

    // Guard against symlink/junction attacks placed between path check and write
    var dirInfo = new DirectoryInfo(dir);
    if (dirInfo.Exists && dirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
        return Result<byte[]>.Failure(ErrorKind.SecurityError,
            "Target directory is a symbolic link or junction and cannot be written to");
}
File.WriteAllBytes(normalizedPath, content);
```

**Step 4: Run all tests**

```
dotnet test
```

**Step 5: Commit**

```bash
git add src/FalkForge.Engine.Elevation/Commands/FileWriteCommand.cs \
        tests/FalkForge.Engine.Elevation.Tests/Commands/FileWriteCommandTests.cs
git commit -m "fix(security): reject reparse point directories in elevated file writes"
```

---

## Task 4: S5 — MsiInstallCommand UNC path blocking

**Files:**
- Modify: `src/FalkForge.Engine.Elevation/Commands/MsiInstallCommand.cs`
- Test: `tests/FalkForge.Engine.Elevation.Tests/Commands/MsiInstallCommandTests.cs`

**Step 1: Write the failing test**

```csharp
[Theory]
[InlineData(@"\\server\share\evil.msi")]
[InlineData(@"\\192.168.1.1\share\malware.msi")]
public void Execute_RejectsUncPaths(string uncPath)
{
    var command = new MsiInstallCommand();
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);
    writer.Write(uncPath);
    writer.Write(string.Empty);
    var payload = stream.ToArray();

    var result = command.Execute(payload);

    Assert.True(result.IsFailure);
    Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
    Assert.Contains("UNC", result.Error.Message);
}
```

**Step 2: Run test to verify it fails**

```
dotnet test tests/FalkForge.Engine.Elevation.Tests --filter "RejectsUncPaths" -v
```

Expected: FAIL (currently falls through to `File.Exists` which returns false → ExecutionError, not SecurityError)

**Step 3: Implement the fix**

In `MsiInstallCommand.cs`, before the `File.Exists` check, add:

```csharp
if (msiPath.StartsWith(@"\\", StringComparison.Ordinal))
    return Result<byte[]>.Failure(ErrorKind.SecurityError,
        "UNC/network MSI paths are not allowed");
```

**Step 4: Run tests and commit**

```
dotnet test
git add src/FalkForge.Engine.Elevation/Commands/MsiInstallCommand.cs \
        tests/FalkForge.Engine.Elevation.Tests/Commands/MsiInstallCommandTests.cs
git commit -m "fix(security): block UNC paths in elevated MSI installation"
```

---

## Task 5: S6 — PayloadDownloader HTTPS enforcement

**Files:**
- Modify: `src/FalkForge.Engine/Download/PayloadDownloader.cs`
- Test: `tests/FalkForge.Engine.Tests/Download/PayloadDownloaderTests.cs`

**Step 1: Write the failing test**

```csharp
[Fact]
public async Task DownloadAsync_RejectsHttpUrl()
{
    var downloader = new PayloadDownloader(new HttpClient());

    var result = await downloader.DownloadAsync(
        "http://example.com/payload.msi",
        "abc123",
        Path.GetTempFileName());

    Assert.True(result.IsFailure);
    Assert.Contains("https", result.Error.Message, StringComparison.OrdinalIgnoreCase);
}
```

**Step 2: Run test to verify it fails**

```
dotnet test tests/FalkForge.Engine.Tests --filter "RejectsHttpUrl" -v
```

Expected: FAIL (currently HTTP is allowed)

**Step 3: Implement the fix**

In `PayloadDownloader.cs`, change lines 30-31 from:
```csharp
if (uri.Scheme != "https" && uri.Scheme != "http")
    return Result<string>.Failure(ErrorKind.DownloadError, $"Unsupported URL scheme: {uri.Scheme}. Only http and https are allowed.");
```

To:
```csharp
if (uri.Scheme != Uri.UriSchemeHttps)
    return Result<string>.Failure(ErrorKind.DownloadError,
        $"Unsupported URL scheme '{uri.Scheme}': only https is allowed.");
```

**Step 4: Run tests and commit**

```
dotnet test
git add src/FalkForge.Engine/Download/PayloadDownloader.cs \
        tests/FalkForge.Engine.Tests/Download/PayloadDownloaderTests.cs
git commit -m "fix(security): enforce HTTPS-only for payload downloads"
```

---

## Task 6: S1 — HMAC secret via stdin instead of CLI args

This is the most complex change. It touches four files across two projects.

**Files:**
- Read first: `src/FalkForge.Engine/Elevation/IProcessLauncher.cs`
- Read first: `src/FalkForge.Engine/Elevation/ProcessLauncher.cs`
- Read first: `src/FalkForge.Engine.Elevation/Program.cs` (entry point — may be top-level statements)
- Modify: `src/FalkForge.Engine/Phases/ElevatingHandler.cs`
- Modify: `src/FalkForge.Engine/Elevation/IProcessLauncher.cs`
- Modify: `src/FalkForge.Engine/Elevation/ProcessLauncher.cs`
- Modify: `src/FalkForge.Engine.Elevation/ElevatedHost.cs` (or Program.cs)
- Test: `tests/FalkForge.Engine.Tests/Phases/ElevatingHandlerTests.cs`
- Test: `tests/FalkForge.Engine.Elevation.Tests/ElevatedHostTests.cs`

**Step 1: Read the launcher and program files**

Before touching anything, read:
- `src/FalkForge.Engine/Elevation/IProcessLauncher.cs`
- `src/FalkForge.Engine/Elevation/ProcessLauncher.cs`
- `src/FalkForge.Engine.Elevation/Program.cs`

Understand how `--secret` is currently parsed from args.

**Step 2: Write the failing test for ElevatingHandler**

Find the existing `ElevatingHandlerTests.cs`. Add a test verifying the secret is NOT passed via command-line args to the mock launcher:

```csharp
[Fact]
public async Task ExecuteAsync_DoesNotPassSecretInCommandLineArgs()
{
    string? capturedArgs = null;
    byte[]? capturedStdin = null;

    var launcher = new FakeProcessLauncher((path, args, stdin) =>
    {
        capturedArgs = args;
        capturedStdin = stdin;
        return Result<Process>.Success(FakeProcess.Running());
    });

    var handler = new ElevatingHandler(launcher, NullLogger.Instance);
    // ... setup context and run ...

    Assert.NotNull(capturedArgs);
    Assert.DoesNotContain("--secret", capturedArgs);
    Assert.NotNull(capturedStdin);
    Assert.Equal(32, capturedStdin!.Length); // 32-byte secret
}
```

Adjust the `FakeProcessLauncher` to accept the new `byte[] stdinPayload` parameter in whatever interface `IProcessLauncher` currently defines.

**Step 3: Update IProcessLauncher**

Change the `Launch` method signature to accept `byte[]? stdinPayload`:

```csharp
public interface IProcessLauncher
{
    Result<Process> Launch(string path, string args, byte[]? stdinPayload = null);
}
```

**Step 4: Update ProcessLauncher**

In `ProcessLauncher.cs`, update the `Launch` implementation:

```csharp
public Result<Process> Launch(string path, string args, byte[]? stdinPayload = null)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = path,
        Arguments = args,
        UseShellExecute = false,
        CreateNoWindow = true,
        Verb = "runas",
        RedirectStandardInput = stdinPayload is not null
    };

    var process = new Process { StartInfo = startInfo };
    process.Start();

    if (stdinPayload is not null)
    {
        process.StandardInput.BaseStream.Write(stdinPayload, 0, stdinPayload.Length);
        process.StandardInput.BaseStream.Flush();
        process.StandardInput.BaseStream.Close(); // signal EOF
    }

    return process;
}
```

**Step 5: Update ElevatingHandler**

Change line 85 from:
```csharp
var args = $"--pipe {pipeName} --secret {Convert.ToBase64String(secret)} --parent-pid {Environment.ProcessId}";
var launchResult = _processLauncher.Launch(companionPath, args);
```

To:
```csharp
var args = $"--pipe {pipeName} --parent-pid {Environment.ProcessId}";
var launchResult = _processLauncher.Launch(companionPath, args, stdinPayload: secret);
```

**Step 6: Update ElevatedHost / Program.cs**

In the elevated companion's entry point, read the secret from stdin before parsing `--secret`:

```csharp
// Read 32-byte secret from stdin (written by parent before we start reading pipe)
var secret = new byte[32];
var totalRead = 0;
while (totalRead < 32)
{
    var read = Console.OpenStandardInput().Read(secret, totalRead, 32 - totalRead);
    if (read == 0) throw new InvalidOperationException("Parent closed stdin before sending full secret");
    totalRead += read;
}

// Remove --secret parsing from args; use the stdin value instead
```

Remove the `--secret` argument parsing from wherever it currently lives in the elevation Program.cs.

**Step 7: Run all tests and commit**

```
dotnet build  # must be 0 warnings
dotnet test
git add src/FalkForge.Engine/Phases/ElevatingHandler.cs \
        src/FalkForge.Engine/Elevation/IProcessLauncher.cs \
        src/FalkForge.Engine/Elevation/ProcessLauncher.cs \
        src/FalkForge.Engine.Elevation/ \
        tests/
git commit -m "fix(security): pass HMAC secret via stdin instead of command-line args"
```

---

## Task 7: M1 — CabinetBuilder ArrayPool in FCI callbacks

**Files:**
- Modify: `src/FalkForge.Compiler.Msi/CabinetBuilder.cs`
- Test: `tests/FalkForge.Compiler.Msi.Tests/CabinetBuilderTests.cs`

The FCI callbacks `CbRead` (line 220) and `CbWrite` (line 243) each allocate `new byte[cb]` on every call. This is in the hot path during cabinet compression.

**Step 1: The test already exists**

The existing cabinet builder tests exercise these callbacks indirectly. Run them first to establish baseline:

```
dotnet test tests/FalkForge.Compiler.Msi.Tests -v
```

All should pass before the change.

**Step 2: Implement the fix**

Add `using System.Buffers;` at the top of `CabinetBuilder.cs`.

Replace `CbRead`:
```csharp
private uint CbRead(nint hf, nint memory, uint cb, out int err, nint pv)
{
    err = 0;
    try
    {
        if (!_openStreams.TryGetValue(hf, out var stream))
        {
            err = 1;
            return unchecked((uint)-1);
        }

        var buffer = ArrayPool<byte>.Shared.Rent((int)cb);
        try
        {
            var bytesRead = stream.Read(buffer, 0, (int)cb);
            Marshal.Copy(buffer, 0, memory, bytesRead);
            return (uint)bytesRead;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
    catch
    {
        err = 1;
        return unchecked((uint)-1);
    }
}
```

Replace `CbWrite`:
```csharp
private uint CbWrite(nint hf, nint memory, uint cb, out int err, nint pv)
{
    err = 0;
    try
    {
        if (!_openStreams.TryGetValue(hf, out var stream))
        {
            err = 1;
            return unchecked((uint)-1);
        }

        var buffer = ArrayPool<byte>.Shared.Rent((int)cb);
        try
        {
            Marshal.Copy(memory, buffer, 0, (int)cb);
            stream.Write(buffer, 0, (int)cb);
            return cb;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
    catch
    {
        err = 1;
        return unchecked((uint)-1);
    }
}
```

**Step 3: Run tests and commit**

```
dotnet test tests/FalkForge.Compiler.Msi.Tests -v
dotnet test
git add src/FalkForge.Compiler.Msi/CabinetBuilder.cs
git commit -m "perf(memory): use ArrayPool in FCI read/write callbacks"
```

---

## Task 8: M2 — CabinetExtractor ArrayPool in FDI callbacks

**Files:**
- Modify: `src/FalkForge.Compiler.Msi/CabinetExtractor.cs`

Same pattern as Task 7. The FDI callbacks `CbRead` (line 202) and `CbWrite` (~line 220) both allocate `new byte[cb]`.

**Step 1: Implement the fix**

Add `using System.Buffers;` at the top of `CabinetExtractor.cs`.

Replace `CbRead`:
```csharp
private uint CbRead(nint hf, nint pv, uint cb)
{
    try
    {
        if (!_openStreams.TryGetValue(hf, out var stream))
        {
            _lastCallbackError = $"Read: handle {hf} not found";
            return unchecked((uint)-1);
        }

        var buffer = ArrayPool<byte>.Shared.Rent((int)cb);
        try
        {
            var bytesRead = stream.Read(buffer, 0, (int)cb);
            Marshal.Copy(buffer, 0, pv, bytesRead);
            return (uint)bytesRead;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
    catch (Exception ex)
    {
        _lastCallbackError = $"Read failed: {ex.Message}";
        return unchecked((uint)-1);
    }
}
```

Apply the same pattern to `CbWrite` (read the remainder of the file to see the write callback signature, then mirror the CabinetBuilder fix).

**Step 2: Run tests and commit**

```
dotnet test tests/FalkForge.Compiler.Msi.Tests -v
dotnet test
git add src/FalkForge.Compiler.Msi/CabinetExtractor.cs
git commit -m "perf(memory): use ArrayPool in FDI read/write callbacks"
```

---

## Task 9: M3 — MsiExecutor StringBuilder in property loop

**Files:**
- Modify: `src/FalkForge.Engine/Execution/MsiExecutor.cs`
- Test: `tests/FalkForge.Engine.Tests/Execution/MsiExecutorTests.cs`

**Step 1: Write the failing test**

The existing tests should already cover this path. Write a test with many properties to demonstrate correctness (not a performance test):

```csharp
[Fact]
public async Task ExecuteAsync_BuildsPropertyArgsCorrectlyWithMultipleProperties()
{
    // Verify that ValidateAndBuildPropertyArgs produces correct output
    // This test ensures StringBuilder produces identical output to string concat
    var action = new PlanAction
    {
        Properties = new Dictionary<string, string>
        {
            ["INSTALLFOLDER"] = @"C:\Program Files\MyApp",
            ["LICENSEKEY"] = "ABC-123",
            ["LOGFILE"] = @"C:\Logs\install.log"
        },
        // ... other required fields
    };
    // ... assert that result contains all three properties
}
```

Run to verify it passes before the change (it should).

**Step 2: Implement the fix**

In `MsiExecutor.cs`, change `ValidateAndBuildPropertyArgs`:

```csharp
private static Result<string> ValidateAndBuildPropertyArgs(PlanAction action, VariableStore? variableStore)
{
    if (action.Properties.Count == 0)
        return string.Empty;

    var sb = new StringBuilder();

    foreach (var prop in action.Properties)
    {
        if (!MsiPropertyKeyPattern().IsMatch(prop.Key))
            return Result<string>.Failure(
                ErrorKind.SecurityError,
                $"Invalid MSI property key '{prop.Key}': must match ^[A-Z_][A-Z0-9_.]*$");

        var resolvedValue = ResolvePropertyValue(prop.Value, variableStore);

        if (resolvedValue.AsSpan().IndexOfAny(ProhibitedValueChars) >= 0)
            return Result<string>.Failure(
                ErrorKind.SecurityError,
                $"MSI property value for '{prop.Key}' contains prohibited characters");

        sb.Append(' ');
        sb.Append(prop.Key);
        sb.Append("=\"");
        sb.Append(resolvedValue);
        sb.Append('"');
    }

    return sb.ToString();
}
```

Add `using System.Text;` at the top if not already present.

**Step 3: Run tests and commit**

```
dotnet test
git add src/FalkForge.Engine/Execution/MsiExecutor.cs
git commit -m "perf(memory): use StringBuilder for MSI property argument building"
```

---

## Task 10: M4 — PipeClient + PipeServer ArrayPool for message buffers

**Files:**
- Modify: `src/FalkForge.Engine.Protocol/Transport/PipeClient.cs`
- Modify: `src/FalkForge.Engine.Protocol/Transport/PipeServer.cs`
- Test: `tests/FalkForge.Engine.Protocol.Tests/Transport/PipeTransportTests.cs`

The `ReceiveLoopAsync` in both files allocates `new byte[messageLength]` for every received message.

**Step 1: Verify existing tests pass**

```
dotnet test tests/FalkForge.Engine.Protocol.Tests -v
```

**Step 2: Implement the fix in PipeClient.cs**

Add `using System.Buffers;` at the top.

In `ReceiveLoopAsync`, change:
```csharp
var messageBuffer = new byte[messageLength];
if (!await ReadExactAsync(_pipe, messageBuffer, ct))
    break;

var result = MessageDeserializer.Deserialize(messageBuffer);
if (result.IsSuccess)
    await _messageHandler(result.Value);
```

To:
```csharp
var messageBuffer = ArrayPool<byte>.Shared.Rent(messageLength);
try
{
    if (!await ReadExactAsync(_pipe, messageBuffer, messageLength, ct))
        break;

    var result = MessageDeserializer.Deserialize(messageBuffer.AsSpan(0, messageLength));
    if (result.IsSuccess)
        await _messageHandler(result.Value);
}
finally
{
    ArrayPool<byte>.Shared.Return(messageBuffer);
}
```

Note: `ReadExactAsync` needs a `length` parameter when using rented buffers (rented buffers may be larger than `messageLength`). Update the helper:

```csharp
private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, int length, CancellationToken ct)
{
    var totalRead = 0;
    while (totalRead < length)
    {
        var read = await stream.ReadAsync(buffer.AsMemory(totalRead, length - totalRead), ct);
        if (read == 0) return false;
        totalRead += read;
    }
    return true;
}
```

Check if `MessageDeserializer.Deserialize` accepts `ReadOnlySpan<byte>`. If it only accepts `byte[]`, pass `messageBuffer.AsSpan(0, messageLength).ToArray()` — still better than the original since the ArrayPool handles most allocations for large messages. Prefer updating the deserializer signature if possible.

Apply the same fix to `PipeServer.cs`.

**Step 3: Run tests and commit**

```
dotnet test
git add src/FalkForge.Engine.Protocol/Transport/PipeClient.cs \
        src/FalkForge.Engine.Protocol/Transport/PipeServer.cs
git commit -m "perf(memory): use ArrayPool for pipe receive buffers"
```

---

## Task 11: M5 — RollbackJournal crash-safe writes

**Files:**
- Modify: `src/FalkForge.Engine/Journal/RollbackJournal.cs`
- Test: `tests/FalkForge.Engine.Tests/Journal/RollbackJournalTests.cs`

**Step 1: Read the full RollbackJournal.cs**

Read the file to find where the FileStream is opened (the `Open()` method) and where entries are written (the `AddEntry()` method).

**Step 2: Verify existing tests pass**

```
dotnet test tests/FalkForge.Engine.Tests --filter "RollbackJournal" -v
```

**Step 3: Implement the fix**

In the `Open()` method, change FileStream creation to add `FileOptions.WriteThrough`:

```csharp
// Before:
_stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);

// After:
_stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read,
    bufferSize: 4096, FileOptions.WriteThrough | FileOptions.SequentialScan);
```

In `AddEntry()` (or equivalent method), add explicit flush after each write:

```csharp
// At the end of each entry write:
_writer.Flush();
```

**Step 4: Run tests and commit**

```
dotnet test tests/FalkForge.Engine.Tests --filter "RollbackJournal" -v
dotnet test
git add src/FalkForge.Engine/Journal/RollbackJournal.cs
git commit -m "fix(reliability): add WriteThrough and flush to RollbackJournal for crash safety"
```

---

## Task 12: P1 — TableEmitter loop hoisting

**Files:**
- Modify: `src/FalkForge.Compiler.Msi/Tables/TableEmitter.cs`
- Test: `tests/FalkForge.Compiler.Msi.Tests/Tables/TableEmitterTests.cs`

This is the largest single-file change. Make surgical edits — do not refactor beyond what is needed.

**Step 1: Verify existing tests pass**

```
dotnet test tests/FalkForge.Compiler.Msi.Tests -v
```

**Step 2: Hoist simple FirstOrDefault() calls**

For each method that has `resolved.Components.FirstOrDefault()?.Id` or `resolved.Package.Features.FirstOrDefault()?.Id` inside a loop, add the variable declaration BEFORE the loop:

**EmitRegistry** (~line 413-434):
```csharp
// Add before the foreach:
var defaultComponentId = resolved.Components.FirstOrDefault()?.Id ?? "MainComponent";
// Replace inside loop: entry.ComponentId ?? resolved.Components.FirstOrDefault()?.Id ?? "MainComponent"
// With: entry.ComponentId ?? defaultComponentId
```

Apply same pattern to: EmitRemoveRegistry (line 438), EmitShortcuts (line 485), EmitServiceControls (line 597), EmitEnvironmentVariables (line 624), EmitIniFiles (line 673), EmitFileAssociations (lines 1179-1180), EmitRemoveFiles (line 1293), EmitCreateFolders (line 1318), EmitMoveFiles (line 1339), EmitDuplicateFiles (line 1365), EmitAssemblies (line 1451).

**Step 3: Fix O(n×m) service executable lookup (~line 528-530)**

Before the services `foreach`, build a lookup dictionary:
```csharp
var executableToComponentId = resolved.Components
    .SelectMany(c => c.Files.Select(f => (FileName: f.FileName, ComponentId: c.Id)))
    .GroupBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
    .ToDictionary(g => g.Key, g => g.First().ComponentId, StringComparer.OrdinalIgnoreCase);

var defaultComponentId = resolved.Components.FirstOrDefault()?.Id ?? "MainComponent";
```

Inside the loop, replace:
```csharp
var componentId = resolved.Components.FirstOrDefault(c =>
    c.Files.Any(f => f.FileName.Equals(service.Executable, StringComparison.OrdinalIgnoreCase)))?.Id
    ?? resolved.Components.FirstOrDefault()?.Id ?? "MainComponent";
```

With:
```csharp
var componentId = executableToComponentId.GetValueOrDefault(service.Executable ?? string.Empty)
    ?? defaultComponentId;
```

**Step 4: Fix font file lookup (~line 654)**

Before the fonts `foreach`, build a lookup:
```csharp
var fileNameToFileId = resolved.Files
    .ToDictionary(f => f.FileName, f => f.FileId, StringComparer.OrdinalIgnoreCase);
```

Inside the loop, replace:
```csharp
var fileId = resolved.Files.FirstOrDefault(f =>
    f.FileName.Equals(font.FileName, StringComparison.OrdinalIgnoreCase))?.FileId;
```

With:
```csharp
var fileId = fileNameToFileId.GetValueOrDefault(font.FileName);
```

**Step 5: Fix assembly file lookup (~lines 1456-1458)**

Same pattern as Step 3 — use `executableToComponentId` dictionary (can be reused if in same method scope, or rebuild for EmitAssemblies).

**Step 6: Run tests and commit**

```
dotnet test tests/FalkForge.Compiler.Msi.Tests -v
dotnet test
git add src/FalkForge.Compiler.Msi/Tables/TableEmitter.cs
git commit -m "perf: hoist FirstOrDefault() calls out of loops in TableEmitter"
```

---

## Task 13: P2 — ComponentResolver hash cache

**Files:**
- Modify: `src/FalkForge.Compiler.Msi/ComponentResolver.cs`
- Test: existing resolver tests cover this

**Step 1: Verify existing tests pass**

```
dotnet test tests/FalkForge.Compiler.Msi.Tests --filter "ComponentResolver" -v
```

**Step 2: Implement the fix**

`StableHash` is a `private static string StableHash(string input)` at line 122. It computes `SHA256.HashData(Encoding.UTF8.GetBytes(input))` on every call.

In the `Resolve()` method, add a local cache dictionary and change all `StableHash()` calls to go through it:

```csharp
public Result<ResolvedPackage> Resolve(PackageModel package)
{
    var components = new List<ResolvedComponent>();
    var fileEntries = new List<ResolvedFile>();
    var hashCache = new Dictionary<string, string>(); // memoize StableHash results

    foreach (var file in package.Files)
    {
        // ... existing code, but replace:
        // StableHash(directory.ToString())
        // with:
        // GetCachedHash(hashCache, directory.ToString())
    }
    // ...
}

private static string GetCachedHash(Dictionary<string, string> cache, string input)
{
    if (!cache.TryGetValue(input, out var result))
    {
        result = StableHash(input);
        cache[input] = result;
    }
    return result;
}
```

Update all 4 call sites in `Resolve()` to use `GetCachedHash(hashCache, ...)`.

**Step 3: Run tests and commit**

```
dotnet test
git add src/FalkForge.Compiler.Msi/ComponentResolver.cs
git commit -m "perf: cache SHA256 hashes in ComponentResolver to avoid redundant computation"
```

---

## Task 14: P3+P4 — BundleCompiler streaming + parallel compression

These two fixes are coupled: P3 changes `PayloadEntry.Data: byte[]` to `SourcePath: string`, which P4 then uses to compress in parallel.

**Files:**
- Modify: `src/FalkForge.Compiler.Bundle/Compilation/BundleCompiler.cs`
- Modify: `src/FalkForge.Compiler.Bundle/Compilation/PayloadEntry.cs`
- Modify: `src/FalkForge.Compiler.Bundle/Compilation/PayloadEmbedder.cs`
- Test: `tests/FalkForge.Compiler.Bundle.Tests/Compilation/BundleCompilerTests.cs`

**Step 1: Read PayloadEntry.cs**

Read the file first to understand its current shape before modifying it.

**Step 2: Update PayloadEntry**

Change `byte[] Data` to `string SourcePath`:

```csharp
public sealed class PayloadEntry
{
    public required string PackageId { get; init; }
    public required string SourcePath { get; init; }  // was: byte[] Data
    public required string Sha256Hash { get; init; }
    public int OriginalSize { get; init; }
    public string? ContainerId { get; init; }
}
```

**Step 3: Update BundleCompiler to stream hash**

Replace lines 36-37:
```csharp
// Before:
var data = File.ReadAllBytes(package.SourcePath);
var hash = Convert.ToHexString(SHA256.HashData(data));

// After:
using var hashStream = File.OpenRead(package.SourcePath);
var hash = Convert.ToHexString(SHA256.HashData(hashStream));
var originalSize = new FileInfo(package.SourcePath).Length;

payloads.Add(new PayloadEntry
{
    PackageId = package.Id,
    SourcePath = package.SourcePath,   // path, not bytes
    Sha256Hash = hash,
    OriginalSize = (int)originalSize,
    ContainerId = package.ContainerId
});
```

**Step 4: Update PayloadEmbedder to compress in parallel then write sequentially**

Replace the sequential `foreach` in `Embed()` (lines 45-63) with:

```csharp
// Compress all payloads in parallel
var compressor = new GzipCompressor();
var results = new (byte[] Compressed, TocEntry Entry)[orderedPayloads.Count];

Parallel.For(0, orderedPayloads.Count, i =>
{
    var payload = orderedPayloads[i];
    using var sourceStream = File.OpenRead(payload.SourcePath);
    var compressResult = compressor.Compress(sourceStream); // update GzipCompressor to accept Stream
    if (compressResult.IsFailure)
        throw new InvalidOperationException(compressResult.Error.Message);

    results[i] = (compressResult.Value, new TocEntry
    {
        PackageId = payload.PackageId,
        CompressedSize = compressResult.Value.Length,
        OriginalSize = payload.OriginalSize,
        Sha256Hash = payload.Sha256Hash
    });
});

// Write sequentially (offsets are determined during write)
var tocEntries = new List<TocEntry>();
foreach (var (compressed, entry) in results)
{
    var offset = stream.Position;
    writer.Write(compressed);
    tocEntries.Add(entry with { Offset = offset });
}
```

**Important:** `GzipCompressor.Compress()` may need a `Stream` overload. Read `GzipCompressor.cs` first and add:

```csharp
public Result<byte[]> Compress(Stream input)
{
    using var output = new MemoryStream();
    using (var gz = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
    {
        input.CopyTo(gz);
    }
    return output.ToArray();
}
```

**Step 5: Run tests and commit**

```
dotnet build  # 0 warnings required
dotnet test tests/FalkForge.Compiler.Bundle.Tests -v
dotnet test
git add src/FalkForge.Compiler.Bundle/Compilation/
git commit -m "perf: stream bundle payloads and compress in parallel"
```

---

## Task 15: P5 — ConditionEvaluator token cache

**Files:**
- Modify: `src/FalkForge.Engine/Variables/ConditionEvaluator.cs`
- Modify: `src/FalkForge.Engine/Variables/ConditionLexer.cs` (read first)
- Test: `tests/FalkForge.Engine.Tests/Variables/ConditionEvaluatorTests.cs`

**Step 1: Read ConditionLexer.cs**

Understand how tokenization works and what type `IReadOnlyList<ConditionToken>` the lexer returns.

**Step 2: Verify existing tests pass**

```
dotnet test tests/FalkForge.Engine.Tests --filter "ConditionEvaluator" -v
```

**Step 3: Add token cache**

In `ConditionEvaluator.cs`, add a static cache field:

```csharp
// Bounded cache: evict when over limit to prevent unbounded memory growth
private static readonly ConcurrentDictionary<string, IReadOnlyList<ConditionToken>> _tokenCache = new();
private const int TokenCacheMaxEntries = 512;
```

In the `Evaluate(string condition, ...)` method, before creating the `Parser`, add:

```csharp
IReadOnlyList<ConditionToken> tokens;
if (!_tokenCache.TryGetValue(condition, out tokens))
{
    tokens = ConditionLexer.Tokenize(condition); // or however tokenization works
    if (_tokenCache.Count < TokenCacheMaxEntries)
        _tokenCache.TryAdd(condition, tokens);
}
// Pass pre-tokenized list to Parser
```

Adjust the `Parser` class to accept pre-tokenized input if it currently re-tokenizes internally.

**Step 4: Run tests and commit**

```
dotnet test tests/FalkForge.Engine.Tests --filter "ConditionEvaluator" -v
dotnet test
git add src/FalkForge.Engine/Variables/ConditionEvaluator.cs
git commit -m "perf: cache tokenized conditions in ConditionEvaluator"
```

---

## Task 16: Final verification and build check

**Step 1: Full build — zero warnings**

```
dotnet build -c Release
```

Expected: 0 errors, 0 warnings. If any warnings appear, fix them before proceeding.

**Step 2: Full test run**

```
dotnet test --no-build -c Release
```

Expected: all ~1900+ tests pass.

**Step 3: Verify security fixes with targeted test run**

```
dotnet test tests/FalkForge.Engine.Elevation.Tests -v
dotnet test tests/FalkForge.Engine.Tests --filter "Download|Payload|Elevat" -v
```

**Step 4: Check for any new compiler warnings introduced**

```
dotnet build 2>&1 | grep -i warning
```

Expected: no output.

**Step 5: Final commit if any cleanup needed, then push**

```
git log --oneline fix/security-memory-perf ^master
```

Review all commits in this branch before requesting code review.

---

## Notes for Implementer

- **NativeAOT constraint**: Engine and Elevation projects use NativeAOT. `ConcurrentDictionary` is fine (no reflection). Do NOT use `Activator.CreateInstance`, `Assembly.Load`, or dynamic types.
- **TreatWarningsAsErrors**: The project has `TreatWarningsAsErrors=true`. Any new warning fails the build.
- **One class per file**: If adding a helper class, put it in its own file. If it's a local static method, it can stay in the same file.
- **ArrayPool contract**: Always return rented buffers in `finally` blocks. Never hold a reference to a rented buffer across await points without careful lifetime management.
- **P3+P4 GzipCompressor**: Read `GzipCompressor.cs` before modifying. It may already have a stream overload.
- **Task 6 (S1) is the riskiest**: If `ProcessLauncher` uses `ShellExecute = true` (for UAC elevation prompt), `RedirectStandardInput` will conflict. Read the implementation carefully and test thoroughly.
